using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// Pipeline values the game drives at runtime (they lived on the URP asset before the HDRP switch). GraphicsEnhancer and
/// AtmosphereLook write them; HdrpLook pushes them into HDRP every frame. Tests read them.
/// </summary>
public class HdrpSettings
{
    public float renderScale = 1f, shadowDistance = 90f;
    /// <summary>Kept for the Settings menu (MSAA choice); HDRP uses post AA (SMAA / TAA) instead, see GraphicsEnhancer.ConfigureCamera.</summary>
    public int msaaSampleCount = 1;
    public int mainLightShadowmapResolution = 2048, shadowCascadeCount = 4;
    /// <summary>High / Ultra grade in HDR (URP's colour grading mode); HDRP always grades in HDR, so this only gates the extra grade.</summary>
    public bool hdrGrading = true;

    public static HdrpSettings Current = new HdrpSettings();
}

/// <summary>
/// HDRP environment and per-preset rendering features (one global volume, priority 10, weight 1 on every preset):
/// - physically based sky (VisualEnvironment), volumetric clouds (Medium+), height fog from RenderSettings.fog / density /
///   colour (the weather and events keep using RenderSettings; volumetric fog on High+);
/// - fixed exposure Rp.ExposureEV (a 1-lux sun reads like the old URP sun 1; identical on every preset, so Ultra is never
///   darker than High), ACES tonemapping;
/// - SSAO (Medium+, Settings > Ambient occlusion), contact shadows and SSR (High+), shadow distance / cascades from HdrpSettings.
/// The quality level (and its HDRP asset Assets/Settings/HDRP/MazraaHDRP_*) follows GraphicsEnhancer.Preset.
/// Brightness and the look grade stay in GraphicsEnhancer's volumes (higher priority, ColorAdjustments.postExposure).
/// </summary>
public class HdrpLook : MonoBehaviour
{
    public static HdrpLook Instance { get; private set; }
    public const float FogMaxHeight = 250f, MinFogDistance = 25f, MaxFogDistance = 6000f;

    public Volume Environment { get; private set; }
    public float AppliedExposureEV => exposure != null ? exposure.fixedExposure.value : 0f;
    public bool CloudsOn => clouds != null && clouds.enable.value;
    public bool VolumetricFogOn => fog != null && fog.enableVolumetricFog.value;
    public bool SsaoOn => ssao != null && ssao.active;
    public bool ContactShadowsOn => contact != null && contact.enable.value;
    public bool SsrOn => ssr != null && ssr.enabled.value;
    public float FogDistance => fog != null ? fog.meanFreePath.value : 0f;
    public bool FogOn => fog != null && fog.enabled.value;

    private Exposure exposure;
    private Fog fog;
    private VolumetricClouds clouds;
    private ScreenSpaceAmbientOcclusion ssao;
    private ContactShadows contact;
    private ScreenSpaceReflection ssr;
    private HDShadowSettings shadows;
    private int lastPreset = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { Instance = null; HdrpSettings.Current = new HdrpSettings(); }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot() => Ensure();

    /// <summary>Create the environment once (survives scene loads; the menu gets the same sky and exposure).</summary>
    public static HdrpLook Ensure()
    {
        if (Instance != null) return Instance;
        var go = new GameObject("Mazraa HDRP Environment");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<HdrpLook>();
        Instance.Build();
        return Instance;
    }

    private void Awake() { if (Instance == null) Instance = this; }
    private void OnDestroy() { if (Instance == this) Instance = null; }

    private void Build()
    {
        if (Environment != null) return;
        Environment = gameObject.AddComponent<Volume>();
        Environment.isGlobal = true;
        Environment.priority = 10f;
        Environment.weight = 1f;
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.name = "Mazraa HDRP environment (runtime)";

        var env = profile.Add<VisualEnvironment>(true);
        env.skyType.Override((int)SkyType.PhysicallyBased);
        env.cloudType.Override(0);   // volumetric clouds are their own override, not a cloud layer
        profile.Add<PhysicallyBasedSky>(true);

        clouds = profile.Add<VolumetricClouds>(true);
        clouds.enable.Override(true);

        fog = profile.Add<Fog>(true);
        fog.enabled.Override(true);
        fog.meanFreePath.Override(800f);
        fog.baseHeight.Override(0f);
        fog.maximumHeight.Override(FogMaxHeight);
        fog.colorMode.Override(FogColorMode.ConstantColor);
        fog.color.Override(new Color(0.78f, 0.72f, 0.62f));
        fog.enableVolumetricFog.Override(false);

        exposure = profile.Add<Exposure>(true);
        exposure.mode.Override(ExposureMode.Fixed);
        exposure.fixedExposure.Override(Rp.ExposureEV);

        var tone = profile.Add<Tonemapping>(true);
        tone.mode.Override(TonemappingMode.ACES);

        ssao = profile.Add<ScreenSpaceAmbientOcclusion>(true);
        ssao.intensity.Override(0.6f);
        contact = profile.Add<ContactShadows>(true);
        contact.enable.Override(true);
        contact.length.Override(0.25f);
        ssr = profile.Add<ScreenSpaceReflection>(true);
        ssr.enabled.Override(true);
        shadows = profile.Add<HDShadowSettings>(true);
        shadows.maxShadowDistance.Override(90f);
        shadows.cascadeShadowSplitCount.Override(4);

        Environment.sharedProfile = profile;
        ApplyPreset(true);
    }

    /// <summary>Feature switches for the current preset (cheap; called when the preset or Settings change).</summary>
    public void ApplyPreset(bool force = false)
    {
        int p = GraphicsEnhancer.Preset;
        if (p == lastPreset && !force && !settingsDirty) return;
        lastPreset = p; settingsDirty = false;
        if (clouds != null) clouds.enable.Override(p >= GraphicsEnhancer.PresetMedium);
        if (fog != null) fog.enableVolumetricFog.Override(p >= GraphicsEnhancer.PresetHigh);
        if (ssao != null) ssao.active = p >= GraphicsEnhancer.PresetMedium && GameSettings.AmbientOcclusion;
        if (contact != null) contact.enable.Override(p >= GraphicsEnhancer.PresetHigh);
        if (ssr != null) ssr.enabled.Override(p >= GraphicsEnhancer.PresetHigh);
        if (exposure != null) exposure.fixedExposure.Override(Rp.ExposureEV);   // same on every preset
    }

    private bool settingsDirty;
    /// <summary>Settings changed (ambient occlusion, shadows): re-apply on the next frame.</summary>
    public void MarkDirty() => settingsDirty = true;

    private void LateUpdate()
    {
        if (settingsDirty || GraphicsEnhancer.Preset != lastPreset) ApplyPreset();
        var s = HdrpSettings.Current;
        if (shadows != null)
        {
            shadows.maxShadowDistance.Override(Mathf.Max(1f, s.shadowDistance));
            shadows.cascadeShadowSplitCount.Override(Mathf.Clamp(s.shadowCascadeCount, 1, 4));
        }
        SyncFog();
    }

    /// <summary>RenderSettings fog (weather, events, regions) -> HDRP height fog. Exponential density d -> mean free path 1/d.</summary>
    public void SyncFog()
    {
        if (fog == null) return;
        bool on = RenderSettings.fog;
        fog.enabled.Override(on);
        if (!on) return;
        float d = Mathf.Max(1e-5f, RenderSettings.fogDensity);
        fog.meanFreePath.Override(Mathf.Clamp(1f / d, MinFogDistance, MaxFogDistance));
        fog.color.Override(RenderSettings.fogColor);
    }

    /// <summary>Directional shadow map resolution for the sun (Settings > Shadow quality); ignored when the HDRP API differs.</summary>
    public static void SetSunShadowResolution(Light sun, int resolution)
    {
        if (sun == null) return;
        var data = sun.GetComponent<HDAdditionalLightData>();
        if (data == null) return;
        try
        {
            var t = data.GetType();
            var ov = t.GetMethod("SetShadowResolutionOverride", new[] { typeof(bool) });
            var set = t.GetMethod("SetShadowResolution", new[] { typeof(int) });
            if (ov != null) ov.Invoke(data, new object[] { true });
            if (set != null) set.Invoke(data, new object[] { resolution });
        }
        catch (System.Exception) { }
    }
}
