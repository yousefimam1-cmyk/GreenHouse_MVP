using UnityEngine;

/// <summary>
/// Render-pipeline bridge for materials and lights (the game runs on HDRP 17; property names differ from URP/Built-in).
/// Every runtime material edit goes through here so HDRP/Lit names are used when the shader has them and the old URP /
/// Built-in names still work on a fallback shader (headless tests, a stripped install). Never returns a pink material:
/// Lit() falls back HDRP/Lit -> URP/Lit -> Standard.
/// Light units: the game's light intensities are in the old URP scale (sun ~1, lamps ~1-3). HDRP is physical (lux / lumen),
/// so HdrpLook keeps a fixed exposure that maps a 1-lux sun to the old brightness (ExposureEV), and LegacyLight() converts
/// lamp intensities (URP point light I ~ I candela; HDRP punctual lights are set in lumen = 4 pi cd).
/// </summary>
public static class Rp
{
    public const string LitName = "HDRP/Lit", UnlitName = "HDRP/Unlit", TerrainLitName = "HDRP/TerrainLit";
    /// <summary>Fixed exposure (EV100) used by HdrpLook: exposure x pi, so a Lambert surface under a 1-lux sun reads like URP intensity 1.</summary>
    public const float ExposureEV = -1.91f;
    /// <summary>HDRP divides diffuse light by pi and HdrpLook's exposure multiplies by pi: emissive colours are divided by pi to keep the URP brightness.</summary>
    public const float EmissiveScale = 1f / Mathf.PI;
    public const float LumenPerLegacy = 4f * Mathf.PI;

    private static Shader lit, unlit;
    private static bool litTried, unlitTried;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { lit = unlit = null; litTried = unlitTried = false; }

    /// <summary>True when the HDRP/Lit shader is in the build and supported.</summary>
    public static bool IsHdrp { get { Lit(); return lit != null && lit.name == LitName; } }

    /// <summary>The lit shader: HDRP/Lit, else URP/Lit, else Standard (never null in a player).</summary>
    public static Shader Lit()
    {
        if (!litTried || lit == null)
        {
            litTried = true;
            lit = Supported(Shader.Find(LitName)) ?? Supported(Shader.Find("Universal Render Pipeline/Lit")) ?? Shader.Find("Standard");
        }
        return lit;
    }

    /// <summary>Unlit shader for particles / glows: HDRP/Unlit, else URP particles, else a built-in particle shader.</summary>
    public static Shader Unlit()
    {
        if (!unlitTried || unlit == null)
        {
            unlitTried = true;
            unlit = Supported(Shader.Find(UnlitName)) ?? Supported(Shader.Find("Universal Render Pipeline/Particles/Unlit")) ?? Supported(Shader.Find("Particles/Standard Unlit")) ?? Lit();
        }
        return unlit;
    }

    private static Shader Supported(Shader s) => s != null && s.isSupported ? s : null;

    public static bool IsHdrpLit(Material m) => m != null && m.shader != null && m.shader.name == LitName;
    public static bool IsHdrpMaterial(Material m) => m != null && m.shader != null && m.shader.name != null && m.shader.name.StartsWith("HDRP/");

    // ------------------------------------------------------------------ property names
    public static string BaseMapName(Material m) => m != null && m.HasProperty("_BaseColorMap") && IsHdrpMaterial(m) ? "_BaseColorMap"
        : m != null && m.HasProperty("_UnlitColorMap") && IsHdrpMaterial(m) ? "_UnlitColorMap" : m != null && m.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
    public static string NormalMapName(Material m) => IsHdrpMaterial(m) ? "_NormalMap" : "_BumpMap";
    public static string ColorName(Material m) => IsHdrpMaterial(m) && m.shader.name == UnlitName ? "_UnlitColor" : "_BaseColor";

    public static Texture GetBaseMap(Material m)
    {
        if (m == null) return null;
        var t = m.HasProperty(BaseMapName(m)) ? m.GetTexture(BaseMapName(m)) : null;
        if (t == null && m.HasProperty("_BaseMap")) t = m.GetTexture("_BaseMap");
        if (t == null && m.HasProperty("_MainTex")) t = m.GetTexture("_MainTex");
        return t;
    }

    public static void SetBaseMap(Material m, Texture t)
    {
        if (m == null) return;
        m.SetTexture(BaseMapName(m), t);
        if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", t);
    }

    public static Texture GetNormalMap(Material m) => m == null ? null : m.GetTexture(NormalMapName(m));

    public static void SetNormalMap(Material m, Texture t)
    {
        if (m == null) return;
        m.SetTexture(NormalMapName(m), t);
        if (t != null) { m.EnableKeyword("_NORMALMAP"); if (IsHdrpMaterial(m)) m.EnableKeyword("_NORMALMAP_TANGENT_SPACE"); }
        else m.DisableKeyword("_NORMALMAP");
    }

    /// <summary>Texture tiling for the whole material (HDRP/Lit shares the base map's UV transform with every map).</summary>
    public static void SetTiling(Material m, Vector2 tiling)
    {
        if (m == null) return;
        if (IsHdrpMaterial(m)) { m.SetTextureScale(BaseMapName(m), tiling); return; }
        foreach (var t in new[] { "_BaseMap", "_BumpMap", "_OcclusionMap", "_MetallicGlossMap" }) if (m.HasProperty(t)) m.SetTextureScale(t, tiling);
    }

    public static void SetColor(Material m, Color c)
    {
        if (m == null) return;
        string n = ColorName(m);
        if (m.HasProperty(n)) m.SetColor(n, c);
        m.color = c;
    }

    public static Color GetColor(Material m)
    {
        if (m == null) return Color.white;
        string n = ColorName(m);
        return m.HasProperty(n) ? m.GetColor(n) : m.color;
    }

    /// <summary>Smoothness; on HDRP/Lit with a mask map the remap range carries it (constant smoothness like the URP materials).</summary>
    public static void SetSmoothness(Material m, float s)
    {
        if (m == null) return;
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", s);
        if (IsHdrpLit(m) && m.HasProperty("_SmoothnessRemapMax") && m.GetTexture("_MaskMap") != null)
        {
            m.SetFloat("_SmoothnessRemapMin", s);
            m.SetFloat("_SmoothnessRemapMax", s);
        }
    }

    public static float GetSmoothness(Material m, float fallback = 0.1f) => m != null && m.HasProperty("_Smoothness") ? m.GetFloat("_Smoothness") : fallback;

    public static void SetMetallic(Material m, float v)
    {
        if (m == null) return;
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", v);
        if (IsHdrpLit(m) && m.HasProperty("_MetallicRemapMax") && m.GetTexture("_MaskMap") != null) { m.SetFloat("_MetallicRemapMin", v); m.SetFloat("_MetallicRemapMax", v); }
    }

    public static void SetEmission(Material m, Color e)
    {
        if (m == null) return;
        if (IsHdrpMaterial(m)) { m.SetColor("_EmissiveColor", e * EmissiveScale); }
        else { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", e); }
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
    }

    /// <summary>Alpha-blended surface (HDRP surface type Transparent / URP _Surface 1).</summary>
    public static void MakeTransparent(Material m)
    {
        if (m == null) return;
        if (IsHdrpMaterial(m))
        {
            m.SetFloat("_SurfaceType", 1f); m.SetFloat("_BlendMode", 0f);
            m.SetFloat("_ZWrite", 0f); m.SetFloat("_TransparentZWrite", 0f);
            m.SetFloat("_SrcBlend", 1f); m.SetFloat("_DstBlend", 10f); m.SetFloat("_AlphaSrcBlend", 1f); m.SetFloat("_AlphaDstBlend", 10f);
            m.SetFloat("_ZTestDepthEqualForOpaque", 4f); m.SetFloat("_EnableFogOnTransparent", 1f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.EnableKeyword("_BLENDMODE_ALPHA"); m.EnableKeyword("_ENABLE_FOG_ON_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = 3000;
            Validate(m);
            return;
        }
        m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = 3000;
    }

    /// <summary>HDRP: re-derive keywords and render states after property changes (HDMaterial.ValidateMaterial; ignored elsewhere).</summary>
    public static void Validate(Material m)
    {
        if (m == null || !IsHdrpMaterial(m)) return;
        try
        {
            var t = System.Type.GetType("UnityEngine.Rendering.HighDefinition.HDMaterial, Unity.RenderPipelines.HighDefinition.Runtime");
            var v = t != null ? t.GetMethod("ValidateMaterial", new[] { typeof(Material) }) : null;
            if (v != null) v.Invoke(null, new object[] { m });
        }
        catch (System.Exception) { }
    }

    /// <summary>Convert an old URP-scale intensity of a lamp (point / spot) to HDRP lumen. Directional lights keep their value (lux, see ExposureEV).</summary>
    public static void LegacyLight(Light l, float legacyIntensity)
    {
        if (l == null) return;
        l.intensity = l.type == LightType.Directional || !IsHdrp ? legacyIntensity : legacyIntensity * LumenPerLegacy;
    }
}
