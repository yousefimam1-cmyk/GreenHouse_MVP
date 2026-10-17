using System.Collections.Generic;
using UnityEngine;

/// <summary>Small mesh builder: vertices with normal / uv / colour and one triangle list per sub-mesh.</summary>
public class MeshKit
{
    public readonly List<Vector3> V = new List<Vector3>();
    public readonly List<Vector3> N = new List<Vector3>();
    public readonly List<Vector2> UV = new List<Vector2>();
    public readonly List<Color> C = new List<Color>();
    public readonly List<int>[] T;

    public MeshKit(int submeshes)
    {
        T = new List<int>[Mathf.Max(1, submeshes)];
        for (int i = 0; i < T.Length; i++) T[i] = new List<int>();
    }

    public int Vert(Vector3 p, Vector3 n, Vector2 uv, Color c) { V.Add(p); N.Add(n); UV.Add(uv); C.Add(c); return V.Count - 1; }
    public void Tri(int sub, int a, int b, int c) { T[sub].Add(a); T[sub].Add(b); T[sub].Add(c); }
    public int Triangles { get { int n = 0; foreach (var t in T) n += t.Count / 3; return n; } }

    public Mesh ToMesh(string name, bool tangents)
    {
        var m = new Mesh();
        m.name = name;
        if (V.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        m.SetVertices(V);
        m.SetNormals(N);
        m.SetUVs(0, UV);
        m.SetColors(C);
        m.subMeshCount = T.Length;
        for (int i = 0; i < T.Length; i++) m.SetTriangles(T[i], i);
        m.RecalculateBounds();
        if (tangents && V.Count > 0) m.RecalculateTangents();
        return m;
    }

    // ------------------------------------------------------------------ primitives
    /// <summary>A tube along a path (bark): rings of sides + 1 vertices (seam duplicated), outward normals, u around, v along (metres / vTile).</summary>
    public void Tube(int sub, IList<Vector3> path, IList<float> radii, int sides, float vTile, float uRepeat, Color col)
    {
        if (path.Count < 2) return;
        Vector3 dir = path[path.Count - 1] - path[0];
        Vector3 reference = Mathf.Abs(Vector3.Dot(dir.normalized, Vector3.forward)) > 0.9f ? Vector3.right : Vector3.forward;
        float along = 0f;
        int start = V.Count;
        for (int i = 0; i < path.Count; i++)
        {
            Vector3 t = (path[Mathf.Min(i + 1, path.Count - 1)] - path[Mathf.Max(i - 1, 0)]).normalized;
            if (t.sqrMagnitude < 1e-6f) t = Vector3.up;
            Vector3 side = Vector3.Cross(t, reference).normalized;
            if (side.sqrMagnitude < 1e-6f) side = Vector3.right;
            Vector3 bin = Vector3.Cross(side, t).normalized;
            if (i > 0) along += (path[i] - path[i - 1]).magnitude;
            for (int j = 0; j <= sides; j++)
            {
                float a = j / (float)sides * Mathf.PI * 2f;
                Vector3 n = side * Mathf.Cos(a) + bin * Mathf.Sin(a);
                Vert(path[i] + n * radii[i], n, new Vector2(j / (float)sides * uRepeat, along / Mathf.Max(0.01f, vTile)), col);
            }
        }
        int ring = sides + 1;
        for (int i = 0; i + 1 < path.Count; i++)
            for (int j = 0; j < sides; j++)
            {
                int a = start + i * ring + j, b = a + ring, c = a + 1, d = b + 1;
                Tri(sub, a, b, c);
                Tri(sub, c, b, d);
            }
    }

    /// <summary>
    /// A leaf / twig card hanging from attach: the atlas cell's top edge (where the twig enters) sits at attach and the card
    /// runs along down for length. Colours: base -> tip (wind weights). Double sided through the material (Cull Off).
    /// </summary>
    public void Card(int sub, Vector3 attach, Vector3 down, Vector3 across, float length, float width, Vector4 cell, Vector3 normal, Color baseCol, Color tipCol)
    {
        down = down.normalized; across = across.normalized;
        Vector3 h = across * (width * 0.5f);
        int a = Vert(attach - h, normal, new Vector2(cell.x, cell.w), baseCol);
        int b = Vert(attach + h, normal, new Vector2(cell.z, cell.w), baseCol);
        int c = Vert(attach + h + down * length, normal, new Vector2(cell.z, cell.y), tipCol);
        int d = Vert(attach - h + down * length, normal, new Vector2(cell.x, cell.y), tipCol);
        Tri(sub, a, b, c);
        Tri(sub, a, c, d);
    }

    /// <summary>A tuft standing on the ground: the atlas cell's bottom edge at base, up for height.</summary>
    public void Blade(int sub, Vector3 basePos, Vector3 up, Vector3 across, float height, float width, Vector4 cell, Vector3 normal, Color baseCol, Color tipCol)
    {
        Vector3 h = across.normalized * (width * 0.5f);
        int a = Vert(basePos - h, normal, new Vector2(cell.x, cell.y), baseCol);
        int b = Vert(basePos + h, normal, new Vector2(cell.z, cell.y), baseCol);
        int c = Vert(basePos + h + up.normalized * height, normal, new Vector2(cell.z, cell.w), tipCol);
        int d = Vert(basePos - h + up.normalized * height, normal, new Vector2(cell.x, cell.w), tipCol);
        Tri(sub, a, b, c);
        Tri(sub, a, c, d);
    }
}

/// <summary>Kinds of vegetation the look can build (Vegetation.PlaceTree / Scatter).</summary>
public enum TreeType { DatePalm, Acacia, Ghaf, Sidr, Eucalyptus, Shrub, DryShrub }

/// <summary>
/// Procedural, game-ready vegetation meshes with three levels of detail each (LOD0 full, LOD1 reduced, LOD2 a few
/// big cards = distant billboard look). Bark is sub-mesh 0 (URP/Lit, real bark textures), leaves sub-mesh 1
/// (Mazraa/Foliage, alpha-cut CC0 leaf atlases, wind weights in vertex colours). A handful of seeded variants per
/// species is built once and shared by every instance (GPU instancing / SRP Batcher, low RAM).
/// </summary>
public static class FoliageMeshes
{
    public const int Variants = 4;
    public const int Lods = 3;

    // Atlas cells (u0, v0, u1, v1), from the texture build (see CREDITS.md).
    public static readonly Vector4[] ClusterCells =
    {
        new Vector4(0f, 0.5f, 0.5f, 1f), new Vector4(0.5f, 0.5f, 1f, 1f), new Vector4(0f, 0f, 0.5f, 0.5f), new Vector4(0.5f, 0f, 1f, 0.5f),
    };
    public static readonly Vector4[] GrassCells =
    {
        new Vector4(0.00391f, 0.50391f, 0.49609f, 0.94336f), new Vector4(0.50391f, 0.50391f, 0.99609f, 0.92188f),
        new Vector4(0.00391f, 0.00391f, 0.49609f, 0.39648f), new Vector4(0.57031f, 0.00391f, 0.92871f, 0.49219f),
    };
    public static readonly Vector4 FrondGreen = new Vector4(0f, 0f, 0.5f, 1f), FrondDry = new Vector4(0.5f, 0f, 1f, 1f);

    private static readonly Dictionary<string, Mesh[]> cache = new Dictionary<string, Mesh[]>();
    private static readonly Dictionary<string, int[]> tris = new Dictionary<string, int[]>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { cache.Clear(); tris.Clear(); }

    /// <summary>Meshes for LOD0..2 of one variant (built once, shared).</summary>
    public static Mesh[] Get(TreeType type, int variant)
    {
        variant = ((variant % Variants) + Variants) % Variants;
        string key = type + "/" + variant;
        Mesh[] lods;
        if (cache.TryGetValue(key, out lods) && lods[0] != null) return lods;
        lods = new Mesh[Lods];
        var counts = new int[Lods];
        for (int lod = 0; lod < Lods; lod++)
        {
            var kit = new MeshKit(2);
            var rnd = new System.Random(1000 + (int)type * 97 + variant * 13);   // same seed per LOD: same overall shape
            switch (type)
            {
                case TreeType.DatePalm: DatePalm(kit, rnd, lod); break;
                case TreeType.Acacia: Acacia(kit, rnd, lod, false); break;
                case TreeType.Ghaf: Acacia(kit, rnd, lod, true); break;
                case TreeType.Sidr: Sidr(kit, rnd, lod); break;
                case TreeType.Eucalyptus: Eucalyptus(kit, rnd, lod); break;
                case TreeType.Shrub: Shrub(kit, rnd, lod); break;
                case TreeType.DryShrub: DryShrub(kit, rnd, lod); break;
            }
            counts[lod] = kit.Triangles;
            lods[lod] = kit.ToMesh(key + " LOD" + lod, true);
        }
        cache[key] = lods;
        tris[key] = counts;
        return lods;
    }

    /// <summary>Triangle counts per LOD of a built variant (tests: each LOD is lighter than the one before).</summary>
    public static int[] TriangleCounts(TreeType type, int variant) { Get(type, variant); return tris[type + "/" + (((variant % Variants) + Variants) % Variants)]; }

    /// <summary>Whether a type has bark (sub-mesh 0) at all.</summary>
    public static bool HasBark(TreeType t) => t != TreeType.Shrub && t != TreeType.DryShrub;

    private static float R(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);
    private static Color W(float bend, float flutter, float phase, float ao) => new Color(bend, flutter, phase, ao);
    private static readonly Color Still = new Color(0f, 0f, 0f, 1f);

    // ------------------------------------------------------------------ date palm (Phoenix dactylifera)
    private static void DatePalm(MeshKit k, System.Random r, int lod)
    {
        float height = R(r, 6.5f, 9f);
        float lean = R(r, -0.35f, 0.35f), lean2 = R(r, -0.25f, 0.25f);
        int rings = lod == 0 ? 14 : lod == 1 ? 7 : 3;
        int sides = lod == 0 ? 12 : lod == 1 ? 7 : 4;
        var path = new List<Vector3>(); var radii = new List<float>();
        for (int i = 0; i <= rings; i++)
        {
            float t = i / (float)rings;
            float y = height * t;
            path.Add(new Vector3(lean * t * t * 2.2f, y, lean2 * t * t * 1.6f));
            float rad = Mathf.Lerp(0.27f, 0.23f, t);
            if (t < 0.08f) rad += (0.08f - t) / 0.08f * 0.16f;     // root flare
            if (t > 0.88f) rad += (t - 0.88f) / 0.12f * 0.14f;     // leaf-base "boot" under the crown
            radii.Add(rad);
        }
        k.Tube(0, path, radii, sides, 1.86f, 2f, Still);
        Vector3 top = path[path.Count - 1] + Vector3.up * 0.1f;

        int green = lod == 0 ? 26 : lod == 1 ? 14 : 7;
        int dry = lod == 0 ? 7 : lod == 1 ? 3 : 0;
        int segs = lod == 0 ? 7 : lod == 1 ? 4 : 2;
        bool fold = lod < 2;
        for (int f = 0; f < green + dry; f++)
        {
            bool isDry = f >= green;
            float yaw = isDry ? R(r, 0f, 360f) : f * 137.5f + R(r, -8f, 8f);
            float tier = isDry ? 0f : (f % 3) / 2f;                       // three whorls: young upright, middle, old drooping
            float pitch = isDry ? R(r, -75f, -55f) : Mathf.Lerp(62f, 8f, tier) + R(r, -8f, 8f);
            float len = isDry ? R(r, 2.2f, 3f) : R(r, 3f, 4.3f) * (1f - 0.15f * (1f - tier));
            float droop = isDry ? 0.05f : Mathf.Lerp(0.12f, 0.42f, tier);
            Frond(k, r, top + Vector3.down * (0.25f * tier + (isDry ? 0.45f : 0f)), yaw, pitch, len, droop, isDry ? FrondDry : FrondGreen, segs, fold, (float)r.NextDouble());
        }
    }

    private static void Frond(MeshKit k, System.Random r, Vector3 basePos, float yaw, float pitch, float length, float droop, Vector4 cell, int segs, bool fold, float phase)
    {
        Vector3 h = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        Vector3 dir0 = Quaternion.AngleAxis(-pitch, Vector3.Cross(Vector3.up, h)) * h;   // tilt up by pitch
        Vector3 side = Vector3.Cross(Vector3.up, h).normalized;
        float width = 1.35f * Mathf.Clamp(length / 3.6f, 0.75f, 1.1f);
        int prevL = -1, prevC = -1, prevR = -1;
        for (int s = 0; s <= segs; s++)
        {
            float t = s / (float)segs;
            Vector3 p = basePos + dir0 * (length * t) + Vector3.down * (length * droop * t * t);
            Vector3 tan = (dir0 * length + Vector3.down * (length * droop * 2f * t)).normalized;
            Vector3 up = Vector3.Cross(side, tan).normalized;      // strip normal (points up for a rising frond)
            if (up.y < 0f) up = -up;
            Vector3 lightN = (up * 0.55f + Vector3.up * 0.35f + h * 0.3f).normalized;
            float ao = Mathf.Lerp(0.72f, 1f, t);
            Color c = W(Mathf.Pow(t, 1.4f), t, phase, ao);
            float u0 = cell.x, u1 = cell.z, uc = (cell.x + cell.z) * 0.5f;
            float v = Mathf.Lerp(cell.y, cell.w, t);
            int l = k.Vert(p - side * (width * 0.5f), lightN, new Vector2(u0, v), c);
            int rr = k.Vert(p + side * (width * 0.5f), lightN, new Vector2(u1, v), c);
            int cc = fold ? k.Vert(p - up * (width * 0.14f), lightN, new Vector2(uc, v), c) : -1;
            if (s > 0)
            {
                if (fold)
                {
                    k.Tri(1, prevL, l, prevC); k.Tri(1, prevC, l, cc);
                    k.Tri(1, prevC, cc, prevR); k.Tri(1, prevR, cc, rr);
                }
                else { k.Tri(1, prevL, l, prevR); k.Tri(1, prevR, l, rr); }
            }
            prevL = l; prevR = rr; prevC = cc;
        }
    }

    // ------------------------------------------------------------------ acacia (umbrella thorn) and ghaf (Prosopis)
    private static void Acacia(MeshKit k, System.Random r, int lod, bool ghaf)
    {
        float fork = ghaf ? R(r, 1.8f, 2.6f) : R(r, 1.1f, 1.7f);
        float crownY = ghaf ? R(r, 4.2f, 5.2f) : R(r, 3.6f, 4.6f);
        float crownR = ghaf ? R(r, 2.8f, 3.5f) : R(r, 3.4f, 4.4f);
        int sides = lod == 0 ? 8 : lod == 1 ? 5 : 4;
        Vector3 lean = new Vector3(R(r, -0.4f, 0.4f), 0f, R(r, -0.4f, 0.4f));
        var trunk = new List<Vector3> { Vector3.zero, new Vector3(lean.x * 0.3f, fork * 0.5f, lean.z * 0.3f), new Vector3(lean.x, fork, lean.z) };
        k.Tube(0, trunk, new List<float> { 0.24f, 0.2f, 0.17f }, sides, 1f, 1f, Still);
        Vector3 forkP = trunk[2];
        int branches = ghaf ? 4 : (lod == 2 ? 3 : 4 + r.Next(2));
        var tips = new List<Vector3>();
        for (int b = 0; b < branches; b++)
        {
            float yaw = b * 360f / branches + R(r, -20f, 20f);
            Vector3 hdir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            Vector3 end = new Vector3(0f, crownY - 0.3f, 0f) + hdir * crownR * R(r, 0.45f, 0.65f);
            Vector3 mid = Vector3.Lerp(forkP, end, 0.5f) + hdir * 0.35f + Vector3.down * 0.25f;
            k.Tube(0, new List<Vector3> { forkP, mid, end }, new List<float> { 0.12f, 0.085f, 0.05f }, Mathf.Max(4, sides - 2), 1f, 1f, Still);
            tips.Add(end);
            if (lod == 0)
                for (int s = 0; s < 2; s++)
                {
                    Vector3 sd = Quaternion.Euler(0f, R(r, -50f, 50f), 0f) * hdir;
                    Vector3 e2 = end + sd * crownR * R(r, 0.25f, 0.4f) + Vector3.up * R(r, 0.05f, 0.35f);
                    k.Tube(0, new List<Vector3> { end, Vector3.Lerp(end, e2, 0.5f) + Vector3.up * 0.1f, e2 }, new List<float> { 0.05f, 0.035f, 0.02f }, 4, 1f, 1f, Still);
                    tips.Add(e2);
                }
        }
        // leaf clusters: an umbrella (acacia) or a drooping dome (ghaf)
        int cards = lod == 0 ? (ghaf ? 120 : 110) : lod == 1 ? 46 : 9;
        bool cross = lod == 0;
        float cardLen = lod == 2 ? 3.2f : lod == 1 ? 2.3f : 1.8f;
        Vector3 centre = new Vector3(0f, crownY, 0f);
        for (int i = 0; i < cards; i++)
        {
            float a = i * 137.508f * Mathf.Deg2Rad;
            float rr = crownR * Mathf.Sqrt((i + 0.5f) / cards) * R(r, 0.85f, 1.05f);
            Vector3 radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            float dome = ghaf ? 1.1f * (1f - (rr / crownR) * (rr / crownR)) : 0.45f * (1f - (rr / crownR) * (rr / crownR));
            Vector3 p = centre + radial * rr + Vector3.up * (dome + R(r, -0.25f, 0.25f));
            Vector3 down = ghaf ? (Vector3.down * 0.85f + radial * 0.5f) : (radial * 0.95f + Vector3.down * R(r, 0.1f, 0.45f));
            Vector3 across = Vector3.Cross(Vector3.up, radial);
            Vector3 n = ((p - centre + Vector3.down * (ghaf ? 1.5f : 2.5f)).normalized + Vector3.up * 0.4f).normalized;
            float edge = Mathf.Clamp01(rr / crownR);
            Color baseC = W(0.25f + 0.5f * edge, 0.4f, (float)r.NextDouble(), 0.7f + 0.3f * edge);
            Color tipC = W(0.4f + 0.6f * edge, 1f, baseC.b, 0.75f + 0.25f * edge);
            Vector4 cell = ClusterCells[r.Next(ClusterCells.Length)];
            float len = cardLen * R(r, 0.85f, 1.15f);
            k.Card(1, p, down, across, len, len, cell, n, baseC, tipC);
            if (cross) k.Card(1, p, down, Vector3.Cross(down, across), len * 0.9f, len * 0.9f, ClusterCells[r.Next(ClusterCells.Length)], n, baseC, tipC);
        }
    }

    // ------------------------------------------------------------------ sidr (Ziziphus spina-christi)
    private static void Sidr(MeshKit k, System.Random r, int lod)
    {
        int stems = lod == 2 ? 1 : 2 + r.Next(2);
        int sides = lod == 0 ? 7 : lod == 1 ? 5 : 4;
        float crownY = R(r, 2.9f, 3.6f);
        Vector3 rad = new Vector3(R(r, 2.4f, 3.1f), R(r, 1.7f, 2.2f), R(r, 2.4f, 3.1f));
        for (int s = 0; s < stems; s++)
        {
            Vector3 d = Quaternion.Euler(0f, s * 360f / stems + R(r, -30f, 30f), 0f) * Vector3.forward;
            var path = new List<Vector3> { d * 0.1f, d * 0.45f + Vector3.up * 1.2f, d * 1.1f + Vector3.up * (crownY - 0.6f) };
            k.Tube(0, path, new List<float> { 0.17f, 0.12f, 0.06f }, sides, 1f, 1f, Still);
        }
        int cards = lod == 0 ? 120 : lod == 1 ? 50 : 10;
        float len = lod == 2 ? 2.8f : lod == 1 ? 2f : 1.6f;
        Vector3 c = new Vector3(0f, crownY, 0f);
        for (int i = 0; i < cards; i++)
        {
            // points on (and a little inside) the ellipsoid: golden spiral
            float y = 1f - (i + 0.5f) / cards * 2f;
            float ring = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
            float a = i * 137.508f * Mathf.Deg2Rad;
            Vector3 unit = new Vector3(Mathf.Cos(a) * ring, y, Mathf.Sin(a) * ring);
            float depth = R(r, 0.7f, 1f);
            Vector3 p = c + Vector3.Scale(unit, rad) * depth;
            if (p.y < 1.2f) p.y = 1.2f + R(r, 0f, 0.3f);
            Vector3 outward = new Vector3(unit.x, 0f, unit.z).normalized;
            if (outward.sqrMagnitude < 0.01f) outward = Vector3.forward;
            Vector3 down = (outward * 0.7f + Vector3.down * 0.7f).normalized;
            Vector3 across = Vector3.Cross(Vector3.up, outward);
            Vector3 n = (unit + Vector3.up * 0.3f).normalized;
            Color baseC = W(0.35f, 0.4f, (float)r.NextDouble(), Mathf.Lerp(0.6f, 1f, (y + 1f) * 0.5f));
            Color tipC = W(0.7f, 1f, baseC.b, baseC.a);
            float l = len * R(r, 0.8f, 1.15f);
            k.Card(1, p, down, across, l, l, ClusterCells[r.Next(4)], n, baseC, tipC);
            if (lod == 0) k.Card(1, p, down, Vector3.Cross(down, across), l * 0.85f, l * 0.85f, ClusterCells[r.Next(4)], n, baseC, tipC);
        }
    }

    // ------------------------------------------------------------------ eucalyptus (kafour)
    private static void Eucalyptus(MeshKit k, System.Random r, int lod)
    {
        float height = R(r, 13f, 17f);
        int rings = lod == 0 ? 10 : lod == 1 ? 5 : 3;
        int sides = lod == 0 ? 10 : lod == 1 ? 6 : 4;
        float bend = R(r, -0.6f, 0.6f), bend2 = R(r, -0.6f, 0.6f);
        var path = new List<Vector3>(); var radii = new List<float>();
        for (int i = 0; i <= rings; i++)
        {
            float t = i / (float)rings;
            path.Add(new Vector3(Mathf.Sin(t * 2.2f) * bend, height * t, Mathf.Sin(t * 1.7f + 0.4f) * bend2));
            radii.Add(Mathf.Lerp(0.36f, 0.09f, t) + (t < 0.06f ? (0.06f - t) * 2.5f : 0f));
        }
        k.Tube(0, path, radii, sides, 1.3f, 1f, Still);
        int branches = lod == 2 ? 2 : 5;
        var anchors = new List<Vector3>();
        for (int b = 0; b < branches; b++)
        {
            float t = Mathf.Lerp(0.5f, 0.85f, b / (float)Mathf.Max(1, branches - 1));
            Vector3 start = Vector3.Lerp(path[0], path[path.Count - 1], t);
            Vector3 d = Quaternion.Euler(0f, b * 137.5f + R(r, -20f, 20f), 0f) * Vector3.forward;
            Vector3 end = start + d * R(r, 1.8f, 2.8f) + Vector3.up * R(r, 1.8f, 3f);
            k.Tube(0, new List<Vector3> { start, Vector3.Lerp(start, end, 0.5f) + Vector3.up * 0.2f, end }, new List<float> { 0.09f, 0.06f, 0.03f }, Mathf.Max(4, sides - 4), 1.3f, 1f, Still);
            anchors.Add(Vector3.Lerp(start, end, 0.6f)); anchors.Add(end);
        }
        anchors.Add(path[path.Count - 1]);
        int cards = lod == 0 ? 110 : lod == 1 ? 46 : 10;
        float len = lod == 2 ? 3.4f : lod == 1 ? 2.4f : 1.9f;
        for (int i = 0; i < cards; i++)
        {
            Vector3 a = anchors[r.Next(anchors.Count)];
            Vector3 off = new Vector3(R(r, -1.6f, 1.6f), R(r, -1.4f, 1.8f), R(r, -1.6f, 1.6f));
            Vector3 p = a + off;
            Vector3 outward = new Vector3(off.x, 0f, off.z).normalized;
            if (outward.sqrMagnitude < 0.01f) outward = Vector3.right;
            Vector3 down = (Vector3.down + outward * 0.3f).normalized;      // eucalyptus leaves hang
            Vector3 across = Vector3.Cross(Vector3.up, outward);
            Vector3 n = (outward + Vector3.up * 0.5f).normalized;
            float h01 = Mathf.Clamp01(p.y / height);
            Color baseC = W(0.35f + 0.3f * h01, 0.6f, (float)r.NextDouble(), Mathf.Lerp(0.65f, 1f, h01));
            Color tipC = W(0.8f, 1f, baseC.b, baseC.a);
            float l = len * R(r, 0.8f, 1.2f);
            k.Card(1, p, down, across, l, l, ClusterCells[r.Next(4)], n, baseC, tipC);
            if (lod == 0) k.Card(1, p, down, Vector3.Cross(down, across), l * 0.9f, l * 0.9f, ClusterCells[r.Next(4)], n, baseC, tipC);
        }
    }

    // ------------------------------------------------------------------ shrubs
    private static void Shrub(MeshKit k, System.Random r, int lod)
    {
        int cards = lod == 0 ? 22 : lod == 1 ? 10 : 4;
        float size = R(r, 0.8f, 1.2f);
        for (int i = 0; i < cards; i++)
        {
            float a = i * 137.508f * Mathf.Deg2Rad;
            Vector3 outward = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Vector3 p = outward * R(r, 0f, 0.35f) * size + Vector3.up * size * R(r, 0.55f, 0.95f);
            Vector3 down = (outward * 0.8f + Vector3.down * 0.6f).normalized;
            Vector3 n = (outward + Vector3.up).normalized;
            Color baseC = W(0.1f, 0.5f, (float)r.NextDouble(), 0.75f), tipC = W(0.45f, 1f, baseC.b, 1f);
            k.Card(1, p, down, Vector3.Cross(Vector3.up, outward), size, size, ClusterCells[r.Next(4)], n, baseC, tipC);
        }
    }

    private static void DryShrub(MeshKit k, System.Random r, int lod)
    {
        int quads = lod == 0 ? 5 : lod == 1 ? 3 : 2;
        float h = R(r, 0.7f, 1.1f), w = h * 1.6f;
        for (int i = 0; i < quads; i++)
        {
            float a = (i * 180f / quads + R(r, -10f, 10f)) * Mathf.Deg2Rad;
            Vector3 across = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Vector3 n = Vector3.Cross(across, Vector3.up).normalized * 0.4f + Vector3.up * 0.9f;
            k.Blade(1, Vector3.zero, Vector3.up, across, h, w, GrassCells[r.Next(GrassCells.Length)], n.normalized, W(0f, 0f, (float)r.NextDouble(), 0.7f), W(0.35f, 0.6f, 0f, 1f));
        }
    }

    // ------------------------------------------------------------------ grass tufts (instanced)
    private static readonly Mesh[] grass = new Mesh[4];

    /// <summary>A grass tuft: three crossed quads with one atlas cell, about 1 m wide and 0.55 m tall (scaled per instance).</summary>
    public static Mesh GrassTuft(int cell)
    {
        cell = ((cell % 4) + 4) % 4;
        if (grass[cell] != null) return grass[cell];
        var k = new MeshKit(1);
        for (int i = 0; i < 3; i++)
        {
            float a = (i * 60f + cell * 17f) * Mathf.Deg2Rad;
            Vector3 across = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Vector3 n = (Vector3.up * 0.85f + Vector3.Cross(across, Vector3.up).normalized * 0.3f).normalized;
            Vector4 c = GrassCells[cell];
            float aspect = (c.z - c.x) * 2f / Mathf.Max(0.01f, (c.w - c.y));   // atlas is 2:1
            float h = 0.55f;
            k.Blade(0, Vector3.zero, Vector3.up, across, h, h * aspect, c, n, W(0f, 0f, i / 3f, 0.6f), W(0.7f, 0.5f, i / 3f, 1f));
        }
        grass[cell] = k.ToMesh("Grass tuft " + cell, false);
        return grass[cell];
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetGrass() { for (int i = 0; i < grass.Length; i++) grass[i] = null; }
}
