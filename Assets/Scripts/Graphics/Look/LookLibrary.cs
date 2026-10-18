using System.Collections.Generic;
using UnityEngine;

/// <summary>Every PBR material of the Mazraa look (Resources/Look/Materials/&lt;name&gt;.mat).</summary>
public enum LookMat
{
    // ground (terrain layers, sand mesh, track, dunes, field soil)
    Sand, Gravel, Rock, Soil, Grass, Tilled,
    // buildings and props
    MudBrick, Plaster, Concrete, Corrugated, Metal, Wood,
    // cloth and fur
    Linen, Wool, Hessian,
    // bark
    PalmBark, Bark, EucBark,
    // foliage (Mazraa/Foliage shader, alpha cut, wind)
    LeavesAcacia, LeavesEucalyptus, LeavesSidr, PalmFrond, GrassGreen, GrassDry,
    // transparent
    Glass,
    // roads (Poly Haven asphalt_02; appended so saved enum values keep their meaning)
    Asphalt,
}

/// <summary>
/// The material library of the graphics makeover. Materials are real .mat assets in Resources/Look/Materials (CC0 Poly
/// Haven textures, see Assets/ThirdParty/CREDITS.md): shipping them as assets keeps their shader variants (normal map,
/// occlusion, metallic map, alpha test, instancing) in the build. Runtime code only clones them for tints, so the
/// variants always exist. When a material is missing (stripped install, headless tests) a plain URP/Lit colour from
/// Build.Mat is used instead, so nothing ever turns pink.
/// Textures are 1K (masks 512), mipmapped, block-compressed (import settings in their .meta); TextureBytes() estimates
/// the GPU memory of everything loaded.
/// </summary>
public static class LookLibrary
{
    public const string MaterialFolder = "Look/Materials/";
    public const string TextureFolder = "Look/Textures/";
    /// <summary>Upper bound for the look's textures in GPU memory (bytes, compressed estimate).</summary>
    public const long TextureBudgetBytes = 96L * 1024 * 1024;
    /// <summary>Budget on Ultra, where the main ground / wall / palm sets use their 2K albedo + normal (Textures2K).</summary>
    public const long TextureBudgetBytesUltra = 192L * 1024 * 1024;
    public const int MaxTextureSize = 2048;
    public const string HiResFolder = "Look/Textures2K/";
    public const string ParallaxMaterial = "Look/Materials/GroundParallax";
    /// <summary>Sets with a 2K albedo + normal in Resources/Look/Textures2K (the surfaces seen up close most).</summary>
    public static readonly LookMat[] HiResSets = { LookMat.Sand, LookMat.Gravel, LookMat.Rock, LookMat.Soil, LookMat.Grass, LookMat.Tilled, LookMat.MudBrick, LookMat.Plaster, LookMat.PalmBark };
    /// <summary>True while the 2K textures are applied (Ultra, MaxLook).</summary>
    public static bool HighRes { get; private set; }
    private static readonly Dictionary<LookMat, Texture[]> lowTex = new Dictionary<LookMat, Texture[]>();
    private static Material parallax;
    private static bool parallaxTried;

    private static readonly Dictionary<LookMat, Material> cache = new Dictionary<LookMat, Material>();
    private static readonly Dictionary<string, Material> tinted = new Dictionary<string, Material>();
    private static readonly Dictionary<Material, Color> baseColors = new Dictionary<Material, Color>();
    private static readonly Dictionary<Material, float> baseSmooth = new Dictionary<Material, float>();
    private static readonly HashSet<LookMat> missing = new HashSet<LookMat>();

    /// <summary>Loaded from Resources (true) or the plain-colour fallback (false), per material (tests / diagnostics).</summary>
    public static int LoadedCount { get; private set; }
    public static int FallbackCount { get; private set; }
    /// <summary>0 dry .. 1 soaked (rain): ground materials get darker and glossier.</summary>
    public static float Wetness { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        cache.Clear(); tinted.Clear(); baseColors.Clear(); baseSmooth.Clear(); missing.Clear(); lowTex.Clear();
        LoadedCount = FallbackCount = 0; Wetness = 0f; HighRes = false; parallax = null; parallaxTried = false;
    }

    public static bool IsFoliage(LookMat m) => m >= LookMat.LeavesAcacia && m <= LookMat.GrassDry;
    public static bool IsGround(LookMat m) => m <= LookMat.Tilled;

    /// <summary>Real-world size of one texture repeat in metres (world-space UVs).</summary>
    public static float TileSize(LookMat m)
    {
        switch (m)
        {
            case LookMat.Sand: return 2.6f;
            case LookMat.Gravel: return 2.5f;
            case LookMat.Rock: return 4f;
            case LookMat.Soil: return 1.6f;
            case LookMat.Grass: return 2.5f;
            case LookMat.Tilled: return 5f;
            case LookMat.MudBrick: return 1.5f;
            case LookMat.Plaster: return 2f;
            case LookMat.Concrete: return 2.7f;
            case LookMat.Asphalt: return 3.2f;
            case LookMat.Corrugated: return 1.1f;
            case LookMat.Metal: return 2f;
            case LookMat.Wood: return 1.8f;
            case LookMat.Linen: case LookMat.Hessian: return 0.27f;
            case LookMat.Wool: return 0.33f;
            case LookMat.PalmBark: return 1.86f;
            case LookMat.Bark: return 1f;
            case LookMat.EucBark: return 1.3f;
            default: return 1f;
        }
    }

    /// <summary>Average colour of each texture set (fallback colour and minimap-ish tint).</summary>
    public static Color AverageColor(LookMat m)
    {
        switch (m)
        {
            case LookMat.Sand: return new Color(0.78f, 0.62f, 0.44f);
            case LookMat.Gravel: return new Color(0.7f, 0.55f, 0.4f);
            case LookMat.Rock: return new Color(0.55f, 0.47f, 0.4f);
            case LookMat.Soil: return new Color(0.3f, 0.24f, 0.18f);
            case LookMat.Grass: return new Color(0.36f, 0.42f, 0.22f);
            case LookMat.Tilled: return new Color(0.36f, 0.28f, 0.2f);
            case LookMat.MudBrick: return new Color(0.66f, 0.52f, 0.38f);
            case LookMat.Plaster: return new Color(0.88f, 0.86f, 0.82f);
            case LookMat.Concrete: return new Color(0.62f, 0.6f, 0.57f);
            case LookMat.Asphalt: return new Color(0.3f, 0.3f, 0.31f);
            case LookMat.Corrugated: return new Color(0.62f, 0.64f, 0.66f);
            case LookMat.Metal: return new Color(0.5f, 0.45f, 0.4f);
            case LookMat.Wood: return new Color(0.4f, 0.3f, 0.22f);
            case LookMat.Linen: return new Color(0.86f, 0.86f, 0.86f);
            case LookMat.Wool: return new Color(0.88f, 0.84f, 0.76f);
            case LookMat.Hessian: return new Color(0.7f, 0.6f, 0.44f);
            case LookMat.PalmBark: return new Color(0.42f, 0.34f, 0.26f);
            case LookMat.Bark: return new Color(0.36f, 0.29f, 0.23f);
            case LookMat.EucBark: return new Color(0.72f, 0.69f, 0.62f);
            case LookMat.LeavesAcacia: return new Color(0.42f, 0.5f, 0.28f);
            case LookMat.LeavesEucalyptus: return new Color(0.36f, 0.45f, 0.36f);
            case LookMat.LeavesSidr: return new Color(0.3f, 0.42f, 0.22f);
            case LookMat.PalmFrond: return new Color(0.42f, 0.5f, 0.32f);
            case LookMat.GrassGreen: return new Color(0.38f, 0.48f, 0.2f);
            case LookMat.GrassDry: return new Color(0.68f, 0.56f, 0.32f);
            case LookMat.Glass: return new Color(0.86f, 0.93f, 0.95f, 0.28f);
            default: return Color.grey;
        }
    }

    /// <summary>The shared material (never edit it directly: use Tinted for variations).</summary>
    public static Material Get(LookMat m)
    {
        Material mat;
        if (cache.TryGetValue(m, out mat) && mat != null) return mat;
        mat = Resources.Load<Material>(MaterialFolder + m);
        if (mat != null && mat.shader != null && mat.shader.isSupported)
        {
            if (IsFoliage(m)) mat = PrepareFoliage(m, mat);
            if (System.Array.IndexOf(HiResSets, m) >= 0)
            {
                if (Application.isEditor) { var src = mat; mat = new Material(src); mat.name = src.name; }   // Play mode never edits the .mat asset
                lowTex[m] = new[] { mat.GetTexture("_BaseMap"), mat.GetTexture("_BumpMap") };
            }
            cache[m] = mat;
            ApplyRes(m, mat);
            LoadedCount++;
        }
        else
        {
            mat = Fallback(m);
            missing.Add(m);
            FallbackCount++;
        }
        cache[m] = mat;
        return mat;
    }

    /// <summary>True when the textured asset was found (false = plain colour fallback).</summary>
    public static bool IsTextured(LookMat m) { Get(m); return !missing.Contains(m); }

    /// <summary>A cached copy with another base colour (multiplies the texture) and optionally another smoothness.</summary>
    public static Material Tinted(LookMat m, Color tint, float smoothness = -1f) => Tinted(m, tint, smoothness, Vector2.zero);

    /// <summary>Same, with a texture tiling for meshes with 0..1 UVs (plot soil cubes); zero = keep the material's.</summary>
    public static Material Tinted(LookMat m, Color tint, float smoothness, Vector2 tiling)
    {
        string key = (int)m + ":" + ColorUtility.ToHtmlStringRGBA(tint) + ":" + Mathf.Round(smoothness * 100f) + ":" + Mathf.Round(tiling.x * 10f) + "x" + Mathf.Round(tiling.y * 10f);
        Material mat;
        if (tinted.TryGetValue(key, out mat) && mat != null) return mat;
        var src = Get(m);
        mat = new Material(src);
        mat.name = src.name + " " + ColorUtility.ToHtmlStringRGB(tint);
        if (missing.Contains(m))
        {
            var c = AverageColor(m) * tint; c.a = tint.a;
            mat.color = c;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
        }
        else
        {
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
            mat.color = tint;
        }
        if (smoothness >= 0f && mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
        if (tiling.x > 0f && tiling.y > 0f && !missing.Contains(m))
            foreach (var t in new[] { "_BaseMap", "_BumpMap", "_OcclusionMap", "_MetallicGlossMap" }) if (mat.HasProperty(t)) mat.SetTextureScale(t, tiling);
        if (!missing.Contains(m)) ApplyRes(m, mat);
        tinted[key] = mat;
        if (IsGround(m)) Remember(mat);
        return mat;
    }

    private static Material Fallback(LookMat m)
    {
        if (IsFoliage(m))
        {
            // A missing foliage asset: plain colour, but still alpha-free and instanced (never pink).
            var f = new Material(Build.Mat(AverageColor(m), 0.15f));
            f.name = "Look fallback " + m;
            f.enableInstancing = true;
            return f;
        }
        if (m == LookMat.Glass) return Build.Mat(AverageColor(m), 0.92f);
        float smooth = m == LookMat.Metal || m == LookMat.Corrugated ? 0.45f : m == LookMat.Plaster || m == LookMat.Concrete ? 0.2f : 0.08f;
        return Build.Mat(AverageColor(m), smooth, m == LookMat.Corrugated ? 0.6f : 0f);
    }

    /// <summary>Foliage uses Mazraa/Foliage; if that shader can't run here, swap to URP Simple Lit cut-out (no wind).</summary>
    private static Material PrepareFoliage(LookMat m, Material mat)
    {
        mat.enableInstancing = true;   // instanced grass (Graphics.RenderMeshInstanced) and many trees
        if (mat.shader.name == "Mazraa/Foliage") return mat;
        var fallback = Resources.Load<Material>(MaterialFolder + "FoliageFallback");
        if (fallback == null) return mat;
        var f = new Material(fallback);
        f.name = mat.name + " (simple)";
        f.SetTexture("_BaseMap", mat.GetTexture("_BaseMap"));
        if (mat.HasProperty("_BaseColor")) f.SetColor("_BaseColor", mat.GetColor("_BaseColor"));
        f.enableInstancing = true;
        return f;
    }

    // ------------------------------------------------------------------ wetness (rain)
    private static void Remember(Material mat)
    {
        if (mat == null || baseColors.ContainsKey(mat)) return;
        baseColors[mat] = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : mat.color;
        baseSmooth[mat] = mat.HasProperty("_Smoothness") ? mat.GetFloat("_Smoothness") : 0.1f;
    }

    /// <summary>Darken and gloss every ground material (wet sand / soil after rain); 0 restores them.</summary>
    public static void SetWetness(float wet01)
    {
        wet01 = Mathf.Clamp01(wet01);
        if (Mathf.Abs(wet01 - Wetness) < 0.01f && wet01 > 0f && wet01 < 1f) return;
        Wetness = wet01;
        for (var m = LookMat.Sand; m <= LookMat.Tilled; m++) if (cache.ContainsKey(m)) Remember(cache[m]);
        foreach (var kv in baseColors)
        {
            var mat = kv.Key;
            if (mat == null) continue;
            Color c = kv.Value;
            Color wetC = new Color(c.r * 0.62f, c.g * 0.6f, c.b * 0.58f, c.a);
            Color now = Color.Lerp(c, wetC, wet01);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", now);
            mat.color = now;
            float s;
            if (baseSmooth.TryGetValue(mat, out s) && mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", Mathf.Lerp(s, Mathf.Max(s, 0.62f), wet01));
        }
    }

    // ------------------------------------------------------------------ textures
    public static Texture2D Texture(string name) => Resources.Load<Texture2D>(TextureFolder + name);

    /// <summary>The 2K version of a texture ("sand_d", "rock_n"...), or null when the set has none.</summary>
    public static Texture2D HiResTexture(string name) => Resources.Load<Texture2D>(HiResFolder + name);

    /// <summary>Swap the 2K albedo + normal in (Ultra) or back to the 1K ones, on every loaded and tinted material.</summary>
    public static void SetHighRes(bool on)
    {
        if (on == HighRes) return;
        HighRes = on;
        foreach (var kv in cache) ApplyRes(kv.Key, kv.Value);
        foreach (var kv in tinted)
        {
            int id;
            if (int.TryParse(kv.Key.Split(':')[0], out id) && !missing.Contains((LookMat)id)) ApplyRes((LookMat)id, kv.Value);
        }
        if (parallax != null) ApplyRes(LookMat.Sand, parallax);
    }

    private static void ApplyRes(LookMat m, Material mat)
    {
        Texture[] low;
        if (mat == null || !lowTex.TryGetValue(m, out low)) return;
        string f = m.ToString().ToLowerInvariant();
        Texture d = HighRes ? HiResTexture(f + "_d") : null, n = HighRes ? HiResTexture(f + "_n") : null;
        if (d == null || n == null) { d = low[0]; n = low[1]; }   // 1K (or 2K files missing: stay 1K)
        if (d != null) { mat.SetTexture("_BaseMap", d); if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", d); }
        if (n != null) mat.SetTexture("_BumpMap", n);
    }

    /// <summary>
    /// Sand with parallax occlusion (URP/Lit _PARALLAXMAP, height from the mask) for the open ground and dunes on Ultra;
    /// null when the asset or the sand textures are missing (the plain sand material is used then).
    /// </summary>
    public static Material GroundParallax()
    {
        if (parallaxTried) return parallax;
        parallaxTried = true;
        if (!IsTextured(LookMat.Sand)) return null;
        var mat = Resources.Load<Material>(ParallaxMaterial);
        if (mat == null || mat.shader == null || !mat.shader.isSupported) return null;
        if (Application.isEditor) { var src = mat; mat = new Material(src); mat.name = src.name; }
        parallax = mat;
        ApplyRes(LookMat.Sand, parallax);
        Remember(parallax);   // wet after rain like the rest of the ground
        return parallax;
    }

    /// <summary>Estimated GPU bytes of every texture used by the loaded look materials (BC-compressed, with mips).</summary>
    public static long TextureBytes()
    {
        var seen = new HashSet<Texture>();
        long total = 0;
        foreach (var mat in cache.Values)
        {
            if (mat == null) continue;
            foreach (var prop in new[] { "_BaseMap", "_BumpMap", "_OcclusionMap", "_MetallicGlossMap" })
            {
                if (!mat.HasProperty(prop)) continue;
                var t = mat.GetTexture(prop);
                if (t == null || !seen.Add(t)) continue;
                // opaque albedo = BC1 (half a byte per texel); alpha albedo, normals and masks = 1 byte per texel
                bool halfByte = prop == "_BaseMap" && !IsFoliageTexture(mat);
                total += EstimateBytes(t.width, t.height, !halfByte);
            }
        }
        return total;
    }

    private static bool IsFoliageTexture(Material mat) => mat.shader != null && (mat.shader.name == "Mazraa/Foliage" || mat.name.Contains("Leaves") || mat.name.Contains("Frond") || mat.name.Contains("Grass"));

    /// <summary>Compressed size with a full mip chain: BC1 = 0.5 byte/texel, BC3/BC5/BC7 = 1 byte/texel.</summary>
    public static long EstimateBytes(int w, int h, bool fullByte)
    {
        w = Mathf.Min(w, MaxTextureSize); h = Mathf.Min(h, MaxTextureSize);
        double texels = w * (double)h * 4.0 / 3.0;
        return (long)(texels * (fullByte ? 1.0 : 0.5));
    }

    // ------------------------------------------------------------------ UVs for world-mapped materials
    /// <summary>Flat ground: uv = world xz / tile (meshes whose vertices are already in world space).</summary>
    public static void PlanarUV(Mesh mesh, Vector3 worldOffset, float tile)
    {
        if (mesh == null) return;
        var v = mesh.vertices;
        var uv = new Vector2[v.Length];
        float k = 1f / Mathf.Max(0.01f, tile);
        for (int i = 0; i < v.Length; i++) uv[i] = new Vector2((v[i].x + worldOffset.x) * k, (v[i].z + worldOffset.z) * k);
        mesh.uv = uv;
        if (v.Length > 0) mesh.RecalculateTangents();
    }

    /// <summary>
    /// Box projection in world space: each vertex takes the two world axes across its normal's dominant axis, so
    /// a 10 m wall and a 0.5 m beam get the same texel density (merged primitive parts, buildings).
    /// </summary>
    public static void BoxUV(Mesh mesh, Matrix4x4 localToWorld, float tile)
    {
        if (mesh == null) return;
        var v = mesh.vertices;
        var n = mesh.normals;
        if (v.Length == 0 || n == null || n.Length != v.Length) return;
        var uv = new Vector2[v.Length];
        float k = 1f / Mathf.Max(0.01f, tile);
        for (int i = 0; i < v.Length; i++)
        {
            Vector3 p = localToWorld.MultiplyPoint3x4(v[i]);
            Vector3 d = localToWorld.MultiplyVector(n[i]);
            float ax = Mathf.Abs(d.x), ay = Mathf.Abs(d.y), az = Mathf.Abs(d.z);
            if (ay >= ax && ay >= az) uv[i] = new Vector2(p.x * k, p.z * k);
            else if (ax >= az) uv[i] = new Vector2(p.z * k, p.y * k);
            else uv[i] = new Vector2(p.x * k, p.y * k);
        }
        mesh.uv = uv;
        mesh.RecalculateTangents();
    }

    /// <summary>The look material a plain Build.Mat colour stands for (merged dressing parts), or null.</summary>
    public static LookMat? Classify(Material m)
    {
        if (m == null) return null;
        foreach (var kv in cache) if (kv.Value == m) return kv.Key;
        foreach (var kv in tinted)
            if (kv.Value == m) { int id; if (int.TryParse(kv.Key.Split(':')[0], out id)) return (LookMat)id; }
        return null;
    }

    /// <summary>All materials made so far (tests: no pink / missing shaders).</summary>
    public static IEnumerable<Material> AllLoaded()
    {
        foreach (var m in cache.Values) if (m != null) yield return m;
        foreach (var m in tinted.Values) if (m != null) yield return m;
    }
}
