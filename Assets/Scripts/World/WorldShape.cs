using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Ground height of the open world (metres, relative to the scene terrain's base) and its surface layers.
/// Najd: rolling sand, seif dunes further out, a rocky escarpment (plateau) to the west, a dry wadi; Nile Delta: flat
/// alluvium crossed by canals, a patchwork of fields. Both rise into a soft edge (high dunes / a levee) at the map's
/// rim and blend into the scene terrain at its edges. Roads, pads and fields are flattened (spatial hash of features,
/// so a heightmap sample only looks at the few features near it). Pure maths: the same function makes the heightmaps
/// and places every object, so nothing floats or sinks.
/// </summary>
public class WorldShape
{
    public const float MinH = -30f, Range = 170f;   // heightmap span below / above the scene terrain's base
    public const float EdgeBand = 160f;

    public readonly string Region;
    public readonly Vector3 CoreMin, CoreMax, Center;
    public readonly float Half;
    private readonly Func<Vector3, float> coreHeight;
    public bool Delta => Region == "nile_delta";

    private readonly List<Vector3> wadi = new List<Vector3>();
    // canal and wadi segments by 50 m cell (distance queries only look at nearby segments; beyond LineReach = far)
    private const float LineCell = 50f, LineReach = 45f;
    private struct LineSeg { public Vector3 a, b; public int kind; }
    private readonly Dictionary<long, List<LineSeg>> lineGrid = new Dictionary<long, List<LineSeg>>();
    public readonly List<List<Vector3>> Canals = new List<List<Vector3>>();
    public Vector3 Pond { get; private set; }
    public float PondRadius { get; private set; }

    private struct Feature
    {
        public Vector3 a, b; public float ha, hb, flat, shoulder; public bool disc, road, asphalt;
    }
    private readonly List<Feature> features = new List<Feature>();
    private const float Cell = 40f;
    private readonly Dictionary<long, List<int>> grid = new Dictionary<long, List<int>>();

    public WorldShape(string region, Vector3 coreMin, Vector3 coreMax, Func<Vector3, float> coreHeight)
    {
        Region = region ?? Regions.DefaultId;
        CoreMin = coreMin; CoreMax = coreMax; CoreMin.y = CoreMax.y = 0f;
        Center = (CoreMin + CoreMax) * 0.5f;
        Half = WorldLayout.HalfFor(coreMin, coreMax);
        this.coreHeight = coreHeight ?? (p => 0f);
        if (Delta)
        {
            Canals.Add(Line(new Vector3(-1f, 0f, -0.48f), new Vector3(1f, 0f, -0.50f), 0.02f, 3));
            Canals.Add(Line(new Vector3(0.80f, 0f, -1f), new Vector3(0.78f, 0f, 1f), 0.015f, 5));
            Canals.Add(Line(new Vector3(-1f, 0f, 0.74f), new Vector3(1f, 0f, 0.72f), 0.02f, 7));
            Canals.Add(Line(new Vector3(-0.74f, 0f, -1f), new Vector3(-0.76f, 0f, 1f), 0.015f, 9));
        }
        else
        {
            wadi.AddRange(Line(new Vector3(-0.35f, 0f, -1.05f), new Vector3(1.05f, 0f, 0.30f), 0.06f, 2));
        }
        foreach (var c in Canals) AddLine(c, 0);
        AddLine(wadi, 1);
    }

    private void AddLine(List<Vector3> line, int kind)
    {
        for (int i = 0; i + 1 < line.Count; i++)
        {
            Vector3 a = line[i], b = line[i + 1];
            int x0 = Mathf.FloorToInt((Mathf.Min(a.x, b.x) - LineReach) / LineCell), x1 = Mathf.FloorToInt((Mathf.Max(a.x, b.x) + LineReach) / LineCell);
            int z0 = Mathf.FloorToInt((Mathf.Min(a.z, b.z) - LineReach) / LineCell), z1 = Mathf.FloorToInt((Mathf.Max(a.z, b.z) + LineReach) / LineCell);
            for (int x = x0; x <= x1; x++)
                for (int z = z0; z <= z1; z++)
                {
                    long k = Key(x, z);
                    if (!lineGrid.TryGetValue(k, out var l)) lineGrid[k] = l = new List<LineSeg>();
                    l.Add(new LineSeg { a = a, b = b, kind = kind });
                }
        }
    }

    /// <summary>Distance to the nearest canal (kind 0) or wadi (kind 1) segment; float.MaxValue when none is within ~45 m.</summary>
    private float LineDistance(float x, float z, int kind)
    {
        if (!lineGrid.TryGetValue(Key(Mathf.FloorToInt(x / LineCell), Mathf.FloorToInt(z / LineCell)), out var l)) return float.MaxValue;
        float best = float.MaxValue;
        var p = new Vector3(x, 0f, z);
        foreach (var s in l) if (s.kind == kind) best = Mathf.Min(best, Seg(p, s.a, s.b, out _));
        return best;
    }

    // A slightly wavy line across the map (fractions of the half-size), one point every ~25 m.
    private List<Vector3> Line(Vector3 fa, Vector3 fb, float wave, int seed)
    {
        var list = new List<Vector3>();
        Vector3 a = Center + fa * Half, b = Center + fb * Half;
        Vector3 d = b - a; float len = d.magnitude;
        Vector3 side = Vector3.Cross(Vector3.up, d / len);
        int n = Mathf.CeilToInt(len / 25f);
        for (int i = 0; i <= n; i++)
        {
            float t = i / (float)n;
            list.Add(a + d * t + side * (Mathf.Sin(t * 9f + seed) * wave * Half));
        }
        return list;
    }

    // ------------------------------------------------------------------ natural ground

    private static float N(float x, float z, float f, float ox, float oz) => Mathf.PerlinNoise(x * f + ox, z * f + oz);

    /// <summary>The land without roads or pads (no scene blend, no edge).</summary>
    public float Natural(float x, float z)
    {
        if (Delta)
        {
            float h = 0.7f * N(x, z, 1f / 260f, 3.1f, 1.7f) + 0.25f * N(x, z, 1f / 70f, 9.4f, 5.2f);
            h -= 1.7f * (1f - WorldLayout.Smooth01(LineDistance(x, z, 0) / 9f));
            return h;
        }
        float rolling = 5f * N(x, z, 1f / 320f, 11.3f, 7.9f) + 1.6f * N(x, z, 1f / 85f, 2.2f, 4.4f);
        float dx = x - Center.x, dz = z - Center.z;
        float d = Mathf.Sqrt(dx * dx + dz * dz);
        float duneMask = WorldLayout.Smooth01((N(x, z, 1f / 760f, 5.5f, 8.8f) - 0.42f) / 0.2f) * WorldLayout.Smooth01((d - 430f) / 260f);
        float ridge = 1f - Mathf.Abs(2f * N(x, z * 0.22f, 1f / 95f, 1.3f, 6.1f) - 1f);
        float dunes = duneMask * ridge * ridge * 12f;
        float plateau = Plateau(x, z);
        float wadiCut = -5f * (1f - WorldLayout.Smooth01(LineDistance(x, z, 1) / 26f));
        return rolling + dunes + plateau + wadiCut;
    }

    /// <summary>Najd: 0..1 how far onto the rocky plateau (escarpment to the west).</summary>
    public float PlateauMask(float x, float z)
    {
        if (Delta) return 0f;
        float u = x - (Center.x - 0.70f * Half) + 60f * (N(0f, z, 1f / 300f, 4.4f, 0.7f) - 0.5f);
        return WorldLayout.Smooth01(-u / 50f + 0.5f);
    }

    private float Plateau(float x, float z) => 26f * PlateauMask(x, z);

    public float WadiMask(float x, float z) => Delta || wadi.Count == 0 ? 0f : 1f - WorldLayout.Smooth01(LineDistance(x, z, 1) / 34f);

    public float CanalDistance(float x, float z) => Canals.Count == 0 ? float.MaxValue : LineDistance(x, z, 0);

    public static float Seg(Vector3 p, Vector3 a, Vector3 b, out float t)
    {
        float abx = b.x - a.x, abz = b.z - a.z;
        float len2 = abx * abx + abz * abz;
        t = len2 < 1e-6f ? 0f : Mathf.Clamp01(((p.x - a.x) * abx + (p.z - a.z) * abz) / len2);
        float qx = a.x + abx * t - p.x, qz = a.z + abz * t - p.z;
        return Mathf.Sqrt(qx * qx + qz * qz);
    }

    public float CoreDistance(float x, float z)
    {
        float dx = Mathf.Max(Mathf.Max(CoreMin.x - x, 0f), x - CoreMax.x), dz = Mathf.Max(Mathf.Max(CoreMin.z - z, 0f), z - CoreMax.z);
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    public float EdgeDistance(float x, float z) => Mathf.Min(Mathf.Min(x - (Center.x - Half), Center.x + Half - x), Mathf.Min(z - (Center.z - Half), Center.z + Half - z));

    /// <summary>Ground before roads and pads: natural land, blended into the scene terrain near it, rising at the map's rim.</summary>
    public float Raw(float x, float z)
    {
        float h = Natural(x, z);
        float dc = CoreDistance(x, z);
        if (dc < 200f)
        {
            var edge = new Vector3(Mathf.Clamp(x, CoreMin.x, CoreMax.x), 0f, Mathf.Clamp(z, CoreMin.z, CoreMax.z));
            h = Mathf.Lerp(coreHeight(edge), h, WorldLayout.Smooth01(dc / 200f));
        }
        float t = 1f - Mathf.Clamp01(EdgeDistance(x, z) / EdgeBand);
        if (t > 0f)
        {
            float rise = Delta ? 7f : 34f + 10f * N(x, z, 1f / 60f, 7.7f, 3.3f);
            h += rise * WorldLayout.Smooth01(t);
        }
        return h;
    }

    // ------------------------------------------------------------------ flattened features

    /// <summary>Flatten roads (smoothed profiles) and pads (discs) of a layout into the ground.</summary>
    public void AddFeatures(WorldLayout w)
    {
        features.Clear(); grid.Clear();
        foreach (var r in w.Roads)
        {
            if (r.inCore || r.path.Count < 2) continue;
            for (int i = 0; i + 1 < r.path.Count; i++)
                Add(new Feature { a = r.path[i], b = r.path[i + 1], ha = r.profile[i], hb = r.profile[i + 1], flat = r.width * 0.5f + 1.6f, shoulder = r.asphalt ? 14f : 10f, road = true, asphalt = r.asphalt });
        }
        foreach (var p in w.Pois)
        {
            if (p.kind == PoiKind.Farm) continue;
            Add(new Feature { a = p.pos, b = p.pos, ha = p.pos.y, hb = p.pos.y, flat = p.radius, shoulder = 26f, disc = true });
        }
        // Najd oasis: a pond beside the town (carved a little lower, palms around it)
        var town = w.Poi("town");
        if (!Delta && town != null)
        {
            Vector3 away = Quaternion.Euler(0f, town.yaw, 0f) * Vector3.back;
            Pond = town.pos + away * (town.radius + 34f);
            Pond = new Vector3(Pond.x, town.pos.y - 1.4f, Pond.z);
            PondRadius = 17f;
            Add(new Feature { a = Pond, b = Pond, ha = Pond.y, hb = Pond.y, flat = PondRadius, shoulder = 12f, disc = true });
        }
        else PondRadius = 0f;
    }

    private void Add(Feature f)
    {
        features.Add(f);
        int id = features.Count - 1;
        float pad = f.flat + f.shoulder;
        int x0 = Mathf.FloorToInt((Mathf.Min(f.a.x, f.b.x) - pad) / Cell), x1 = Mathf.FloorToInt((Mathf.Max(f.a.x, f.b.x) + pad) / Cell);
        int z0 = Mathf.FloorToInt((Mathf.Min(f.a.z, f.b.z) - pad) / Cell), z1 = Mathf.FloorToInt((Mathf.Max(f.a.z, f.b.z) + pad) / Cell);
        for (int i = x0; i <= x1; i++)
            for (int j = z0; j <= z1; j++)
            {
                long k = Key(i, j);
                if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<int>();
                l.Add(id);
            }
    }

    private static long Key(int i, int j) => ((long)i << 32) ^ (uint)j;

    /// <summary>Final ground height (roads and pads flattened).</summary>
    public float Final(float x, float z)
    {
        float h = Raw(x, z);
        if (!grid.TryGetValue(Key(Mathf.FloorToInt(x / Cell), Mathf.FloorToInt(z / Cell)), out var list)) return h;
        float bestW = 0f, target = h;
        var p = new Vector3(x, 0f, z);
        foreach (int id in list)
        {
            var f = features[id];
            float t = 0f, d;
            if (f.disc) d = Mathf.Sqrt((x - f.a.x) * (x - f.a.x) + (z - f.a.z) * (z - f.a.z));
            else d = Seg(p, f.a, f.b, out t);
            float w = d <= f.flat ? 1f : 1f - WorldLayout.Smooth01((d - f.flat) / f.shoulder);
            if (w > bestW) { bestW = w; target = Mathf.Lerp(f.ha, f.hb, t); }
        }
        return Mathf.Lerp(h, target, bestW);
    }

    /// <summary>Distance to the nearest road outside the scene terrain (asphalt = that road is asphalt). MaxValue if none near.</summary>
    public float RoadDistance(Vector3 p, out bool asphalt, out float halfWidth)
    {
        asphalt = false; halfWidth = 0f;
        float best = float.MaxValue;
        if (!grid.TryGetValue(Key(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.z / Cell)), out var list)) return best;
        foreach (int id in list)
        {
            var f = features[id];
            if (!f.road) continue;
            float d = Seg(p, f.a, f.b, out _);
            if (d < best) { best = d; asphalt = f.asphalt; halfWidth = f.flat - 1.6f; }
        }
        return best;
    }

    // ------------------------------------------------------------------ surface layers

    /// <summary>Surface layer colours: Najd sand, rock, gravel; Delta young crops (berseem), soil, ripening fields.</summary>
    public Color[] LayerColors(Color ground)
    {
        if (Delta) return new[] { ground, new Color(0.33f, 0.25f, 0.17f), new Color(0.58f, 0.6f, 0.3f) };
        return new[] { ground, new Color(0.55f, 0.43f, 0.33f), new Color(0.66f, 0.56f, 0.44f) };
    }

    /// <summary>Layer weights at a point (sum 1). slope = rise per metre of the ground there.</summary>
    public void Weights(float x, float z, float slope, float[] w)
    {
        if (Delta)
        {
            // a patchwork of fields (cells of ~70 x 45 m, rotated a little), bare soil on the canal banks and road sides
            float c = Mathf.Cos(0.14f), s = Mathf.Sin(0.14f);
            float u = (x * c - z * s) / 70f, v = (x * s + z * c) / 45f;
            int iu = Mathf.FloorToInt(u), iv = Mathf.FloorToInt(v);
            int hsh = (iu * 73856093) ^ (iv * 19349663);
            int k = ((hsh % 7) + 7) % 7;
            float fu = u - iu, fv = v - iv;
            bool bund = fu < 0.04f || fv < 0.06f;      // earth bunds between fields
            w[0] = k <= 2 ? 1f : 0f; w[1] = k == 3 || k == 4 ? 1f : 0f; w[2] = k >= 5 ? 1f : 0f;
            if (bund) { w[0] = 0.2f; w[1] = 0.8f; w[2] = 0f; }
            float edge = 1f - Mathf.Clamp01(EdgeDistance(x, z) / EdgeBand);
            if (edge > 0.2f) { w[0] = 1f; w[1] = w[2] = 0f; }
            if (CanalDistance(x, z) < 9f) { w[0] = 0.4f; w[1] = 0.6f; w[2] = 0f; }
            Normalize(w);
            return;
        }
        float rock = Mathf.Clamp01((slope - 0.32f) / 0.3f) + PlateauMask(x, z) * 0.55f;
        float gravel = WadiMask(x, z) * 0.9f + 0.25f * N(x, z, 1f / 40f, 1.9f, 8.3f);
        w[1] = Mathf.Clamp01(rock); w[2] = Mathf.Clamp01(gravel) * (1f - w[1]); w[0] = Mathf.Max(0f, 1f - w[1] - w[2]);
        Normalize(w);
    }

    private static void Normalize(float[] w)
    {
        float s = 0f; foreach (var v in w) s += v;
        if (s <= 1e-5f) { w[0] = 1f; for (int i = 1; i < w.Length; i++) w[i] = 0f; return; }
        for (int i = 0; i < w.Length; i++) w[i] /= s;
    }
}
