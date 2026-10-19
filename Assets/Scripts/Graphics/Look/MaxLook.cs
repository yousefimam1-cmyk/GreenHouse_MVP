using UnityEngine;

/// <summary>
/// The top end of the graphics presets (called by AtmosphereLook.ApplyPreset; cheap when nothing changed):
/// - High / Ultra: canals, ditches and the Delta river draw with the Water material (HDRP/Lit transparent, glossy, SSR /
///   sky reflections) instead of the flat colour; people and animals get their character copy (ModelLook).
/// - Ultra: the nine main ground / wall / palm sets switch to their 2K albedo + normal (Resources/Look/Textures2K, CC0
///   Poly Haven, same sources as the 1K set), on materials and terrain layers; open sand and dunes get parallax.
/// Lower presets swap everything back. Missing shaders or textures leave the plain look in place (never pink).
/// Exposure is not touched here (GraphicsEnhancer owns it).
/// </summary>
public static class MaxLook
{
    public const string WaterMaterialPath = "Look/Materials/Water";
    /// <summary>The flat water colour of DesertDressing / OpenWorld (one shared Build.Mat material).</summary>
    public static readonly Color PlainWaterColor = new Color(0.2f, 0.36f, 0.4f);
    public const float PlainWaterSmoothness = 0.92f;

    public static bool WaterOn => GraphicsEnhancer.Preset >= GraphicsEnhancer.PresetHigh;
    public static bool CharactersOn => GraphicsEnhancer.Preset >= GraphicsEnhancer.PresetHigh;
    public static bool HiResOn => GraphicsEnhancer.Preset >= GraphicsEnhancer.PresetUltra;
    public static bool ParallaxOn => GraphicsEnhancer.Preset >= GraphicsEnhancer.PresetUltra;

    /// <summary>Times a preset change was applied, and renderers swapped by the last one (tests / diagnostics).</summary>
    public static int Applied { get; private set; }
    public static int WaterSwaps { get; private set; }
    public static int CharacterSwaps { get; private set; }

    private static Material shaderWater;
    private static bool waterTried;
    private static int lastPreset = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { shaderWater = null; waterTried = false; lastPreset = -1; Applied = WaterSwaps = CharacterSwaps = 0; }

    public static Material PlainWater => Build.Mat(PlainWaterColor, PlainWaterSmoothness);

    /// <summary>The Water material (Resources/Look/Materials/Water, HDRP/Lit transparent), or null when missing / unsupported.</summary>
    public static Material ShaderWater()
    {
        if (!waterTried)
        {
            waterTried = true;
            var m = Resources.Load<Material>(WaterMaterialPath);
            if (m != null && m.shader != null && m.shader.isSupported) shaderWater = m;
        }
        return shaderWater;
    }

    /// <summary>Water material for new canals / ditches / river strips at the current preset.</summary>
    public static Material WaterMaterial()
    {
        if (WaterOn) { var w = ShaderWater(); if (w != null) return w; }
        return PlainWater;
    }

    /// <summary>Apply the current preset (no-op when it is the one already applied, unless force).</summary>
    public static void Apply(bool force = false)
    {
        int p = GraphicsEnhancer.Preset;
        if (p == lastPreset && !force) return;
        lastPreset = p;
        Applied++;
        LookLibrary.SetHighRes(HiResOn);
        GroundLook.SetHighRes(HiResOn);
        WaterSwaps = SwapWater();
        CharacterSwaps = ModelLook.Refresh();
    }

    private static int SwapWater()
    {
        var to = WaterMaterial();
        var from = to == PlainWater ? shaderWater : PlainWater;
        if (from == null || from == to) return 0;
        int n = 0;
        foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            if (r == null) continue;
            var ms = r.sharedMaterials;
            if (ms == null || ms.Length == 0) { if (r.sharedMaterial == from) { r.sharedMaterial = to; n++; } continue; }
            bool changed = false;
            for (int i = 0; i < ms.Length; i++) if (ms[i] == from) { ms[i] = to; changed = true; }
            if (changed) { r.sharedMaterials = ms; n++; }
        }
        return n;
    }
}
