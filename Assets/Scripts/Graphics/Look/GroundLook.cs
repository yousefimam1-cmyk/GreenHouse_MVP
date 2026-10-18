using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Real ground for every map: the terrain gets four PBR layers (albedo + normal + mask with height) painted by slope,
/// noise and biome (sand / gravel / rock in Najd; alluvial soil / grass / rock in the Delta; layer 3 = tilled soil or
/// dirt road for the world generator, see PaintArea), using URP Terrain/Lit with height blending. The dressing's
/// sand mesh, dunes and track use the same textures with world-space UVs, so they join the terrain without a seam.
/// After rain everything is darker and glossier (SetWetness). In the editor the TerrainData is cloned first so Play
/// mode never changes the asset.
/// </summary>
public static class GroundLook
{
    public const string TerrainMaterial = "Look/Materials/TerrainLook";

    public static int TerrainsDressed { get; private set; }
    public static Biome? LastBiome { get; private set; }
    private static readonly Dictionary<LookMat, TerrainLayer> layers = new Dictionary<LookMat, TerrainLayer>();
    private static readonly HashSet<TerrainData> cloned = new HashSet<TerrainData>();
    private static float wetness = -1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { TerrainsDressed = 0; LastBiome = null; layers.Clear(); cloned.Clear(); wetness = -1f; }

    /// <summary>The four terrain layers of a biome: [0] main ground, [1] patches, [2] rock (steep), [3] tilled / road.</summary>
    public static LookMat[] LayersOf(Biome b)
    {
        switch (b)
        {
            case Biome.Delta: return new[] { LookMat.Soil, LookMat.Grass, LookMat.Rock, LookMat.Tilled };
            case Biome.Oasis: return new[] { LookMat.Sand, LookMat.Grass, LookMat.Rock, LookMat.Tilled };
            default: return new[] { LookMat.Sand, LookMat.Gravel, LookMat.Rock, LookMat.Tilled };
        }
    }

    /// <summary>Material for the dressing's ground meshes: "ground" (open land), "track" (dirt road), "dune".</summary>
    public static Material MeshMaterial(Biome b, string role)
    {
        switch (role)
        {
            case "track": return LookLibrary.Get(b == Biome.Delta ? LookMat.Soil : LookMat.Gravel);
            case "dune": return LookLibrary.Get(LookMat.Sand);
            default: return LookLibrary.Get(b == Biome.Delta ? LookMat.Grass : LookMat.Sand);
        }
    }

    public static LookMat MeshLook(Biome b, string role) =>
        role == "track" ? (b == Biome.Delta ? LookMat.Soil : LookMat.Gravel) : role == "dune" ? LookMat.Sand : (b == Biome.Delta ? LookMat.Grass : LookMat.Sand);

    private static string FileOf(LookMat m) => m.ToString().ToLowerInvariant();

    /// <summary>A terrain layer for a look material (shared, cached); null when its textures are not in the build.</summary>
    public static TerrainLayer Layer(LookMat m)
    {
        TerrainLayer l;
        if (layers.TryGetValue(m, out l) && l != null) return l;
        var d = LookLibrary.Texture(FileOf(m) + "_d");
        if (d == null) return null;
        l = new TerrainLayer();
        l.name = "Look " + m;
        l.diffuseTexture = d;
        l.normalMapTexture = LookLibrary.Texture(FileOf(m) + "_n");
        l.maskMapTexture = LookLibrary.Texture(FileOf(m) + "_m");
        l.normalScale = 1f;
        l.tileSize = Vector2.one * LookLibrary.TileSize(m) * 1.6f;   // terrain is seen from further away: a little larger
        l.metallic = 0f;
        l.smoothness = m == LookMat.Rock ? 0.2f : 0.08f;
        l.diffuseRemapMin = Vector4.zero;
        l.diffuseRemapMax = Vector4.one;
        l.maskMapRemapMin = Vector4.zero;
        l.maskMapRemapMax = Vector4.one;
        layers[m] = l;
        return l;
    }

    /// <summary>Dress one terrain for a biome (layers + painted splat map + terrain material). False if textures are missing.</summary>
    public static bool ApplyTerrain(Terrain t, Biome biome)
    {
        if (t == null || t.terrainData == null) return false;
        var mats = LayersOf(biome);
        var ls = new TerrainLayer[mats.Length];
        for (int i = 0; i < mats.Length; i++) { ls[i] = Layer(mats[i]); if (ls[i] == null) return false; }
        var data = RuntimeData(t);
        data.terrainLayers = ls;
        Paint(data, t.transform.position, biome);
        var tm = Resources.Load<Material>(TerrainMaterial);
        if (tm != null && tm.shader != null && tm.shader.isSupported) t.materialTemplate = tm;
        t.basemapDistance = Mathf.Max(t.basemapDistance, 220f);
        TerrainsDressed++;
        LastBiome = biome;
        return true;
    }

    /// <summary>
    /// The terrain's data, safe to change at runtime: in the editor it is cloned once (and the collider pointed at the
    /// clone) so Play mode never writes into the TerrainData asset; in a build the loaded data is already a copy.
    /// Shared by the look (splat maps) and the open world (rim heights, World/WorldRim).
    /// </summary>
    public static TerrainData RuntimeData(Terrain t)
    {
        if (t == null || t.terrainData == null) return null;
        var data = t.terrainData;
        if (Application.isEditor && !cloned.Contains(data))
        {
            data = Object.Instantiate(data);
            data.name = t.terrainData.name + " (runtime)";
            cloned.Add(data);
            t.terrainData = data;
            var col = t.GetComponent<TerrainCollider>();
            if (col != null) col.terrainData = data;
        }
        return data;
    }

    /// <summary>Paint the splat map: rock on slopes, noise patches of the second layer, the main ground elsewhere.</summary>
    public static void Paint(TerrainData data, Vector3 origin, Biome biome)
    {
        int res = data.alphamapResolution;
        int layersN = data.alphamapLayers;
        if (res <= 0 || layersN < 3) return;
        var size = data.size;
        var a = new float[res, res, layersN];
        float patchLo = biome == Biome.Delta ? 0.42f : biome == Biome.Oasis ? 0.5f : 0.55f;
        float patchHi = patchLo + 0.18f;
        for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                float nx = (x + 0.5f) / res, nz = (y + 0.5f) / res;
                float steep = data.GetSteepness(nx, nz);
                float wx = origin.x + nx * size.x, wz = origin.z + nz * size.z;
                float rock = Smooth(24f, 40f, steep);
                float n = Mathf.PerlinNoise(wx * 0.021f + 3.7f, wz * 0.021f + 1.3f) * 0.7f + Mathf.PerlinNoise(wx * 0.09f, wz * 0.09f) * 0.3f;
                float patch = Smooth(patchLo, patchHi, n) * (1f - rock);
                a[y, x, 0] = Mathf.Max(0f, 1f - rock - patch);
                a[y, x, 1] = patch;
                a[y, x, 2] = rock;
                for (int l = 3; l < layersN; l++) a[y, x, l] = 0f;
            }
        data.SetAlphamaps(0, 0, a);
    }

    /// <summary>
    /// Paint one layer (0..3, see LayersOf) over a world-space area with soft edges, e.g. tilled soil under the world
    /// generator's fields (layer 3) or rock around a quarry (layer 2). strength 0..1.
    /// </summary>
    public static void PaintArea(Terrain t, Bounds area, int layer, float strength = 1f, float softEdge = 2f)
    {
        if (t == null || t.terrainData == null) return;
        var data = t.terrainData;
        int res = data.alphamapResolution, n = data.alphamapLayers;
        if (layer < 0 || layer >= n || res <= 0) return;
        Vector3 o = t.transform.position, size = data.size;
        int x0 = Mathf.Clamp(Mathf.FloorToInt((area.min.x - softEdge - o.x) / size.x * res), 0, res - 1);
        int x1 = Mathf.Clamp(Mathf.CeilToInt((area.max.x + softEdge - o.x) / size.x * res), 0, res - 1);
        int z0 = Mathf.Clamp(Mathf.FloorToInt((area.min.z - softEdge - o.z) / size.z * res), 0, res - 1);
        int z1 = Mathf.Clamp(Mathf.CeilToInt((area.max.z + softEdge - o.z) / size.z * res), 0, res - 1);
        int w = x1 - x0 + 1, h = z1 - z0 + 1;
        if (w <= 0 || h <= 0) return;
        var a = data.GetAlphamaps(x0, z0, w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float wx = o.x + (x0 + x + 0.5f) / res * size.x, wz = o.z + (z0 + y + 0.5f) / res * size.z;
                float dx = Mathf.Max(0f, Mathf.Max(area.min.x - wx, wx - area.max.x)), dz = Mathf.Max(0f, Mathf.Max(area.min.z - wz, wz - area.max.z));
                float k = strength * (1f - Mathf.Clamp01(Mathf.Sqrt(dx * dx + dz * dz) / Mathf.Max(0.01f, softEdge)));
                if (k <= 0f) continue;
                for (int l = 0; l < n; l++) a[y, x, l] *= 1f - k;
                a[y, x, layer] += k;
            }
        data.SetAlphamaps(x0, z0, a);
    }

    private static float Smooth(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }

    /// <summary>Wet terrain after rain: darker albedo, glossier surface (0 = dry).</summary>
    public static void SetWetness(float wet01)
    {
        if (Mathf.Abs(wet01 - wetness) < 0.02f) return;
        wetness = wet01;
        foreach (var l in layers.Values)
        {
            if (l == null) continue;
            l.diffuseRemapMax = new Vector4(1f - 0.38f * wet01, 1f - 0.4f * wet01, 1f - 0.42f * wet01, 1f);
            l.maskMapRemapMin = new Vector4(0f, 0f, 0f, 0.55f * wet01);
        }
    }

    /// <summary>World UVs and the look material for a ground mesh built by the dressing (sand, track, dunes).</summary>
    public static void DressMesh(MeshRenderer r, Biome b, string role)
    {
        if (r == null) return;
        var mf = r.GetComponent<MeshFilter>();
        var look = MeshLook(b, role);
        if (!LookLibrary.IsTextured(look)) return;     // keep the plain colour when the textures are not available
        // same UVs as the terrain's layer (terrain-local position / tile size), so mesh and terrain line up
        Vector3 terrainOrigin = Terrain.activeTerrain != null ? Terrain.activeTerrain.transform.position : Vector3.zero;
        if (mf != null && mf.sharedMesh != null) LookLibrary.PlanarUV(mf.sharedMesh, r.transform.position - terrainOrigin, LookLibrary.TileSize(look) * 1.6f);
        r.sharedMaterial = MeshMaterial(b, role);
    }
}
