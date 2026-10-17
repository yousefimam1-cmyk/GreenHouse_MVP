using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Material pass for imported models (Build.TryModel calls Upgrade once per instance): animals get matte fur / wool /
/// feather materials (no plastic shine; sheep and goats get the CC0 wool weave on their fleece), characters get matte
/// skin and cloth, and every model casts and receives shadows. Replacements are cached per source material, so all
/// copies of an animal share one material (batching, low RAM). Vehicles and crops are left alone (their own systems).
/// </summary>
public static class ModelLook
{
    private static readonly Dictionary<Material, Material> upgraded = new Dictionary<Material, Material>();
    public static int Upgraded { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { upgraded.Clear(); Upgraded = 0; }

    private static readonly string[] Animals = { "cow", "sheep", "goat", "chicken", "horse", "dog", "cat", "eagle", "camel", "pigeon", "bee", "duck", "donkey", "buffalo" };

    public static bool IsAnimal(string modelName)
    {
        string n = (modelName ?? "").ToLowerInvariant();
        foreach (var a in Animals) if (n.EndsWith(a) || n.Contains("/" + a)) return true;
        return false;
    }

    public static bool IsCharacter(string modelName) => (modelName ?? "").StartsWith("Characters/");

    /// <summary>Upgrade one freshly spawned model (no-op for vehicles, crops and unknown models).</summary>
    public static void Upgrade(GameObject go, string modelName)
    {
        if (go == null) return;
        bool animal = IsAnimal(modelName), person = IsCharacter(modelName);
        if (!animal && !person) return;
        string n = modelName.ToLowerInvariant();
        bool woolly = n.EndsWith("sheep") || n.EndsWith("goat");
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || r is ParticleSystemRenderer) continue;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            r.receiveShadows = true;
            var ms = r.sharedMaterials;
            if (ms == null) continue;
            bool changed = false;
            for (int i = 0; i < ms.Length; i++)
            {
                var m = ms[i];
                if (m == null) continue;
                Material up;
                if (!upgraded.TryGetValue(m, out up) || up == null)
                {
                    up = Make(m, animal, woolly);
                    upgraded[m] = up;
                }
                if (up != m) { ms[i] = up; changed = true; }
            }
            if (changed) r.sharedMaterials = ms;
        }
        Upgraded++;
    }

    private static Material Make(Material src, bool animal, bool woolly)
    {
        if (src.shader == null || !src.HasProperty("_Smoothness")) return src;
        Color c = src.HasProperty("_BaseColor") ? src.GetColor("_BaseColor") : src.color;
        // Fleece (the light, low-saturation parts of sheep / goats): CC0 wool weave tinted with the model's colour.
        if (woolly && c.grayscale > 0.55f && Saturation(c) < 0.25f && LookLibrary.IsTextured(LookMat.Wool) && src.mainTexture == null)
            return LookLibrary.Tinted(LookMat.Wool, new Color(Mathf.Min(1f, c.r * 1.08f), Mathf.Min(1f, c.g * 1.06f), Mathf.Min(1f, c.b * 1.02f)), 0.05f);
        var m = new Material(src);
        m.name = src.name + (animal ? " (fur)" : " (matte)");
        m.SetFloat("_Smoothness", animal ? 0.08f : 0.18f);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
        // (no new shader keywords here: a keyword no shipped material uses has no variant in the build)
        return m;
    }

    private static float Saturation(Color c)
    {
        float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b)), min = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
        return max <= 0.001f ? 0f : (max - min) / max;
    }
}
