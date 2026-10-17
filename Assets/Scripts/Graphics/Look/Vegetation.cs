using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Vegetation mixes for Vegetation.Scatter (one per kind of landscape in the Arab world).</summary>
public enum Biome
{
    /// <summary>Najd / open desert: acacia, ghaf and sidr far apart, dry grass tufts, desert shrubs.</summary>
    Desert,
    /// <summary>Irrigated oasis / date garden: dense date palms, sidr, green grass under the palms.</summary>
    Oasis,
    /// <summary>Nile Delta: eucalyptus windbreaks, sidr, lush green grass, a few palms.</summary>
    Delta,
    /// <summary>Wadi (dry river bed): acacia and ghaf along the bed, shrubs, mixed grass.</summary>
    Wadi,
    /// <summary>Coast (Batinah, Red Sea): date palms and ghaf, sparse dry grass.</summary>
    Coastal,
}

/// <summary>What a Scatter call placed (counts for tests and the world generator's own bookkeeping).</summary>
public struct ScatterResult
{
    public int Trees, Shrubs, Grass;
    public override string ToString() => "trees " + Trees + ", shrubs " + Shrubs + ", grass " + Grass;
}

/// <summary>
/// Public API of the graphics makeover's vegetation, for the farm dressing and the open-world generator:
/// <code>
///   Vegetation.PlaceTree(pos, TreeType.DatePalm);                          // one tree, LODs, collider, wind
///   Vegetation.Scatter(new Bounds(centre, size), Biome.Desert, seed, p => myRoads.Clear(p));
///   Vegetation.AddGrass(pos, Biome.Delta);                                  // one instanced grass tuft
///   Vegetation.BiomeOf(Regions.CurrentId);                                  // biome of the current map
/// </code>
/// Trees are GameObjects with a LODGroup (LOD0 / LOD1 / far cards, culled beyond ~1-2% of the screen) that share a
/// few meshes and materials per species (GPU instancing / SRP Batcher). Grass is drawn by FoliageGrass with
/// Graphics.RenderMeshInstanced (no GameObjects). Counts follow Settings > Graphics > Foliage (GraphicsEnhancer.DressingDensity).
/// Colliders sit on the Ignore Raycast layer (DesertProps.PropLayer) so ground raycasts never land on trees.
/// </summary>
public static class Vegetation
{
    /// <summary>LOD switch heights (fraction of screen height) for LOD0 -> LOD1 -> cards -> culled.</summary>
    public static readonly float[] LodHeights = { 0.16f, 0.05f, 0.012f };

    public static int TreesPlaced { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { TreesPlaced = 0; leafMats.Clear(); }

    /// <summary>Biome of a map id (regions.json): Najd = desert, Nile Delta = delta, others by their landscape.</summary>
    public static Biome BiomeOf(string regionId)
    {
        switch (regionId)
        {
            case "nile_delta": return Biome.Delta;
            case "mesopotamia": return Biome.Delta;
            case "batinah": return Biome.Coastal;
            case "jordan_valley": return Biome.Wadi;
            case "souss": return Biome.Wadi;
            default: return Biome.Desert;
        }
    }

    /// <summary>Biome of the map being played.</summary>
    public static Biome Current => BiomeOf(Regions.CurrentId);

    // ------------------------------------------------------------------ materials
    private static readonly Dictionary<TreeType, Material> leafMats = new Dictionary<TreeType, Material>();

    public static Material BarkMaterial(TreeType t)
    {
        switch (t)
        {
            case TreeType.DatePalm: return LookLibrary.Get(LookMat.PalmBark);
            case TreeType.Eucalyptus: return LookLibrary.Get(LookMat.EucBark);
            default: return LookLibrary.Get(LookMat.Bark);
        }
    }

    public static Material LeafMaterial(TreeType t)
    {
        Material m;
        if (leafMats.TryGetValue(t, out m) && m != null) return m;
        switch (t)
        {
            case TreeType.DatePalm: m = LookLibrary.Get(LookMat.PalmFrond); break;
            case TreeType.Acacia: m = LookLibrary.Get(LookMat.LeavesAcacia); break;
            case TreeType.Ghaf: m = LookLibrary.Tinted(LookMat.LeavesAcacia, new Color(0.78f, 0.86f, 0.8f)); break;    // grey-green
            case TreeType.Sidr: m = LookLibrary.Get(LookMat.LeavesSidr); break;
            case TreeType.Eucalyptus: m = LookLibrary.Get(LookMat.LeavesEucalyptus); break;
            case TreeType.Shrub: m = LookLibrary.Tinted(LookMat.LeavesSidr, new Color(0.82f, 0.86f, 0.72f)); break;   // dusty desert shrub
            default: m = LookLibrary.Get(LookMat.GrassDry); break;
        }
        m.enableInstancing = true;
        leafMats[t] = m;
        return m;
    }

    /// <summary>Instanced grass material (green for watered land, dry for the desert).</summary>
    public static Material GrassMaterial(bool dry)
    {
        var m = LookLibrary.Get(dry ? LookMat.GrassDry : LookMat.GrassGreen);
        m.enableInstancing = true;
        return m;
    }

    // ------------------------------------------------------------------ trees
    /// <summary>
    /// Place one tree / shrub on the ground at pos (y is snapped to the ground). Returns the root (named after the species,
    /// e.g. "Date Palm"), which has a LODGroup and, for trees, a trunk BoxCollider on the Ignore Raycast layer.
    /// </summary>
    public static GameObject PlaceTree(Vector3 pos, TreeType type, int seed = 0, Transform parent = null, bool snapToGround = true)
    {
        var rnd = new System.Random(seed != 0 ? seed : Mathf.RoundToInt(pos.x * 73.1f + pos.z * 31.7f));
        var root = new GameObject(DisplayName(type));
        if (parent != null) root.transform.SetParent(parent, false);
        root.transform.position = snapToGround ? Build.OnGround(pos) : pos;
        root.transform.rotation = Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f);
        float s = type == TreeType.DatePalm ? 0.9f + (float)rnd.NextDouble() * 0.25f : 0.8f + (float)rnd.NextDouble() * 0.45f;
        root.transform.localScale = Vector3.one * s;
        Assemble(root, type, rnd.Next(FoliageMeshes.Variants));
        if (type != TreeType.Shrub && type != TreeType.DryShrub)
        {
            var c = root.AddComponent<BoxCollider>();
            c.center = new Vector3(0f, 1.5f, 0f);
            c.size = type == TreeType.Eucalyptus || type == TreeType.DatePalm ? new Vector3(0.6f, 3f, 0.6f) : new Vector3(0.55f, 2.4f, 0.55f);
            root.layer = DesertProps.PropLayer;
        }
        TreesPlaced++;
        return root;
    }

    /// <summary>Build the LOD children (meshes + materials + LODGroup) of a vegetation root that already sits in place.</summary>
    public static void Assemble(GameObject root, TreeType type, int variant)
    {
        var meshes = FoliageMeshes.Get(type, variant);
        var bark = BarkMaterial(type);
        var leaves = LeafMaterial(type);
        var lods = new LOD[FoliageMeshes.Lods];
        for (int i = 0; i < FoliageMeshes.Lods; i++)
        {
            var go = new GameObject("LOD" + i);
            go.transform.SetParent(root.transform, false);
            go.layer = root.layer;
            go.AddComponent<MeshFilter>().sharedMesh = meshes[i];
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[] { bark, leaves };
            mr.shadowCastingMode = i < 2 ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = true;
            lods[i] = new LOD(LodHeights[i] * (type == TreeType.Shrub || type == TreeType.DryShrub ? 3f : 1f), new Renderer[] { mr });
        }
        var group = root.AddComponent<LODGroup>();
        group.fadeMode = LODFadeMode.None;     // LOD cross-fade variants are stripped from the build (MazraaShaderStripper)
        group.SetLODs(lods);
        group.RecalculateBounds();
    }

    public static string DisplayName(TreeType t)
    {
        switch (t)
        {
            case TreeType.DatePalm: return "Date Palm";
            case TreeType.DryShrub: return "Dry Shrub";
            default: return t.ToString();
        }
    }

    // ------------------------------------------------------------------ grass
    /// <summary>One instanced grass tuft (green or dry by biome) at pos (ground-snapped). Scale ~0.7..1.3.</summary>
    public static void AddGrass(Vector3 pos, Biome biome, float scale = 1f, int seed = 0)
    {
        bool dry = biome == Biome.Desert || biome == Biome.Coastal || (biome == Biome.Wadi && (seed & 1) == 0);
        FoliageGrass.Ensure().Add(Build.OnGround(pos), dry, scale, seed);
    }

    // ------------------------------------------------------------------ scatter
    /// <summary>Trees, shrubs and grass per 10,000 m2 (one hectare) at Foliage 100%.</summary>
    private struct Mix { public float trees, shrubs, grass; public TreeType[] kinds; public float[] weights; }

    private static Mix MixOf(Biome b)
    {
        switch (b)
        {
            case Biome.Oasis: return new Mix { trees = 26f, shrubs = 10f, grass = 900f, kinds = new[] { TreeType.DatePalm, TreeType.Sidr }, weights = new[] { 0.85f, 0.15f } };
            case Biome.Delta: return new Mix { trees = 8f, shrubs = 8f, grass = 1400f, kinds = new[] { TreeType.Eucalyptus, TreeType.Sidr, TreeType.DatePalm }, weights = new[] { 0.55f, 0.3f, 0.15f } };
            case Biome.Wadi: return new Mix { trees = 6f, shrubs = 14f, grass = 500f, kinds = new[] { TreeType.Acacia, TreeType.Ghaf, TreeType.Sidr }, weights = new[] { 0.5f, 0.3f, 0.2f } };
            case Biome.Coastal: return new Mix { trees = 7f, shrubs = 6f, grass = 220f, kinds = new[] { TreeType.DatePalm, TreeType.Ghaf }, weights = new[] { 0.7f, 0.3f } };
            default: return new Mix { trees = 2.2f, shrubs = 9f, grass = 260f, kinds = new[] { TreeType.Acacia, TreeType.Ghaf, TreeType.Sidr }, weights = new[] { 0.5f, 0.25f, 0.25f } };
        }
    }

    /// <summary>
    /// Fill an area (world-space bounds, y ignored) with the biome's vegetation. canPlace(p) lets the caller keep roads,
    /// fields, buildings and water clear (return false to refuse a point). density = 0..1 (default: the Foliage setting).
    /// Deterministic for a seed. Trees go under parent (or a "Vegetation" root), grass to FoliageGrass.
    /// </summary>
    public static ScatterResult Scatter(Bounds area, Biome biome, int seed = 1, Func<Vector3, bool> canPlace = null, Transform parent = null, float density = -1f)
    {
        var res = new ScatterResult();
        if (density < 0f) density = GraphicsEnhancer.DressingDensity;
        if (density <= 0.001f) return res;
        var mix = MixOf(biome);
        var rnd = new System.Random(seed);
        float hectares = Mathf.Max(0f, area.size.x * area.size.z) / 10000f;
        if (parent == null) parent = Root();
        var placedTrees = new List<Vector3>();

        int trees = Mathf.RoundToInt(mix.trees * hectares * density);
        for (int tries = trees * 8; res.Trees < trees && tries > 0; tries--)
        {
            Vector3 p = RandomIn(area, rnd);
            if (!Clumped(p, seed, 0.35f) && rnd.NextDouble() < 0.6) continue;     // trees grow in loose groups
            if (!Free(p, placedTrees, 5f) || (canPlace != null && !canPlace(p))) continue;
            PlaceTree(p, Pick(mix, rnd), rnd.Next(1, int.MaxValue), parent);
            placedTrees.Add(p); res.Trees++;
        }
        int shrubs = Mathf.RoundToInt(mix.shrubs * hectares * density);
        for (int tries = shrubs * 6; res.Shrubs < shrubs && tries > 0; tries--)
        {
            Vector3 p = RandomIn(area, rnd);
            if (!Free(p, placedTrees, 2f) || (canPlace != null && !canPlace(p))) continue;
            bool dry = biome == Biome.Desert || biome == Biome.Coastal ? rnd.NextDouble() < 0.6 : rnd.NextDouble() < 0.2;
            PlaceTree(p, dry ? TreeType.DryShrub : TreeType.Shrub, rnd.Next(1, int.MaxValue), parent);
            placedTrees.Add(p); res.Shrubs++;
        }
        res.Grass = ScatterGrass(area, biome, rnd.Next(), canPlace, density);
        return res;
    }

    /// <summary>Only the grass part of Scatter (cheap: no GameObjects). Returns the number of tufts added.</summary>
    public static int ScatterGrass(Bounds area, Biome biome, int seed, Func<Vector3, bool> canPlace = null, float density = -1f)
    {
        if (density < 0f) density = GraphicsEnhancer.DressingDensity;
        var mix = MixOf(biome);
        var rnd = new System.Random(seed);
        float hectares = Mathf.Max(0f, area.size.x * area.size.z) / 10000f;
        int want = Mathf.Min(FoliageGrass.MaxInstances, Mathf.RoundToInt(mix.grass * hectares * density));
        int added = 0;
        for (int tries = want * 3; added < want && tries > 0; tries--)
        {
            Vector3 p = RandomIn(area, rnd);
            // patchy: grass grows in clumps (noise), denser in wet biomes
            float clump = Mathf.PerlinNoise(p.x * 0.045f + seed * 0.001f, p.z * 0.045f);
            if (clump < (biome == Biome.Delta || biome == Biome.Oasis ? 0.3f : 0.52f)) continue;
            if (canPlace != null && !canPlace(p)) continue;
            AddGrass(p, biome, 0.7f + (float)rnd.NextDouble() * 0.6f, rnd.Next());
            added++;
        }
        return added;
    }

    /// <summary>Default parent for scattered trees.</summary>
    public static Transform Root()
    {
        var go = GameObject.Find("Vegetation");
        if (go == null) go = new GameObject("Vegetation");
        return go.transform;
    }

    private static Vector3 RandomIn(Bounds b, System.Random r) =>
        new Vector3(b.min.x + (float)r.NextDouble() * b.size.x, b.center.y, b.min.z + (float)r.NextDouble() * b.size.z);

    private static bool Clumped(Vector3 p, int seed, float threshold) => Mathf.PerlinNoise(p.x * 0.02f + (seed % 100) * 0.37f, p.z * 0.02f) > threshold;

    private static bool Free(Vector3 p, List<Vector3> placed, float r)
    {
        foreach (var q in placed) { float dx = p.x - q.x, dz = p.z - q.z; if (dx * dx + dz * dz < r * r) return false; }
        return true;
    }

    private static TreeType Pick(Mix m, System.Random r)
    {
        double x = r.NextDouble(), acc = 0;
        for (int i = 0; i < m.kinds.Length; i++) { acc += m.weights[i]; if (x <= acc) return m.kinds[i]; }
        return m.kinds[m.kinds.Length - 1];
    }
}
