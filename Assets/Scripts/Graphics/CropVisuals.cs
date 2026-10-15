using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Crop models by growth stage. Uses the original FarmCrops models (copied to Resources/Models/FarmCrops, four plant
/// stages per crop: seedling, young, grown, ripe) and falls back to the Kenney placeholders in Resources/Models/Crops.
/// Get() returns a path under Resources/Models (or null = use primitives). Spawn() places a model, and
/// TryGetMesh() gives the mesh + materials so existing scene plants (greenhouse beds) can be swapped in place.
/// FarmCrops models are real-world size (metres) with the pivot at soil level, so they are not re-fitted.
/// </summary>
public static class CropVisuals
{
    public const string Folder = "FarmCrops/";

    // crop id -> FarmCrops model names, young to ripe (stages _05.._07 in the pack are harvested produce, not plants)
    private static readonly Dictionary<string, string[]> stages = new Dictionary<string, string[]>
    {
        { "wheat",      new[] { "Wheat_01", "Wheat_02a", "Wheat_03a", "Wheat_04a" } },
        { "potato",     new[] { "Potato_01", "Potato_02", "Potato_03", "Potato_04" } },
        { "onion",      new[] { "Onion_01", "Onion_02", "Onion_03b", "Onion_04" } },
        { "watermelon", new[] { "Watermelon_01", "Watermelon_02", "Watermelon_03", "Watermelon_04" } },
        { "tomato",     new[] { "Tomato_01", "Tomato_02", "Tomato_03", "Tomato_04" } },
        { "cucumber",   new[] { "Cucumber_01", "Cucumber_02", "Cucumber_03", "Cucumber_04" } },
        { "corn",       new[] { "Corn_01", "Corn_02", "Corn_03", "Corn_04" } },
        { "cabbage",    new[] { "Cabbage_01", "Cabbage_02", "Cabbage_03", "Cabbage_04" } },
        { "pumpkin",    new[] { "Pumpkin_01", "Pumpkin_02", "Pumpkin_03", "Pumpkin_04" } },
        { "carrot",     new[] { "Carrot_01", "Carrot_02", "Carrot_03", "Carrot_04" } },
    };

    // other crop ids that look close enough to a FarmCrops crop
    private static readonly Dictionary<string, string> aliases = new Dictionary<string, string>
    {
        { "melon", "watermelon" }, { "lettuce", "cabbage" }, { "maize", "corn" }, { "sweetcorn", "corn" },
        { "squash", "pumpkin" }, { "barley", "wheat" }, { "alfalfa", "wheat" }, { "millet", "wheat" }, { "sorghum", "corn" }, { "garlic", "onion" },
    };

    private static readonly Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();
    private static readonly HashSet<string> missing = new HashSet<string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { prefabs.Clear(); missing.Clear(); }

    /// <summary>The FarmCrops crop used for a crop id (after aliases), or null.</summary>
    public static string Resolve(string cropId)
    {
        if (string.IsNullOrEmpty(cropId)) return null;
        string id = cropId.ToLowerInvariant();
        if (stages.ContainsKey(id)) return id;
        string a;
        return aliases.TryGetValue(id, out a) ? a : null;
    }

    /// <summary>True when FarmCrops stage models exist for this crop.</summary>
    public static bool HasModels(string cropId) => Resolve(cropId) != null;

    public static IEnumerable<string> Crops => stages.Keys;

    /// <summary>Stage index 0..3 for growth 0..1 (1 = ripe = last stage).</summary>
    public static int StageIndex(float growth01, int count = 4)
    {
        if (count <= 1) return 0;
        if (growth01 >= 0.999f) return count - 1;
        return Mathf.Clamp(Mathf.FloorToInt(Mathf.Max(0f, growth01) * (count - 1) + 0.0001f), 0, count - 2);
    }

    /// <summary>
    /// Model path under Resources/Models for this crop at this growth: FarmCrops stage if the crop has one,
    /// else a Kenney placeholder; null means "build primitives".
    /// </summary>
    public static string Get(string cropId, float growth01)
    {
        string crop = Resolve(cropId);
        if (crop != null) { var s = stages[crop]; return Folder + s[StageIndex(growth01, s.Length)]; }
        return KenneyPath(cropId, growth01);
    }

    /// <summary>The Kenney Nature Kit placeholder for a crop (Resources/Models/Crops).</summary>
    public static string KenneyPath(string cropId, float growth01)
    {
        int stage = StageIndex(growth01);
        bool wheat = cropId == "wheat";
        if (stage == 0) return "Crops/crops_leafsStageA";
        if (stage < 3) return wheat ? "Crops/crops_wheatStageA" : "Crops/crops_leafsStageB";
        switch (cropId)
        {
            case "wheat": return "Crops/crops_wheatStageB";
            case "watermelon": case "melon": return "Crops/crop_melon";
            case "onion": return "Crops/crop_turnip";
            case "carrot": return "Crops/crop_carrot";
            default: return "Crops/crops_leafsStageB";
        }
    }

    /// <summary>The loaded model for Get(cropId, growth), or null.</summary>
    public static GameObject Prefab(string cropId, float growth01) => Load(Get(cropId, growth01));

    private static GameObject Load(string path)
    {
        if (path == null || missing.Contains(path)) return null;
        GameObject p;
        if (prefabs.TryGetValue(path, out p) && p != null) return p;
        p = Resources.Load<GameObject>("Models/" + path);
        if (p == null) { missing.Add(path); return null; }
        prefabs[path] = p;
        return p;
    }

    /// <summary>
    /// Place the crop model under parent. FarmCrops models keep their real size (capped at maxSpread metres across);
    /// Kenney placeholders are fitted to kenneyFit. Returns null if no model exists (caller builds primitives).
    /// </summary>
    public static GameObject Spawn(string cropId, float growth01, Transform parent, float kenneyFit, float maxSpread = 1.15f)
    {
        string path = Get(cropId, growth01);
        if (path == null) return null;
        if (!path.StartsWith(Folder)) return Build.TryModel(path, parent, kenneyFit, 0f);
        var go = Build.TryModel(path, parent);
        if (go == null) return Build.TryModel(KenneyPath(cropId, growth01), parent, kenneyFit, 0f);
        HideCollisionMeshes(go);
        CapSize(go, parent, maxSpread);
        return go;
    }

    /// <summary>FarmCrops models carry a "C_" collision mesh next to the visible one: never render it.</summary>
    public static void HideCollisionMeshes(GameObject go)
    {
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            if (r.gameObject.name.StartsWith("C_")) r.enabled = false;
    }

    private static void CapSize(GameObject go, Transform parent, float maxSpread)
    {
        if (parent == null || maxSpread <= 0f) return;
        bool any = false; float minX = 0, maxX = 0, minZ = 0, maxZ = 0;
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            if (!r.enabled) continue;
            var b = r.bounds;
            Vector3 a = parent.InverseTransformPoint(b.min), c = parent.InverseTransformPoint(b.max);
            float x0 = Mathf.Min(a.x, c.x), x1 = Mathf.Max(a.x, c.x), z0 = Mathf.Min(a.z, c.z), z1 = Mathf.Max(a.z, c.z);
            if (!any) { minX = x0; maxX = x1; minZ = z0; maxZ = z1; any = true; }
            else { minX = Mathf.Min(minX, x0); maxX = Mathf.Max(maxX, x1); minZ = Mathf.Min(minZ, z0); maxZ = Mathf.Max(maxZ, z1); }
        }
        float spread = any ? Mathf.Max(maxX - minX, maxZ - minZ) : 0f;
        if (spread > maxSpread) go.transform.localScale *= maxSpread / spread;
    }

    /// <summary>Mesh + materials of the visible (non "C_") part of the stage model, for swapping into scene plants.</summary>
    public static bool TryGetMesh(string cropId, float growth01, out Mesh mesh, out Material[] materials)
    {
        mesh = null; materials = null;
        if (!HasModels(cropId)) return false;
        var p = Prefab(cropId, growth01);
        if (p == null) return false;
        foreach (var mf in p.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.gameObject.name.StartsWith("C_") || mf.sharedMesh == null) continue;
            var r = mf.GetComponent<MeshRenderer>();
            if (r == null) continue;
            mesh = mf.sharedMesh; materials = r.sharedMaterials;
            return true;
        }
        return false;
    }

    /// <summary>Guess the FarmCrops crop from an object name such as "Watermelon (2)" or "Carrot_03".</summary>
    public static string CropFromName(string objectName)
    {
        if (string.IsNullOrEmpty(objectName)) return null;
        string n = objectName.ToLowerInvariant();
        if (n.StartsWith("c_")) return null;
        foreach (var k in stages.Keys) if (n.StartsWith(k)) return k;
        return null;
    }
}
