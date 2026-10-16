using System.Collections.Generic;
using UnityEngine;

/// <summary>A box collider of a place, in the place's local frame (one GameObject per place carries all of them).</summary>
public struct WorldCollider
{
    public Vector3 center, size;
    public WorldCollider(Vector3 center, Vector3 size) { this.center = center; this.size = size; }
}

/// <summary>
/// Low-poly buildings of the open world's places, written straight into a WorldMesh (merged per material):
/// Najdi mud-brick town with souq stalls and a mosque, fuel station, dealership + workshop, grain silos, dairy barn,
/// fodder sheds, dry port with containers, Delta brick village, cotton gin, rice mill, neighbour farms (centre pivots
/// in Najd, strip fields on the Delta). Local frame: +z faces the access road; the selling pad is at (0, 0).
/// Colliders are a few boxes per place (cheap), returned in the local frame.
/// </summary>
public static class WorldBuildings
{
    public static readonly Color Mud = new Color(0.78f, 0.62f, 0.44f), MudDark = new Color(0.64f, 0.49f, 0.34f), Plaster = new Color(0.9f, 0.87f, 0.8f);
    public static readonly Color Brick = new Color(0.6f, 0.34f, 0.25f), DeltaPlaster = new Color(0.85f, 0.79f, 0.66f), Concrete = new Color(0.68f, 0.67f, 0.64f);
    public static readonly Color Door = new Color(0.22f, 0.38f, 0.34f), Dark = new Color(0.16f, 0.16f, 0.17f), Steel = new Color(0.62f, 0.63f, 0.65f);
    public static readonly Color Asphalt = new Color(0.2f, 0.2f, 0.21f), Glass = new Color(0.3f, 0.45f, 0.55f);
    private static readonly Color[] Awnings = { new Color(0.85f, 0.3f, 0.2f), new Color(0.95f, 0.75f, 0.25f), new Color(0.25f, 0.5f, 0.75f), new Color(0.9f, 0.9f, 0.85f), new Color(0.3f, 0.6f, 0.35f) };

    private class Ctx
    {
        public WorldMesh m; public List<WorldCollider> cols; public Vector3 o; public Quaternion rot; public bool delta; public System.Random rnd;
        public Vector3 W(float x, float z, float y = 0f) => o + rot * new Vector3(x, y, z);
        public Quaternion R(float yaw) => rot * Quaternion.Euler(0f, yaw, 0f);
    }

    /// <summary>Build a place. o = pad centre on the (flattened) ground, yaw = facing of its road.</summary>
    public static void Build(WorldMesh m, List<WorldCollider> cols, WorldPoi p, bool delta, int seed)
    {
        var c = new Ctx { m = m, cols = cols, o = p.pos, rot = Quaternion.Euler(0f, p.yaw, 0f), delta = delta, rnd = new System.Random(seed) };
        switch (p.kind)
        {
            case PoiKind.Town: Town(c, p); break;
            case PoiKind.Fuel: Fuel(c); break;
            case PoiKind.Dealer: Dealer(c); break;
            case PoiKind.Silo: Silo(c); break;
            case PoiKind.Dairy: Dairy(c); break;
            case PoiKind.Fodder: Fodder(c); break;
            case PoiKind.Port: Port(c); break;
            case PoiKind.Gin: Gin(c); break;
            case PoiKind.RiceMill: RiceMill(c); break;
            case PoiKind.VillageSouq: Village(c); break;
            case PoiKind.Neighbour: Neighbour(c); break;
            case PoiKind.Land: Land(c, p); break;
        }
        if (p.Sells) SellPad(c, 0f, 0f, 7f, p.color);
    }

    // ------------------------------------------------------------------ parts

    private static void Col(Ctx c, float x, float z, float y, Vector3 size) => c.cols.Add(new WorldCollider(new Vector3(x, y + size.y * 0.5f, z), size));

    /// <summary>Flat-roofed house with a parapet, door and windows. Najd: mud brick; Delta: brick or plaster.</summary>
    private static void House(Ctx c, float x, float z, float w, float d, float h, float yaw)
    {
        Color wall = c.delta ? (c.rnd.NextDouble() < 0.55 ? Brick : DeltaPlaster) : (c.rnd.NextDouble() < 0.7 ? Mud : Plaster);
        var r = c.R(yaw);
        Vector3 b = c.W(x, z);
        c.m.Box(b, new Vector3(w, h, d), r, wall);
        // parapet
        c.m.Box(b + r * new Vector3(0f, h, d * 0.5f - 0.15f), new Vector3(w, 0.6f, 0.3f), r, wall);
        c.m.Box(b + r * new Vector3(0f, h, -d * 0.5f + 0.15f), new Vector3(w, 0.6f, 0.3f), r, wall);
        // door + windows on the front (+z)
        c.m.Box(b + r * new Vector3(0f, 0f, d * 0.5f + 0.02f), new Vector3(1.1f, 2.1f, 0.08f), r, Door);
        for (int i = -1; i <= 1; i += 2)
            c.m.Box(b + r * new Vector3(i * w * 0.28f, h * 0.55f, d * 0.5f + 0.02f), new Vector3(0.7f, 0.7f, 0.06f), r, Dark);
        if (c.delta && h > 5f) for (int i = -1; i <= 1; i += 2) c.m.Cylinder(b + r * new Vector3(i * w * 0.4f, h, d * 0.4f), 0.05f, 1.2f, 4, Quaternion.identity, Steel, false);   // rebar of the next floor
        if (!c.delta) for (float s = -w * 0.5f + 0.4f; s < w * 0.5f; s += 1.2f) c.m.Box(b + r * new Vector3(s, h + 0.6f, d * 0.5f - 0.15f), new Vector3(0.4f, 0.35f, 0.3f), r, MudDark);   // crenellation
        c.cols.Add(new WorldCollider(new Vector3(x, h * 0.5f, z), RotSize(w, h, d, yaw)));
    }

    // colliders share the place's rotation: houses turned 90 degrees swap width and depth (others use the bounding box)
    private static Vector3 RotSize(float w, float h, float d, float yaw)
    {
        float a = Mathf.Abs(Mathf.DeltaAngle(0f, yaw));
        if (Mathf.Abs(a - 90f) < 1f) return new Vector3(d, h, w);
        if (a < 1f || Mathf.Abs(a - 180f) < 1f) return new Vector3(w, h, d);
        float s = Mathf.Max(w, d); return new Vector3(s, h, s);
    }

    private static void Stall(Ctx c, float x, float z, float yaw, Color awning)
    {
        var r = c.R(yaw);
        Vector3 b = c.W(x, z);
        var wood = new Color(0.52f, 0.36f, 0.22f);
        c.m.Box(b, new Vector3(3f, 1f, 1.2f), r, wood);
        for (int i = 0; i < 4; i++) c.m.Box(b + r * new Vector3(i % 2 == 0 ? -1.45f : 1.45f, 0f, i < 2 ? -0.6f : 0.6f), new Vector3(0.1f, 2.6f, 0.1f), r, wood);
        c.m.Box(b + r * new Vector3(0f, 2.6f, 0.2f), new Vector3(3.3f, 0.08f, 2f), r * Quaternion.Euler(-8f, 0f, 0f), awning);
        for (int i = 0; i < 4; i++) c.m.Box(b + r * new Vector3(-1.1f + i * 0.72f, 1f, 0f), new Vector3(0.5f, 0.25f, 0.5f), r, new Color(0.62f, 0.45f, 0.26f));
        Col(c, x, z, 0f, RotSize(3f, 1.2f, 1.2f, yaw));
    }

    private static void SellPad(Ctx c, float x, float z, float r, Color color)
    {
        Vector3 p = c.W(x, z);
        c.m.Disc(p + Vector3.up * 0.06f, r, 20, Asphalt);
        c.m.Ring(p + Vector3.up * 0.08f, r - 0.35f, r, 24, color * 0.85f);
        Vector3 pole = c.W(x + r + 0.6f, z);
        c.m.Cylinder(pole, 0.15f, 8f, 6, Quaternion.identity, color * 0.8f);
        c.m.Dome(pole + Vector3.up * 8.3f, 0.6f, color, false);
    }

    private static void Palm(Ctx c, float x, float z) => c.m.DatePalm(c.W(x, z), c.rnd);
    private static void Tree(Ctx c, float x, float z) { if (c.delta) c.m.Eucalyptus(c.W(x, z), c.rnd); else c.m.Acacia(c.W(x, z), c.rnd); }

    private static void TractorProp(Ctx c, float x, float z, float yaw, Color body)
    {
        var r = c.R(yaw);
        Vector3 b = c.W(x, z);
        c.m.Box(b + r * new Vector3(0f, 0.7f, 0.4f), new Vector3(1.2f, 1.1f, 2.2f), r, body, 0.4f);
        c.m.Box(b + r * new Vector3(0f, 1.8f, -0.5f), new Vector3(1.3f, 1.3f, 1.2f), r, Glass, 0.8f);
        foreach (var w in new[] { new Vector3(-0.85f, 0f, -0.6f), new Vector3(0.85f, 0f, -0.6f) })
            c.m.Cylinder(b + r * (w + new Vector3(0f, 0.85f, 0f)), 0.85f, 0.45f, 10, r * Quaternion.Euler(0f, 0f, 90f), Dark);
        foreach (var w in new[] { new Vector3(-0.7f, 0f, 1.2f), new Vector3(0.7f, 0f, 1.2f) })
            c.m.Cylinder(b + r * (w + new Vector3(0f, 0.5f, 0f)), 0.5f, 0.3f, 10, r * Quaternion.Euler(0f, 0f, 90f), Dark);
        Col(c, x, z, 0f, new Vector3(2.2f, 2.4f, 3.2f));
    }

    // ------------------------------------------------------------------ places

    private static void Town(Ctx c, WorldPoi p)
    {
        // souq stalls around the market pad
        for (int i = 0; i < 6; i++)
        {
            float a = -60f + i * 24f;
            var d = Quaternion.Euler(0f, a, 0f) * Vector3.back * 13f;
            Stall(c, d.x, d.z, a, Awnings[i % Awnings.Length]);
        }
        // mosque with minaret and dome behind the square
        c.m.Box(c.W(0f, -30f), new Vector3(14f, 7f, 14f), c.rot, c.delta ? DeltaPlaster : Plaster);
        c.m.Dome(c.W(0f, -30f, 7f), 4.6f, c.delta ? new Color(0.35f, 0.55f, 0.45f) : Plaster);
        c.m.Cylinder(c.W(8.5f, -23.5f), 1.2f, 21f, 8, Quaternion.identity, c.delta ? DeltaPlaster : Plaster);
        c.m.Cylinder(c.W(8.5f, -23.5f, 15f), 1.6f, 0.6f, 8, Quaternion.identity, MudDark);
        c.m.Cone(c.W(8.5f, -23.5f, 21f), 1.3f, 2.6f, 8, new Color(0.35f, 0.55f, 0.45f));
        Col(c, 0f, -30f, 0f, new Vector3(14f, 9f, 14f));
        Col(c, 8.5f, -23.5f, 0f, new Vector3(2.6f, 21f, 2.6f));
        // houses around the square, leaving the road open
        float rad = p.radius - 9f;
        for (int i = 0; i < 14; i++)
        {
            float a = 120f + i * (240f / 13f);
            float rr = rad + (i % 2) * 6f;
            Vector3 d = Quaternion.Euler(0f, a, 0f) * Vector3.forward * rr;
            if (Mathf.Abs(d.x) < 9f && d.z < -22f) continue;   // mosque
            float yaw = Mathf.Round(a / 90f) * 90f;
            yaw = Mathf.Abs(Mathf.DeltaAngle(yaw, 0f)) < 1f || Mathf.Abs(Mathf.DeltaAngle(yaw, 180f)) < 1f ? 0f : 90f;
            House(c, d.x, d.z, 7f + (float)c.rnd.NextDouble() * 4f, 7f + (float)c.rnd.NextDouble() * 3f, c.delta ? 6f + (i % 3) * 3f : 4f + (i % 3) * 1.2f, yaw);
        }
        for (int i = 0; i < 6; i++) { var d = Quaternion.Euler(0f, 40f + i * 56f, 0f) * Vector3.forward * 20f; if (d.z > 5f) continue; Palm(c, d.x, d.z); }
    }

    private static void Fuel(Ctx c)
    {
        c.m.Disc(c.o + Vector3.up * 0.05f, 12f, 20, Asphalt);
        c.m.Box(c.W(0f, 0f, 5f), new Vector3(14f, 0.6f, 9f), c.rot, Plaster);
        c.m.Box(c.W(0f, 0f, 5.6f), new Vector3(14.2f, 0.5f, 9.2f), c.rot, new Color(0.85f, 0.2f, 0.18f));
        for (int i = 0; i < 4; i++) c.m.Box(c.W(i % 2 == 0 ? -6f : 6f, i < 2 ? -3.5f : 3.5f), new Vector3(0.4f, 5f, 0.4f), c.rot, Steel);
        for (int i = -1; i <= 1; i += 2)
        {
            c.m.Box(c.W(i * 2.5f, 0f), new Vector3(1.4f, 0.25f, 3f), c.rot, Concrete);
            c.m.Box(c.W(i * 2.5f, 0f, 0.25f), new Vector3(0.7f, 1.7f, 0.5f), c.rot, new Color(0.85f, 0.2f, 0.18f));
            Col(c, i * 2.5f, 0f, 0f, new Vector3(1.4f, 2f, 3f));
        }
        c.m.Box(c.W(0f, -12f), new Vector3(11f, 4f, 6f), c.rot, Plaster);
        c.m.Box(c.W(0f, -8.98f, 0f), new Vector3(4f, 2.4f, 0.06f), c.rot, Glass);
        Col(c, 0f, -12f, 0f, new Vector3(11f, 4f, 6f));
        c.m.Cylinder(c.W(9f, 8f), 0.2f, 7f, 6, Quaternion.identity, Steel);
        c.m.Box(c.W(9f, 8f, 7f), new Vector3(2.4f, 2.6f, 0.3f), c.rot, new Color(0.85f, 0.2f, 0.18f));
    }

    private static void Dealer(Ctx c)
    {
        c.m.Disc(c.o + Vector3.up * 0.05f, 22f, 24, Concrete);
        c.m.Box(c.W(-6f, -17f), new Vector3(20f, 7f, 11f), c.rot, Plaster);
        c.m.Box(c.W(-6f, -11.45f, 0.4f), new Vector3(18f, 5.4f, 0.08f), c.rot, Glass, 0.85f);
        c.m.Box(c.W(-6f, -17f, 7f), new Vector3(20.4f, 0.8f, 11.4f), c.rot, new Color(0.2f, 0.45f, 0.8f));
        Col(c, -6f, -17f, 0f, new Vector3(20f, 7.8f, 11f));
        // workshop shed with an open door (the repair bay)
        c.m.Box(c.W(14f, -15f), new Vector3(12f, 7f, 13f), c.rot, Concrete);
        c.m.Gable(c.W(14f, -15f, 7f), 12.4f, 13.4f, 2.2f, c.R(0f), Steel);
        c.m.Box(c.W(14f, -8.47f), new Vector3(6f, 5f, 0.06f), c.rot, Dark);
        Col(c, 14f, -15f, 0f, new Vector3(12f, 9f, 13f));
        TractorProp(c, -12f, 4f, 30f, new Color(0.15f, 0.55f, 0.25f));
        TractorProp(c, -5f, 6f, 20f, new Color(0.85f, 0.25f, 0.15f));
        TractorProp(c, 2f, 7f, 10f, new Color(0.2f, 0.35f, 0.75f));
        c.m.Cylinder(c.W(-18f, 10f), 0.25f, 9f, 6, Quaternion.identity, Steel);
        c.m.Box(c.W(-18f, 10f, 9f), new Vector3(5f, 2.2f, 0.4f), c.rot, new Color(0.2f, 0.45f, 0.8f));
    }

    private static void Silo(Ctx c)
    {
        for (int i = -1; i <= 1; i++)
        {
            c.m.Cylinder(c.W(i * 9f, -17f), 4f, 17f, 12, Quaternion.identity, Steel, true, 0.5f);
            c.m.Cone(c.W(i * 9f, -17f, 17f), 4.1f, 2.6f, 12, Steel);
            Col(c, i * 9f, -17f, 0f, new Vector3(8f, 19f, 8f));
        }
        c.m.Box(c.W(0f, -9f, 6f), new Vector3(1.2f, 1.2f, 22f), c.R(0f) * Quaternion.Euler(-28f, 0f, 0f), Steel);
        c.m.Box(c.W(17f, -4f), new Vector3(10f, 6f, 14f), c.rot, Concrete);
        c.m.Gable(c.W(17f, -4f, 6f), 10.4f, 14.4f, 2f, c.R(0f), Steel);
        Col(c, 17f, -4f, 0f, new Vector3(10f, 8f, 14f));
        c.m.Box(c.W(-16f, 4f), new Vector3(3f, 3f, 3f), c.rot, new Color(0.8f, 0.7f, 0.4f));
    }

    private static void Dairy(Ctx c)
    {
        Color wall = c.delta ? DeltaPlaster : Plaster;
        c.m.Box(c.W(0f, -18f), new Vector3(30f, 5f, 12f), c.rot, wall);
        c.m.Gable(c.W(0f, -18f, 5f), 12.6f, 30.6f, 3f, c.R(90f), new Color(0.55f, 0.25f, 0.2f));
        Col(c, 0f, -18f, 0f, new Vector3(30f, 8f, 12f));
        c.m.Cylinder(c.W(18.5f, -2f, 1.6f), 1.6f, 7f, 10, c.R(0f) * Quaternion.Euler(0f, 0f, 90f), Steel, true, 0.6f);
        c.m.Box(c.W(11.5f, -2f), new Vector3(0.4f, 1.6f, 2.6f), c.rot, Steel);
        c.m.Box(c.W(18.5f, -2f), new Vector3(0.4f, 1.6f, 2.6f), c.rot, Steel);
        Col(c, 15f, -2f, 0f, new Vector3(7.4f, 3.4f, 3.4f));
        for (int i = 0; i < 6; i++) c.m.Box(c.W(-14f + i * 2.5f, -8f), new Vector3(0.12f, 1.2f, 0.12f), c.rot, new Color(0.5f, 0.35f, 0.2f));
    }

    private static void Fodder(Ctx c)
    {
        var hay = new Color(0.85f, 0.74f, 0.42f);
        for (int i = 0; i < 6; i++) c.m.Box(c.W(i % 3 * 9f - 9f, i < 3 ? -13f : -19f), new Vector3(0.4f, 5f, 0.4f), c.rot, Steel);
        c.m.Box(c.W(0f, -16f, 5f), new Vector3(20f, 0.3f, 9f), c.rot, Steel);
        for (int x = -2; x <= 2; x++) for (int y = 0; y < 3; y++) c.m.Box(c.W(x * 3.2f, -16f, y * 1.2f), new Vector3(3f, 1.2f, 7f), c.rot, hay);
        Col(c, 0f, -16f, 0f, new Vector3(20f, 5.3f, 9f));
        for (int i = 0; i < 4; i++) { c.m.Box(c.W(14f, -6f + i * 3f), new Vector3(2.2f, 1.1f, 2.2f), c.rot, hay); }
        Col(c, 14f, -1.5f, 0f, new Vector3(2.4f, 1.2f, 12f));
    }

    private static void Port(Ctx c)
    {
        c.m.Disc(c.o + Vector3.up * 0.04f, 40f, 28, Concrete);
        c.m.Box(c.W(-16f, -24f), new Vector3(30f, 10f, 16f), c.rot, Concrete);
        c.m.Gable(c.W(-16f, -24f, 10f), 16.4f, 30.4f, 2.5f, c.R(90f), Steel);
        Col(c, -16f, -24f, 0f, new Vector3(30f, 12.5f, 16f));
        Color[] box = { new Color(0.75f, 0.2f, 0.15f), new Color(0.15f, 0.35f, 0.6f), new Color(0.2f, 0.5f, 0.3f), new Color(0.85f, 0.6f, 0.15f), new Color(0.45f, 0.45f, 0.48f) };
        for (int i = 0; i < 4; i++)
            for (int j = 0; j < 3; j++)
            {
                int stack = 1 + (i * 3 + j * 5) % 3;
                for (int k = 0; k < stack; k++) c.m.Box(c.W(12f + j * 3.2f, -8f - i * 7f, k * 2.6f), new Vector3(2.5f, 2.6f, 6.1f), c.rot, box[(i + j + k) % box.Length], 0.3f);
            }
        Col(c, 15.2f, -18.5f, 0f, new Vector3(9.2f, 7.8f, 28f));
        // gantry crane over the container yard
        foreach (var x in new[] { 8.5f, 23f }) foreach (var z in new[] { -2f, -34f }) c.m.Box(c.W(x, z), new Vector3(0.8f, 13f, 0.8f), c.rot, new Color(0.9f, 0.7f, 0.15f));
        c.m.Box(c.W(8.5f, -18f, 13f), new Vector3(1f, 1.2f, 33f), c.rot, new Color(0.9f, 0.7f, 0.15f));
        c.m.Box(c.W(23f, -18f, 13f), new Vector3(1f, 1.2f, 33f), c.rot, new Color(0.9f, 0.7f, 0.15f));
        c.m.Box(c.W(15.75f, -18f, 13.2f), new Vector3(15.5f, 1f, 1.4f), c.rot, new Color(0.9f, 0.7f, 0.15f));
    }

    private static void Gin(Ctx c)
    {
        c.m.Box(c.W(0f, -18f), new Vector3(24f, 8f, 14f), c.rot, DeltaPlaster);
        c.m.Gable(c.W(0f, -18f, 8f), 14.4f, 24.4f, 3f, c.R(90f), Steel);
        Col(c, 0f, -18f, 0f, new Vector3(24f, 11f, 14f));
        for (int i = 0; i < 4; i++) c.m.Cone(c.W(15f + (i % 2) * 6f, -4f - (i / 2) * 7f), 3f, 2.6f, 9, new Color(0.96f, 0.95f, 0.92f));
        Col(c, 18f, -7.5f, 0f, new Vector3(12f, 2.6f, 13f));
        c.m.Cylinder(c.W(-10f, -9f), 0.6f, 14f, 8, Quaternion.identity, Brick);
    }

    private static void RiceMill(Ctx c)
    {
        c.m.Box(c.W(-4f, -18f), new Vector3(16f, 12f, 14f), c.rot, Brick);
        c.m.Box(c.W(-4f, -18f, 12f), new Vector3(16.4f, 0.6f, 14.4f), c.rot, Concrete);
        Col(c, -4f, -18f, 0f, new Vector3(16f, 12.6f, 14f));
        c.m.Cylinder(c.W(8f, -22f), 1.1f, 24f, 10, Quaternion.identity, Brick);
        Col(c, 8f, -22f, 0f, new Vector3(2.2f, 24f, 2.2f));
        c.m.Cylinder(c.W(12f, -10f), 3.2f, 10f, 12, Quaternion.identity, Steel, true, 0.5f);
        c.m.Cone(c.W(12f, -10f, 10f), 3.3f, 2f, 12, Steel);
        Col(c, 12f, -10f, 0f, new Vector3(6.4f, 12f, 6.4f));
        for (int i = 0; i < 5; i++) c.m.Box(c.W(-14f, -4f + i * 1.0f, (i % 2) * 0.5f), new Vector3(1.6f, 0.5f, 0.9f), c.rot, new Color(0.92f, 0.9f, 0.82f));
    }

    private static void Village(Ctx c)
    {
        for (int i = 0; i < 8; i++)
        {
            float x = -12f + (i % 4) * 8f, z = i < 4 ? -11f : 11f;
            if (z > 0f && Mathf.Abs(x) < 6f) continue;
            Stall(c, x, z, i < 4 ? 0f : 180f, Awnings[i % Awnings.Length]);
        }
        for (int i = 0; i < 6; i++)
        {
            float a = 130f + i * 20f;
            Vector3 d = Quaternion.Euler(0f, a, 0f) * Vector3.forward * 26f;
            House(c, d.x, d.z, 7f, 7f, 6f + (i % 2) * 3f, 0f);
        }
        c.m.Cylinder(c.W(-20f, -2f), 2.4f, 9f, 10, Quaternion.identity, DeltaPlaster);   // dovecote
        c.m.Cone(c.W(-20f, -2f, 9f), 2.6f, 3f, 10, MudDark);
        Col(c, -20f, -2f, 0f, new Vector3(5f, 12f, 5f));
    }

    private static void Neighbour(Ctx c)
    {
        if (!c.delta)
        {
            // centre pivot: a green circle of alfalfa / wheat with the pivot arm on wheeled towers
            Vector3 hub = c.W(0f, -10f);
            c.m.Disc(hub + Vector3.up * 0.07f, 44f, 36, new Color(0.38f, 0.5f, 0.2f), 0.05f);
            c.m.Ring(hub + Vector3.up * 0.08f, 44f, 46f, 36, new Color(0.5f, 0.38f, 0.26f));
            c.m.Cylinder(hub, 0.6f, 4f, 8, Quaternion.identity, Steel);
            float yaw = (float)c.rnd.NextDouble() * 360f;
            var r = Quaternion.Euler(0f, yaw, 0f);
            c.m.Box(hub + r * new Vector3(0f, 3.6f, 21f), new Vector3(0.3f, 0.3f, 42f), r, Steel);
            for (int i = 1; i <= 4; i++) c.m.Box(hub + r * new Vector3(0f, 0f, i * 10f), new Vector3(1.6f, 3.6f, 0.4f), r, Steel);
            Col(c, 0f, -10f, 0f, new Vector3(1.2f, 4f, 1.2f));
            House(c, 44f, 30f, 10f, 9f, 4.2f, 0f);
            TractorProp(c, 36f, 40f, 140f, new Color(0.15f, 0.55f, 0.25f));
            for (int i = 0; i < 5; i++) Palm(c, 50f + (i % 2) * 5f, 18f - i * 5f);
        }
        else
        {
            Color[] crops = { new Color(0.33f, 0.5f, 0.22f), new Color(0.58f, 0.6f, 0.3f), new Color(0.4f, 0.55f, 0.25f), new Color(0.35f, 0.28f, 0.19f) };
            for (int i = 0; i < 4; i++)
            {
                float x = -36f + i * 19f;
                Vector3 b = c.W(x, -14f, 0.02f);
                c.m.Box(b, new Vector3(17f, 0.12f, 46f), c.rot, crops[i]);
                for (int k = -3; k <= 3; k++) c.m.Box(b + c.rot * new Vector3(k * 2.2f, 0.12f, 0f), new Vector3(0.5f, 0.35f, 44f), c.rot, crops[i] * 0.85f);
            }
            House(c, 40f, 32f, 9f, 9f, 9f, 0f);
            c.m.Cylinder(c.W(52f, 26f), 2.2f, 8f, 10, Quaternion.identity, DeltaPlaster);   // pigeon tower
            c.m.Cone(c.W(52f, 26f, 8f), 2.4f, 2.6f, 10, MudDark);
            Col(c, 52f, 26f, 0f, new Vector3(4.6f, 10f, 4.6f));
            TractorProp(c, 30f, 42f, 160f, new Color(0.2f, 0.35f, 0.75f));
            for (int i = 0; i < 6; i++) c.m.Eucalyptus(c.W(-44f + i * 16f, 16f), c.rnd);
        }
    }

    private static void Land(Ctx c, WorldPoi p)
    {
        var l = p.parcel;
        if (l == null) return;
        var hx = l.HalfExtents;
        var wood = new Color(0.5f, 0.36f, 0.22f);
        // corner stakes and a low fence line along the back
        foreach (var s in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(1, 1) })
            c.m.Box(l.center + l.rot * new Vector3(s.x * hx.x, 0f, s.y * hx.y), new Vector3(0.18f, 1.3f, 0.18f), l.rot, wood);
        for (float x = -hx.x; x < hx.x; x += 3f)
            c.m.Box(l.center + l.rot * new Vector3(x + 1.5f, 0.7f, -hx.y), new Vector3(3f, 0.08f, 0.06f), l.rot, wood);
    }
}
