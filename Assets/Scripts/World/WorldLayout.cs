using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>What a point of interest on the open world map is.</summary>
public enum PoiKind { Farm, Town, Fuel, Dealer, Silo, Dairy, Fodder, Port, Gin, RiceMill, VillageSouq, Neighbour, Land }

/// <summary>A place on the open world map (town market, fuel station, silo, neighbour farm, land for sale...).</summary>
public class WorldPoi
{
    public string id, nameEn, nameAr, letter;
    public PoiKind kind;
    public string sellPoint;        // MarketManager point id sold to on this POI's pad ("" = none)
    public Vector3 pos;             // centre (y = flattened pad height, relative to the core terrain's base)
    public float radius;            // flattened pad radius
    public float yaw;               // buildings face their access road
    public bool asphalt;            // access road kind
    public int node = -1;           // graph node of the POI
    public Vector3 gate;            // where the access road ends (pad edge)
    public Vector3 arrival;         // fast travel spot (on the access road, outside the buildings)
    public Color color = Color.white;
    public LandParcel parcel;       // Land only
    public bool Sells => !string.IsNullOrEmpty(sellPoint);
}

/// <summary>Expansion farmland on the open world map: a block of field plots bought from its sign (WorldLand).</summary>
public class LandParcel
{
    public string id;
    public int rows, cols, price, indexBase;
    public float plotSize = 10f, gap = 1.4f;
    public Vector3 center;
    public Quaternion rot = Quaternion.identity;
    public int Plots => rows * cols;
    public Vector2 HalfExtents => new Vector2(cols * (plotSize + gap) * 0.5f + 3f, rows * (plotSize + gap) * 0.5f + 3f);
    public Vector3 PlotPos(int r, int c) => center + rot * new Vector3((c - (cols - 1) * 0.5f) * (plotSize + gap), 0f, (r - (rows - 1) * 0.5f) * (plotSize + gap));
}

/// <summary>Road graph node: a junction, a POI or the farm gate.</summary>
public class WorldNode
{
    public Vector3 pos;
    public float h;                         // road height at the node (relative to the core base)
    public readonly List<int> roads = new List<int>();
    public int poi = -1;                    // index of the POI that owns this node (-1 = junction)
    public bool sign;                       // a signpost stands here
}

/// <summary>A road between two graph nodes: asphalt highway / farm road or dirt track, as a polyline with a height profile.</summary>
public class WorldRoad
{
    public int a, b;
    public bool asphalt;
    public bool inCore;                     // inside the scene terrain: draped on the ground, terrain not flattened
    public float width;
    public readonly List<Vector3> path = new List<Vector3>();
    public float[] profile;                 // road surface height per path point (relative to the core base)
    public float Length { get { float l = 0f; for (int i = 1; i < path.Count; i++) l += Flat(path[i] - path[i - 1]).magnitude; return l; } }
    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
}

/// <summary>Region defaults for a POI, in fractions of the world half-size from the core centre.</summary>
public struct PoiDef
{
    public string id, nameEn, nameAr, sell, letter;
    public PoiKind kind;
    public float fx, fz, radius;
    public bool asphalt;
    public PoiDef(string id, PoiKind kind, string nameEn, string nameAr, string sell, float fx, float fz, float radius, bool asphalt, string letter)
    { this.id = id; this.kind = kind; this.nameEn = nameEn; this.nameAr = nameAr; this.sell = sell; this.fx = fx; this.fz = fz; this.radius = radius; this.asphalt = asphalt; this.letter = letter; }
}

/// <summary>
/// The open world around the farm, as data: world bounds (much larger than the scene terrain), points of interest,
/// expansion land, neighbour farms and a road graph (asphalt ring highway with exits, asphalt / dirt access roads)
/// that connects the farm gate to every place. Region-aware (Najd desert or Nile Delta). Pure logic (no scene objects),
/// so tests can check it: Reachable() is a graph search from the farm.
/// </summary>
public class WorldLayout
{
    public const float MinHalfSize = 1200f;        // world half-size from the core centre (2.4 km x 2.4 km at least)
    public const float RoadStep = 8f;

    public string Region;
    public Vector3 CoreMin, CoreMax, Center;
    public float Half;
    public Vector3 Min => new Vector3(Center.x - Half, 0f, Center.z - Half);
    public Vector3 Max => new Vector3(Center.x + Half, 0f, Center.z + Half);
    public readonly List<WorldPoi> Pois = new List<WorldPoi>();
    public readonly List<WorldNode> Nodes = new List<WorldNode>();
    public readonly List<WorldRoad> Roads = new List<WorldRoad>();
    public readonly List<LandParcel> Parcels = new List<LandParcel>();
    public readonly List<Vector3> Ring = new List<Vector3>();     // the ring highway (closed polyline)
    public int FarmPoi = -1;
    public bool Delta => Region == "nile_delta";

    // ------------------------------------------------------------------ region data

    public static readonly PoiDef[] NajdPois =
    {
        new PoiDef("town", PoiKind.Town, "Town market", "سوق البلدة", "souq", 0.62f, 0.36f, 46f, true, "T"),
        new PoiDef("fuel", PoiKind.Fuel, "Fuel station", "محطة الوقود", "", 0.42f, -0.40f, 22f, true, "F"),
        new PoiDef("dealer", PoiKind.Dealer, "Dealership & workshop", "معرض وورشة المعدات", "", 0.44f, 0.15f, 30f, true, "D"),
        new PoiDef("silo", PoiKind.Silo, "Grain silo", "صوامع الحبوب", "mill", -0.36f, 0.60f, 30f, false, "S"),
        new PoiDef("dairy", PoiKind.Dairy, "Dairy co-op", "جمعية الألبان", "dairy", -0.52f, -0.46f, 30f, false, "M"),
        new PoiDef("fodder", PoiKind.Fodder, "Fodder traders", "تجار الأعلاف", "fodder", 0.06f, 0.76f, 26f, false, "H"),
        new PoiDef("port", PoiKind.Port, "Dry port (export)", "الميناء الجاف (تصدير)", "port", 0.82f, -0.78f, 46f, true, "P"),
        new PoiDef("n_abufahd", PoiKind.Neighbour, "Abu Fahd's farm", "مزرعة أبو فهد", "", -0.30f, 0.24f, 70f, false, "N"),
        new PoiDef("n_alotaibi", PoiKind.Neighbour, "Al-Otaibi farm", "مزرعة العتيبي", "", 0.18f, -0.66f, 70f, false, "N"),
        new PoiDef("n_alharbi", PoiKind.Neighbour, "Al-Harbi farm", "مزرعة الحربي", "", 0.80f, 0.74f, 70f, false, "N"),
    };

    public static readonly PoiDef[] DeltaPois =
    {
        new PoiDef("town", PoiKind.Town, "Town market", "سوق البندر", "souq", 0.66f, 0.40f, 46f, true, "T"),
        new PoiDef("village", PoiKind.VillageSouq, "Village souq", "سوق القرية", "village", -0.42f, 0.62f, 34f, false, "V"),
        new PoiDef("gin", PoiKind.Gin, "Cotton gin", "محلج القطن", "gin", 0.30f, -0.62f, 30f, false, "G"),
        new PoiDef("ricemill", PoiKind.RiceMill, "Rice mill", "مضرب الرز", "ricemill", -0.62f, -0.30f, 30f, false, "R"),
        new PoiDef("dairy", PoiKind.Dairy, "Dairy co-op", "جمعية الألبان", "dairy", 0.06f, 0.74f, 28f, false, "M"),
        new PoiDef("fuel", PoiKind.Fuel, "Fuel station", "بنزينة", "", 0.46f, -0.34f, 22f, true, "F"),
        new PoiDef("dealer", PoiKind.Dealer, "Dealership & workshop", "معرض وورشة المعدات", "", 0.44f, 0.12f, 30f, true, "D"),
        new PoiDef("port", PoiKind.Port, "Damietta port (export)", "ميناء دمياط (تصدير)", "port", 0.84f, -0.82f, 46f, true, "P"),
        new PoiDef("n_hassan", PoiKind.Neighbour, "Hajj Mahmoud's farm", "غيط الحاج محمود", "", -0.28f, 0.24f, 70f, false, "N"),
        new PoiDef("n_saad", PoiKind.Neighbour, "Saad's farm", "غيط سعد", "", 0.12f, -0.66f, 70f, false, "N"),
        new PoiDef("n_fathy", PoiKind.Neighbour, "Fathy's farm", "غيط فتحي", "", 0.80f, 0.70f, 70f, false, "N"),
    };

    /// <summary>Expansion land for sale (same spots on both maps, around the farm): id, fx, fz, rows, cols, price.</summary>
    public static readonly (string id, float fx, float fz, int rows, int cols, int price)[] LandDefs =
    {
        ("land_west", -0.27f, -0.08f, 2, 2, 3000),
        ("land_east", 0.24f, -0.04f, 2, 3, 5000),
        ("land_north", 0.10f, 0.34f, 3, 3, 9000),
    };

    private static readonly Vector2[] NajdRing = { new Vector2(0.30f, -0.30f), new Vector2(-0.05f, -0.34f), new Vector2(-0.40f, -0.26f), new Vector2(-0.55f, 0.02f), new Vector2(-0.47f, 0.40f), new Vector2(-0.14f, 0.66f), new Vector2(0.24f, 0.70f), new Vector2(0.52f, 0.48f), new Vector2(0.66f, 0.14f), new Vector2(0.60f, -0.16f) };
    private static readonly Vector2[] DeltaRing = { new Vector2(0.32f, -0.34f), new Vector2(-0.32f, -0.36f), new Vector2(-0.50f, -0.10f), new Vector2(-0.48f, 0.36f), new Vector2(-0.12f, 0.58f), new Vector2(0.30f, 0.60f), new Vector2(0.55f, 0.36f), new Vector2(0.58f, -0.08f) };
    /// <summary>Highway exits to the edge of the map (east and south), as directions from the ring.</summary>
    private static readonly Vector2[] Exits = { new Vector2(1f, 0.1f), new Vector2(-0.05f, -1f) };

    public static PoiDef[] PoiDefs(string region) => region == "nile_delta" ? DeltaPois : NajdPois;

    public static string LandNameEn => "Farmland for sale";
    public static string LandNameAr => "أرض زراعية للبيع";

    // ------------------------------------------------------------------ build

    /// <summary>Obstacle on the scene terrain (plot, greenhouse, pen, loading bay, start) for routing the farm road.</summary>
    public struct Obstacle { public Vector3 p; public float r; public Obstacle(Vector3 p, float r) { this.p = p; this.r = r; } }

    /// <summary>Lay out the world around the scene terrain rect (coreMin..coreMax). farmPad = the farm's market pad (the
    /// farm road starts there); obstacles = farm features the road must not cross; coreHeight = scene ground height.</summary>
    public static WorldLayout Create(string region, Vector3 coreMin, Vector3 coreMax, Vector3 farmPad, IList<Obstacle> obstacles, Func<Vector3, float> coreHeight, Func<float, float, float> rawHeight)
    {
        var w = new WorldLayout { Region = region ?? Regions.DefaultId, CoreMin = Flat(coreMin), CoreMax = Flat(coreMax) };
        w.Center = (w.CoreMin + w.CoreMax) * 0.5f;
        w.Half = HalfFor(coreMin, coreMax);
        w.BuildRing(w.Delta ? DeltaRing : NajdRing);
        // farm POI + farm road
        var farm = new WorldPoi { id = "farm", kind = PoiKind.Farm, nameEn = "Your farm", nameAr = "مزرعتك", letter = "", pos = Flat(farmPad), radius = 8f, asphalt = true, color = new Color(0.35f, 0.85f, 0.35f) };
        w.FarmPoi = w.AddPoi(farm);
        // POIs
        foreach (var d in PoiDefs(w.Region))
        {
            var p = new WorldPoi { id = d.id, kind = d.kind, nameEn = d.nameEn, nameAr = d.nameAr, sellPoint = d.sell, letter = d.letter, radius = d.radius, asphalt = d.asphalt, color = KindColor(d.kind) };
            p.pos = w.OutsideCore(w.Center + new Vector3(d.fx * w.Half, 0f, d.fz * w.Half), d.radius + 70f);
            w.AddPoi(p);
        }
        int li = 0;
        foreach (var l in LandDefs)
        {
            var parcel = new LandParcel { id = l.id, rows = l.rows, cols = l.cols, price = l.price, indexBase = FarmFields.WorldFieldBase + (w.Delta ? 100 : 0) + li++ * 10 };
            var hx = parcel.HalfExtents;
            var p = new WorldPoi { id = l.id, kind = PoiKind.Land, nameEn = LandNameEn, nameAr = LandNameAr, sellPoint = "", letter = "L", radius = Mathf.Max(hx.x, hx.y) + 4f, asphalt = false, color = KindColor(PoiKind.Land), parcel = parcel };
            p.pos = w.OutsideCore(w.Center + new Vector3(l.fx * w.Half, 0f, l.fz * w.Half), p.radius + 45f);
            parcel.center = p.pos;
            w.Parcels.Add(parcel);
            w.AddPoi(p);
        }
        w.Connect(obstacles);
        w.Profiles(coreHeight, rawHeight);
        return w;
    }

    /// <summary>World half-size for a scene terrain rect: at least 1.2 km, four times the scene terrain otherwise.</summary>
    public static float HalfFor(Vector3 coreMin, Vector3 coreMax)
    {
        float coreHalf = Mathf.Max(coreMax.x - coreMin.x, coreMax.z - coreMin.z) * 0.5f;
        return Mathf.Max(MinHalfSize, coreHalf * 4f + 600f);
    }

    /// <summary>Highway exits stop this far inside the edge (the last 160 m rise into the soft edge).</summary>
    public const float ExitInset = 185f;

    public static Color KindColor(PoiKind k)
    {
        switch (k)
        {
            case PoiKind.Town: return new Color(1f, 0.8f, 0.2f);
            case PoiKind.Fuel: return new Color(0.95f, 0.25f, 0.2f);
            case PoiKind.Dealer: return new Color(0.3f, 0.6f, 1f);
            case PoiKind.Neighbour: return new Color(0.6f, 0.75f, 0.5f);
            case PoiKind.Land: return new Color(1f, 0.92f, 0.35f);
            case PoiKind.Farm: return new Color(0.35f, 0.85f, 0.35f);
            default: return new Color(1f, 0.6f, 0.25f);   // selling points
        }
    }

    private int AddPoi(WorldPoi p)
    {
        Pois.Add(p);
        var n = new WorldNode { pos = p.pos, poi = Pois.Count - 1 };
        Nodes.Add(n);
        p.node = Nodes.Count - 1;
        return Pois.Count - 1;
    }

    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    /// <summary>Distance (flat) from p to the scene terrain rect (0 inside).</summary>
    public float CoreDistance(Vector3 p)
    {
        float dx = Mathf.Max(Mathf.Max(CoreMin.x - p.x, 0f), p.x - CoreMax.x), dz = Mathf.Max(Mathf.Max(CoreMin.z - p.z, 0f), p.z - CoreMax.z);
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    public bool InCore(Vector3 p, float pad = 0f) => p.x > CoreMin.x - pad && p.x < CoreMax.x + pad && p.z > CoreMin.z - pad && p.z < CoreMax.z + pad;
    public bool InWorld(Vector3 p, float pad = 0f) => p.x > Center.x - Half + pad && p.x < Center.x + Half - pad && p.z > Center.z - Half + pad && p.z < Center.z + Half - pad;

    /// <summary>Distance to the world's outer edge (negative outside).</summary>
    public float EdgeDistance(Vector3 p) => Mathf.Min(Mathf.Min(p.x - (Center.x - Half), Center.x + Half - p.x), Mathf.Min(p.z - (Center.z - Half), Center.z + Half - p.z));

    // Push a spot out of the scene terrain (a big scene terrain would swallow the fractions).
    private Vector3 OutsideCore(Vector3 p, float clearance)
    {
        p = Flat(p);
        if (!InCore(p, clearance)) return ClampWorld(p, clearance);
        Vector3 dir = p - Center; dir.y = 0f;
        if (dir.sqrMagnitude < 1f) dir = Vector3.forward;
        dir.Normalize();
        for (float s = 0f; s < Half; s += 10f)
        {
            Vector3 q = p + dir * s;
            if (!InCore(q, clearance)) return ClampWorld(q, clearance);
        }
        return ClampWorld(p, clearance);
    }

    private Vector3 ClampWorld(Vector3 p, float pad)
    {
        pad = Mathf.Min(pad + 60f, Half * 0.5f);
        p.x = Mathf.Clamp(p.x, Center.x - Half + pad, Center.x + Half - pad);
        p.z = Mathf.Clamp(p.z, Center.z - Half + pad, Center.z + Half - pad);
        return p;
    }

    // ------------------------------------------------------------------ ring highway

    private void BuildRing(Vector2[] pts)
    {
        var ctrl = new List<Vector3>();
        float ringPad = 110f;
        foreach (var f in pts) ctrl.Add(OutsideCore(Center + new Vector3(f.x * Half, 0f, f.y * Half), ringPad));
        Ring.Clear();
        int n = ctrl.Count;
        for (int i = 0; i < n; i++)
        {
            Vector3 p0 = ctrl[(i - 1 + n) % n], p1 = ctrl[i], p2 = ctrl[(i + 1) % n], p3 = ctrl[(i + 2) % n];
            float len = Flat(p2 - p1).magnitude;
            int steps = Mathf.Max(2, Mathf.CeilToInt(len / RoadStep));
            for (int s = 0; s < steps; s++) Ring.Add(CatmullRom(p0, p1, p2, p3, s / (float)steps));
        }
    }

    public static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    private int NearestRing(Vector3 p)
    {
        int best = 0; float bd = float.MaxValue;
        for (int i = 0; i < Ring.Count; i++) { float d = Flat(Ring[i] - p).sqrMagnitude; if (d < bd) { bd = d; best = i; } }
        return best;
    }

    // ------------------------------------------------------------------ graph

    private void Connect(IList<Obstacle> obstacles)
    {
        // Ring junctions: where an access road, the farm road or a highway exit joins the ring.
        var attach = new List<(int ringIndex, int target, bool asphalt, Vector3 end)>();   // target = node index
        foreach (var p in Pois)
        {
            if (p.kind == PoiKind.Farm) continue;
            int ri = NearestRing(p.pos);
            Vector3 dir = Flat(Ring[ri] - p.pos);
            if (dir.sqrMagnitude < 1f) dir = Vector3.forward;
            dir.Normalize();
            p.yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;   // buildings face their road
            p.gate = p.pos + dir * (p.kind == PoiKind.Land ? p.radius - 2f : p.radius * 0.45f);
            p.arrival = p.pos + dir * (p.radius + 16f);
            if (p.parcel != null) p.parcel.rot = Quaternion.Euler(0f, p.yaw, 0f);
            attach.Add((ri, p.node, p.asphalt, p.gate));
        }
        // Highway exits to the map edge (soft edge far away).
        var exitNodes = new List<int>();
        foreach (var e in Exits)
        {
            Vector3 d = new Vector3(e.x, 0f, e.y).normalized;
            Vector3 far = Center + d * (Half * 1.5f);
            far.x = Mathf.Clamp(far.x, Center.x - Half + ExitInset, Center.x + Half - ExitInset);
            far.z = Mathf.Clamp(far.z, Center.z - Half + ExitInset, Center.z + Half - ExitInset);
            int ri = NearestRing(far);
            var n = new WorldNode { pos = far };
            Nodes.Add(n);
            exitNodes.Add(Nodes.Count - 1);
            attach.Add((ri, Nodes.Count - 1, true, far));
        }
        // Farm road: market pad -> gate on the scene terrain edge -> ring.
        var farm = Pois[FarmPoi];
        Vector3 gate = FarmGate(farm.pos, obstacles);
        var gateNode = new WorldNode { pos = gate, sign = true };
        Nodes.Add(gateNode);
        int gateIndex = Nodes.Count - 1;
        var core = new WorldRoad { a = farm.node, b = gateIndex, asphalt = true, inCore = true, width = 6f };
        AddLine(core.path, farm.pos, gate);
        AddRoad(core);
        Vector3 outDir = OutwardFromCore(gate);
        farm.arrival = farm.pos + Flat(gate - farm.pos).normalized * 14f;
        attach.Add((NearestRing(gate + outDir * 40f), gateIndex, true, gate));

        // Junction nodes on the ring (merge attachments that are close along the ring).
        attach.Sort((x, y) => x.ringIndex.CompareTo(y.ringIndex));
        var ringNodes = new List<(int ringIndex, int node)>();
        foreach (var at in attach)
        {
            int jn = -1;
            foreach (var rn in ringNodes) if (Mathf.Abs(rn.ringIndex - at.ringIndex) * RoadStep < 30f) { jn = rn.node; break; }
            if (jn < 0)
            {
                Nodes.Add(new WorldNode { pos = Ring[at.ringIndex], sign = true });
                jn = Nodes.Count - 1;
                ringNodes.Add((at.ringIndex, jn));
            }
            var road = new WorldRoad { a = jn, b = at.target, asphalt = at.asphalt, width = at.asphalt ? 7f : 5f };
            Vector3 from = Nodes[jn].pos, to = at.end;
            Branch(road.path, from, to, at.target);
            AddRoad(road);
        }
        // Ring edges between consecutive junctions (closed loop).
        ringNodes.Sort((x, y) => x.ringIndex.CompareTo(y.ringIndex));
        for (int i = 0; i < ringNodes.Count; i++)
        {
            var a = ringNodes[i]; var b = ringNodes[(i + 1) % ringNodes.Count];
            var road = new WorldRoad { a = a.node, b = b.node, asphalt = true, width = 9f };
            int k = a.ringIndex;
            road.path.Add(Ring[k]);
            do { k = (k + 1) % Ring.Count; road.path.Add(Ring[k]); } while (k != b.ringIndex);
            AddRoad(road);
        }
    }

    private void AddRoad(WorldRoad r)
    {
        Roads.Add(r);
        Nodes[r.a].roads.Add(Roads.Count - 1);
        Nodes[r.b].roads.Add(Roads.Count - 1);
    }

    private static void AddLine(List<Vector3> path, Vector3 a, Vector3 b)
    {
        int n = Mathf.Max(1, Mathf.CeilToInt(Flat(b - a).magnitude / RoadStep));
        for (int i = path.Count == 0 ? 0 : 1; i <= n; i++) path.Add(Vector3.Lerp(Flat(a), Flat(b), i / (float)n));
    }

    // Access road: a gentle curve from the junction to the destination (smoothed through an offset midpoint).
    private void Branch(List<Vector3> path, Vector3 a, Vector3 b, int seed)
    {
        a = Flat(a); b = Flat(b);
        Vector3 d = b - a; float len = d.magnitude;
        if (len < RoadStep * 2f) { AddLine(path, a, b); return; }
        Vector3 side = Vector3.Cross(Vector3.up, d / len);
        float off = ((seed * 37 % 11) / 10f - 0.5f) * Mathf.Min(len * 0.18f, 60f);
        Vector3 m = (a + b) * 0.5f + side * off;
        if (InCore(m, 30f)) m = (a + b) * 0.5f;
        int steps = Mathf.Max(2, Mathf.CeilToInt(len / RoadStep));
        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            Vector3 q = Vector3.Lerp(Vector3.Lerp(a, m, t), Vector3.Lerp(m, b, t), t);   // quadratic Bezier
            path.Add(q);
        }
    }

    private Vector3 OutwardFromCore(Vector3 p)
    {
        float l = p.x - CoreMin.x, r = CoreMax.x - p.x, s = p.z - CoreMin.z, n = CoreMax.z - p.z;
        float m = Mathf.Min(Mathf.Min(l, r), Mathf.Min(s, n));
        if (m == l) return Vector3.left;
        if (m == r) return Vector3.right;
        if (m == s) return Vector3.back;
        return Vector3.forward;
    }

    // Where the farm road leaves the scene terrain: the edge point reachable from the market pad with the fewest farm
    // features in the way (then the shortest).
    private Vector3 FarmGate(Vector3 pad, IList<Obstacle> obstacles)
    {
        pad = Flat(pad);
        var candidates = new List<Vector3>();
        for (int i = 0; i <= 8; i++)
        {
            float t = i / 8f;
            candidates.Add(new Vector3(Mathf.Lerp(CoreMin.x, CoreMax.x, t), 0f, CoreMax.z));
            candidates.Add(new Vector3(Mathf.Lerp(CoreMin.x, CoreMax.x, t), 0f, CoreMin.z));
            candidates.Add(new Vector3(CoreMin.x, 0f, Mathf.Lerp(CoreMin.z, CoreMax.z, t)));
            candidates.Add(new Vector3(CoreMax.x, 0f, Mathf.Lerp(CoreMin.z, CoreMax.z, t)));
        }
        Vector3 best = candidates[0]; float bestScore = float.MaxValue;
        foreach (var c in candidates)
        {
            float len = (c - pad).magnitude;
            int hits = 0;
            if (obstacles != null)
                for (float s = 10f; s < len; s += 4f)
                {
                    Vector3 q = Vector3.Lerp(pad, c, s / len);
                    foreach (var o in obstacles) if (Flat(q - o.p).magnitude < o.r + 4f) { hits++; break; }
                }
            // corners make awkward exits; ring proximity shortens the drive
            float corner = Mathf.Min(Mathf.Min(Mathf.Abs(c.x - CoreMin.x), Mathf.Abs(c.x - CoreMax.x)), 1e9f) < 1f && Mathf.Min(Mathf.Abs(c.z - CoreMin.z), Mathf.Abs(c.z - CoreMax.z)) < 1f ? 200f : 0f;
            float score = len + hits * 400f + corner;
            if (score < bestScore) { bestScore = score; best = c; }
        }
        return best;
    }

    // ------------------------------------------------------------------ heights

    /// <summary>Road heights: the raw ground along each road, smoothed (no steep climbs over dunes), pinned at the nodes so
    /// roads meet at the same height. Pads take the average ground height of their area.</summary>
    private void Profiles(Func<Vector3, float> coreHeight, Func<float, float, float> raw)
    {
        foreach (var p in Pois)
        {
            if (p.kind == PoiKind.Farm) { p.pos.y = coreHeight(p.pos); continue; }
            float h = 0f; int n = 0;
            for (int i = 0; i < 9; i++)
            {
                Vector3 q = p.pos + Quaternion.Euler(0f, i * 40f, 0f) * Vector3.forward * (i == 0 ? 0f : p.radius * 0.7f);
                h += raw(q.x, q.z); n++;
            }
            p.pos.y = h / n;
            Nodes[p.node].h = p.pos.y;
            p.gate.y = p.pos.y; p.arrival.y = p.pos.y;
            if (p.parcel != null) p.parcel.center.y = p.pos.y;
        }
        // ring first (closed loop), then every road pinned to its end nodes
        var ringH = new float[Ring.Count];
        for (int i = 0; i < Ring.Count; i++) ringH[i] = raw(Ring[i].x, Ring[i].z);
        ringH = Smooth(ringH, 7, true);
        for (int i = 0; i < Ring.Count; i++) Ring[i] = new Vector3(Ring[i].x, ringH[i], Ring[i].z);
        foreach (var n in Nodes)
        {
            if (n.poi >= 0) continue;
            if (InCore(n.pos, 0.5f) || CoreDistance(n.pos) < 0.5f) n.h = coreHeight(n.pos);
            else
            {
                int best = NearestRing(n.pos);
                n.h = Flat(Ring[best] - n.pos).magnitude < 2f ? Ring[best].y : raw(n.pos.x, n.pos.z);
            }
            n.pos.y = n.h;
        }
        foreach (var r in Roads)
        {
            int c = r.path.Count;
            var h = new float[c];
            for (int i = 0; i < c; i++) h[i] = r.inCore ? coreHeight(r.path[i]) : raw(r.path[i].x, r.path[i].z);
            if (!r.inCore) h = Smooth(h, 6, false);
            // pin the ends and blend in over 60 m
            float ha = Nodes[r.a].h, hb = Nodes[r.b].h, len = 0f;
            var dist = new float[c];
            for (int i = 1; i < c; i++) { len += Flat(r.path[i] - r.path[i - 1]).magnitude; dist[i] = len; }
            for (int i = 0; i < c && !r.inCore; i++)
            {
                float wa = 1f - Mathf.Clamp01(dist[i] / 60f), wb = 1f - Mathf.Clamp01((len - dist[i]) / 60f);
                h[i] = Mathf.Lerp(h[i], ha, Smooth01(wa));
                h[i] = Mathf.Lerp(h[i], hb, Smooth01(wb));
            }
            r.profile = h;
            for (int i = 0; i < c; i++) r.path[i] = new Vector3(r.path[i].x, h[i], r.path[i].z);
        }
    }

    public static float Smooth01(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

    private static float[] Smooth(float[] h, int radius, bool loop)
    {
        int n = h.Length;
        var o = new float[n];
        for (int pass = 0; pass < 2; pass++)
        {
            for (int i = 0; i < n; i++)
            {
                float s = 0f; int k = 0;
                for (int j = -radius; j <= radius; j++)
                {
                    int q = i + j;
                    if (loop) q = (q % n + n) % n; else if (q < 0 || q >= n) continue;
                    s += h[q]; k++;
                }
                o[i] = s / k;
            }
            var t = h; h = o; o = t;
        }
        return h;
    }

    // ------------------------------------------------------------------ queries

    /// <summary>Nodes reachable from the farm (graph search over the roads).</summary>
    public HashSet<int> Reachable(int from = -1)
    {
        if (from < 0) from = Pois[FarmPoi].node;
        var seen = new HashSet<int> { from };
        var q = new Queue<int>(); q.Enqueue(from);
        while (q.Count > 0)
        {
            int n = q.Dequeue();
            foreach (int ri in Nodes[n].roads)
            {
                var r = Roads[ri];
                int o = r.a == n ? r.b : r.a;
                if (seen.Add(o)) q.Enqueue(o);
            }
        }
        return seen;
    }

    /// <summary>Road distance (metres) from node a to every node (Dijkstra over road lengths).</summary>
    public float RoadDistance(int a, int b)
    {
        var dist = new Dictionary<int, float> { { a, 0f } };
        var open = new List<int> { a };
        var done = new HashSet<int>();
        while (open.Count > 0)
        {
            int best = open[0];
            foreach (int o in open) if (dist[o] < dist[best]) best = o;
            open.Remove(best);
            if (best == b) return dist[best];
            if (!done.Add(best)) continue;
            foreach (int ri in Nodes[best].roads)
            {
                var r = Roads[ri];
                int o = r.a == best ? r.b : r.a;
                float d = dist[best] + r.Length;
                if (!dist.TryGetValue(o, out var cur) || d < cur) { dist[o] = d; if (!open.Contains(o)) open.Add(o); }
            }
        }
        return float.PositiveInfinity;
    }

    public WorldPoi Poi(string id) => Pois.Find(p => p.id == id);

    /// <summary>Destinations a signpost at this node points to (POIs at the far end of each road, followed through junctions).</summary>
    public List<(WorldPoi poi, Vector3 dir, float metres)> SignTargets(int node)
    {
        var list = new List<(WorldPoi, Vector3, float)>();
        var n = Nodes[node];
        foreach (int ri in n.roads)
        {
            var r = Roads[ri];
            int o = r.a == node ? r.b : r.a;
            Vector3 next = r.a == node ? r.path[Mathf.Min(3, r.path.Count - 1)] : r.path[Mathf.Max(0, r.path.Count - 4)];
            Vector3 dir = Flat(next - n.pos);
            if (dir.sqrMagnitude < 0.01f) continue;
            if (Nodes[o].poi >= 0) list.Add((Pois[Nodes[o].poi], dir.normalized, r.Length));
        }
        return list;
    }
}
