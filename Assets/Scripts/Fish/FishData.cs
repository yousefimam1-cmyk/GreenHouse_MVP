using System;
using System.Collections.Generic;
using UnityEngine;

// Data for the "fish" feature group (fishing, aquaculture, processing, Saudi animals).
// Fishing data: Resources/Data/fish.json. Processing: Resources/Data/processing.json.
// Goods and animals: Resources/Data/Economy/10_fish.json. Arabic text: Resources/Data/Loc/10_fish.json.

[Serializable]
public class FishSpecies
{
    public string id, good, nameEn, nameAr;
    public float minKg = 0.5f, maxKg = 1f, chance = 1f;
    public string[] seasons;   // empty = all year (spring, summer, autumn, winter)
    public string[] times;     // empty = any time (dawn, day, dusk, night)
    public bool coast;         // coast fish (hamour, najil): rare, sold higher
    public int minRod;         // rod level needed
    public string bait;        // bait tag needed ("" = any)
}

[Serializable] public class FishRod { public string id, nameEn, nameAr; public int price, level; public float chanceBonus, windowBonus; }
[Serializable] public class FishBait { public string id, nameEn, nameAr, tag; public int price, count = 10; public float chanceBonus, waitFactor = 1f; }
[Serializable] public class FishTankDef { public int fingerlingPrice = 400, fingerlings = 100; public float growDays = 3f, kgPerFish = 0.45f, feedPerDay = 1f, starveLoss = 0.2f; }

[Serializable]
public class FishFile
{
    public float baseChance = 0.55f, baseWindow = 1.3f, waitMin = 3f, waitMax = 9f;
    public FishSpecies[] species;
    public FishRod[] rods;
    public FishBait[] baits;
    public FishTankDef tank;
}

[Serializable] public class ProcRecipe { public string[] inputs; public float inputAmount = 10f; public string output; public float outputAmount = 5f; }
[Serializable] public class ProcBuilding { public string id, nameEn, nameAr; public int price = 3000; public float seconds = 60f, r = 0.8f, g = 0.7f, b = 0.5f; public ProcRecipe[] recipes; }
[Serializable] public class ProcFile { public ProcBuilding[] buildings; }

/// <summary>Pure fishing rules (time bands, eligible species, weighted pick, catch chance). Tested headless.</summary>
public static class FishRules
{
    /// <summary>dawn 05-08, day 08-17, dusk 17-20, night 20-05.</summary>
    public static string Band(float hour)
    {
        hour = ((hour % 24f) + 24f) % 24f;
        if (hour >= 5f && hour < 8f) return "dawn";
        if (hour >= 8f && hour < 17f) return "day";
        if (hour >= 17f && hour < 20f) return "dusk";
        return "night";
    }

    /// <summary>Clock hour (the in-game day starts at 06:00).</summary>
    public static float Hour() { var c = WorldClock.Instance; return c == null ? 10f : (6f + c.DayProgress * 24f) % 24f; }
    public static string SeasonNow() => WorldClock.Instance == null ? "spring" : WorldClock.SeasonId(WorldClock.Instance.Season);

    private static bool Has(string[] list, string v)
    {
        if (list == null || list.Length == 0) return true;
        foreach (var s in list) if (string.Equals(s, v, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public static bool Allowed(FishSpecies s, string season, string band, int rodLevel, string baitTag) =>
        s != null && Has(s.seasons, season) && Has(s.times, band) && rodLevel >= s.minRod && (string.IsNullOrEmpty(s.bait) || s.bait == baitTag);

    public static List<FishSpecies> Eligible(FishFile f, string season, string band, int rodLevel, string baitTag)
    {
        var list = new List<FishSpecies>();
        if (f != null && f.species != null) foreach (var s in f.species) if (Allowed(s, season, band, rodLevel, baitTag)) list.Add(s);
        return list;
    }

    /// <summary>Weighted pick by chance; r is 0..1.</summary>
    public static FishSpecies Pick(List<FishSpecies> list, float r)
    {
        if (list == null || list.Count == 0) return null;
        float total = 0f; foreach (var s in list) total += Mathf.Max(0f, s.chance);
        if (total <= 0f) return list[0];
        float x = Mathf.Clamp01(r) * total;
        foreach (var s in list) { x -= Mathf.Max(0f, s.chance); if (x <= 0f) return s; }
        return list[list.Count - 1];
    }

    public static float CatchChance(FishFile f, FishRod rod, FishBait bait) =>
        Mathf.Clamp((f != null ? f.baseChance : 0.55f) + (rod != null ? rod.chanceBonus : 0f) + (bait != null ? bait.chanceBonus : 0f), 0.05f, 0.97f);

    public static float Window(FishFile f, FishRod rod) => (f != null ? f.baseWindow : 1.3f) + (rod != null ? rod.windowBonus : 0f);

    public static string Name(FishSpecies s) => s == null ? "" : (Loc.IsArabic && !string.IsNullOrEmpty(s.nameAr) ? s.nameAr : s.nameEn);
    public static string Name(string en, string ar) => Loc.IsArabic && !string.IsNullOrEmpty(ar) ? ar : en;
}

/// <summary>Translation helpers for this feature (keys and Arabic live in Resources/Data/Loc/10_fish.json).</summary>
public static class FishText
{
    public static string T(string key) => Loc.T(key);
    public static string T(string key, params object[] args) => Loc.T(key, args);
    public static string Raw(string key) => Loc.Raw(key);
}

/// <summary>Where the feature's buildings go: relative to the player's start (like VehicleSpawner), or a scene object with the given name.</summary>
public static class FishSite
{
    public static void Frame(out Vector3 start, out Vector3 fwd, out Vector3 right)
    {
        var inputs = UnityEngine.Object.FindAnyObjectByType<StarterAssets.StarterAssetsInputs>();
        Transform t = inputs != null ? inputs.transform : (Camera.main != null ? Camera.main.transform : null);
        start = t != null ? t.position : Vector3.zero;
        fwd = t != null ? Vector3.ProjectOnPlane(t.forward, Vector3.up).normalized : Vector3.forward;
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
        right = Vector3.Cross(Vector3.up, fwd);
    }

    /// <summary>Scene object named spotName if present, else start + right*r + forward*f (on the ground).</summary>
    public static Vector3 At(string spotName, float r, float f)
    {
        var go = GameObject.Find(spotName);
        if (go != null) return Build.OnGround(go.transform.position);
        Frame(out var s, out var fwd, out var right);
        return Build.OnGround(s + right * r + fwd * f);
    }

    public static Quaternion Facing()
    {
        Frame(out var s, out var fwd, out var right);
        return Quaternion.LookRotation(fwd);
    }

    /// <summary>Put every renderer under root on a layer (7 = Interactable, like the pen crate / shop stall).</summary>
    public static void SetLayer(GameObject root, int layer)
    {
        root.layer = layer;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true)) r.gameObject.layer = layer;
    }

    /// <summary>Tint an imported model (keeps its textures).</summary>
    public static void Tint(GameObject model, Color c)
    {
        if (model == null) return;
        foreach (var r in model.GetComponentsInChildren<Renderer>(true))
        {
            var m = r.material;
            if (m == null) continue;
            m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        }
    }
}
