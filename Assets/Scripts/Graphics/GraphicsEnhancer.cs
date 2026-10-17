using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Runtime graphics upgrade for URP: a global post-processing volume (ACES tonemapping, bloom, warm grading,
/// vignette), SMAA, MSAA, longer soft shadows. Three quality presets (Low / Medium / High): shadow distance,
/// render scale, MSAA, post-processing on/off and how much desert dressing is placed. G cycles them; a settings
/// menu can call GraphicsEnhancer.SetPreset(0..3). Saved. Default: High on PC, Low on WebGL and phones.
/// Ultra (graphics makeover): longest shadows, sharpest shadow map, more LOD detail, 256 px reflection probe and the
/// longest grass distance (see AtmosphereLook / FoliageGrass).
/// </summary>
public class GraphicsEnhancer : MonoBehaviour
{
    public const int PresetLow = 0, PresetMedium = 1, PresetHigh = 2, PresetUltra = 3;
    public const int PresetCount = 4;
    public static readonly string[] PresetNames = { "Graphics: Low", "Graphics: Medium", "Graphics: High", "Graphics: Ultra" };

    private struct PresetData { public float shadow, scale, volume, dressing; public int msaa; public bool post, smaa; }
    private static readonly PresetData[] presets =
    {
        new PresetData { shadow = 35f, scale = 0.75f, msaa = 1, post = false, smaa = false, volume = 0f,   dressing = 0.35f },
        new PresetData { shadow = 60f, scale = 0.9f,  msaa = 2, post = true,  smaa = false, volume = 0.75f, dressing = 0.7f },
        new PresetData { shadow = 90f, scale = 1f,    msaa = 4, post = true,  smaa = true,  volume = 1f,   dressing = 1f },
        new PresetData { shadow = 150f, scale = 1f,   msaa = 4, post = true,  smaa = true,  volume = 1f,   dressing = 1f },   // Ultra
    };
    /// <summary>LOD bias per preset when Settings > Level of detail is Auto (multiplies the project's own bias).</summary>
    private static readonly float[] presetLodBias = { 0.7f, 1f, 1.3f, 1.8f };

    public static GraphicsEnhancer Instance { get; private set; }
    /// <summary>Current preset: 0 Low, 1 Medium, 2 High, 3 Ultra.</summary>
    public static int Preset { get; private set; } = 2;
    /// <summary>Kept for older callers: true on the High preset.</summary>
    public static bool High => Preset >= 2;
    /// <summary>Post-processing enabled by the current preset.</summary>
    public static bool PostEnabled => presets[Preset].post;
    /// <summary>0..1 multiplier for decorative props (DesertDressing reads it when it builds); Settings > Graphics > Foliage overrides it.</summary>
    public static float DressingDensity => GameSettings.FoliageDensity >= 0 ? GameSettings.FoliageDensity / 100f : presets[Preset].dressing;
    /// <summary>Raised after the preset changes (other systems can rebuild or rescale).</summary>
    public static event System.Action<int> PresetChanged;

    private Volume volume, brightnessVolume;
    private Bloom bloom;
    private MotionBlur motionBlur;
    private DepthOfField depthOfField;
    private ColorAdjustments brightness;
    private ColorAdjustments grade;      // region colour grade (RegionLook)
    private WhiteBalance balance;
    // Original URP asset values, restored on exit so Play mode doesn't permanently change project settings.
    private UniversalRenderPipelineAsset urpAsset;
    private int origShadowRes = -1;
    private int origMsaa; private float origScale, origShadow;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { PresetChanged = null; Instance = null; Preset = 2; camOrig.Clear(); lastDensity = -1f; origLodBias = -1f; }

    /// <summary>Default preset for this device when nothing is saved.</summary>
    public static int DefaultPreset()
    {
        if (Application.isMobilePlatform) return PresetLow;
        if (Application.platform == RuntimePlatform.WebGLPlayer) return PresetLow;
        return PresetHigh;
    }

    private void Awake() => Instance = this;
    private void OnEnable() => GameEvents.RegionChanged += OnRegion;
    private void OnDisable() => GameEvents.RegionChanged -= OnRegion;
    private void OnRegion(string regionId) => ApplyRegionGrade();

    /// <summary>Colour grade of the current map: warm desert in Najd, greener and cooler on the Nile Delta (RegionLook).</summary>
    public void ApplyRegionGrade()
    {
        var look = Regions.Look;
        if (grade != null) grade.saturation.Override(look.saturation);
        if (balance != null) balance.temperature.Override(look.temperature);
    }

    /// <summary>Saturation of the post-processing grade now (tests).</summary>
    public float GradeSaturation => grade != null ? grade.saturation.value : 0f;

    private void Start()
    {
        Instance = this;
        urpAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (urpAsset != null) { origMsaa = urpAsset.msaaSampleCount; origScale = urpAsset.renderScale; origShadow = urpAsset.shadowDistance; }
        // The Settings menu (GameSettings, PlayerPrefs) is the source of truth; the old save keys are only a fallback.
        int saved = PlayerPrefs.HasKey("mazraa.settings.graphics") ? PlayerPrefs.GetInt("mazraa.settings.graphics") : SaveData.GetInt("mazraa.gfxPreset", -1);
        if (saved < 0) saved = SaveData.GetInt("mazraa.gfxHigh", -1) == 0 ? PresetLow : (SaveData.GetInt("mazraa.gfxHigh", -1) == 1 ? PresetHigh : DefaultPreset());
        Preset = Mathf.Clamp(saved, 0, PresetCount - 1);
        BuildVolume();
        BuildSettingsVolumes();
        Apply();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (urpAsset == null) return;
        urpAsset.msaaSampleCount = origMsaa;
        urpAsset.renderScale = origScale;
        urpAsset.shadowDistance = origShadow;
        if (origShadowRes > 0) urpAsset.mainLightShadowmapResolution = origShadowRes;
        RestoreAmbientOcclusion();
    }

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && !NetSession.Typing && kb.gKey.wasPressedThisFrame)
        {
            SetPreset((Preset + 1) % PresetCount);
            if (UIManager.Instance != null) UIManager.Instance.ShowAlertMessage(Loc.T(PresetNames[Preset]));
        }
    }

    /// <summary>Switch quality preset (0 Low, 1 Medium, 2 High, 3 Ultra). Saved; safe to call before the game has started.</summary>
    public static void SetPreset(int preset)
    {
        int next = Mathf.Clamp(preset, 0, PresetCount - 1);
        bool changed = next != Preset;
        Preset = next;
        SaveData.SetInt("mazraa.gfxPreset", Preset);
        SaveData.SetInt("mazraa.gfxHigh", Preset >= 2 ? 1 : 0);
        PlayerPrefs.SetInt("mazraa.settings.graphics", Preset);   // keep the Settings menu in step (G key)
        SaveData.Save();
        if (Instance != null) Instance.Apply();
        if (changed) PresetChanged?.Invoke(Preset);   // settings saves call this often: only rebuild on a real change
    }

    private void BuildVolume()
    {
        var go = new GameObject("Mazraa Post Processing");
        go.transform.SetParent(transform, false);
        volume = go.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 50f;
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.name = "Mazraa Look (runtime)";

        var tone = profile.Add<Tonemapping>(true);
        tone.mode.Override(TonemappingMode.ACES);
        bloom = profile.Add<Bloom>(true);
        bloom.intensity.Override(0.55f);
        bloom.threshold.Override(1.05f);
        bloom.scatter.Override(0.65f);
        var color = profile.Add<ColorAdjustments>(true);
        color.postExposure.Override(0.15f);
        color.contrast.Override(14f);
        color.saturation.Override(12f);
        var wb = profile.Add<WhiteBalance>(true);
        wb.temperature.Override(8f);
        grade = color; balance = wb;
        ApplyRegionGrade();
        var vig = profile.Add<Vignette>(true);
        vig.intensity.Override(0.22f);
        vig.smoothness.Override(0.45f);
        volume.sharedProfile = profile;
    }

    private void Apply()
    {
        var p = presets[Preset];
        if (volume != null) volume.weight = p.volume;
        if (urpAsset == null)
        {
            urpAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urpAsset != null) { origMsaa = urpAsset.msaaSampleCount; origScale = urpAsset.renderScale; origShadow = urpAsset.shadowDistance; }
        }
        if (urpAsset != null)
        {
            urpAsset.msaaSampleCount = EffectiveMsaa;
            urpAsset.renderScale = EffectiveRenderScale;
            urpAsset.shadowDistance = EffectiveShadowDistance;
        }
        ApplySettingsOverrides();
        foreach (var cam in FindObjectsByType<Camera>(FindObjectsSortMode.None)) { ConfigureCamera(cam); ConfigureView(cam); }
    }

    /// <summary>Turn on post-processing and anti-aliasing for a camera (also used by the truck camera).</summary>
    public static void ConfigureCamera(Camera cam)
    {
        if (cam == null || cam.targetTexture != null) return;   // minimap / render-texture cameras stay plain
        var data = cam.GetUniversalAdditionalCameraData();
        if (data == null) return;
        var p = presets[Preset];
        data.renderPostProcessing = p.post || Mathf.Abs(GameSettings.Brightness - 1f) > 0.01f;
        int aa = EffectiveAntialiasing;
        data.antialiasing = aa >= 2 ? AntialiasingMode.SubpixelMorphologicalAntiAliasing : aa == 1 ? AntialiasingMode.FastApproximateAntialiasing : AntialiasingMode.None;
        data.antialiasingQuality = aa >= 2 ? AntialiasingQuality.High : AntialiasingQuality.Low;
        data.renderShadows = GameSettings.ShadowQuality != 0;
    }

    // ================================================================== Settings > Graphics / Display overrides
    // Each option is -1 / 0 = Auto (the preset's value) or the player's own choice (GameSettingsOptions.cs).

    /// <summary>Shadow distance in metres after Settings (0 = shadows off).</summary>
    public static float EffectiveShadowDistance
    {
        get
        {
            int q = GameSettings.ShadowQuality;
            if (q == 0) return 0f;
            if (GameSettings.ShadowDistance > 0) return GameSettings.ShadowDistance;
            return presets[Preset].shadow * (q == 1 ? 0.6f : q == 2 ? 0.85f : 1f);
        }
    }
    public static float EffectiveRenderScale => GameSettings.RenderScale > 0 ? GameSettings.RenderScale / 100f : presets[Preset].scale;
    public static int EffectiveMsaa => GameSettings.AntiAliasing < 0 ? presets[Preset].msaa : GameSettings.AntiAliasing == 3 ? 4 : 1;
    /// <summary>0 none, 1 FXAA, 2 SMAA (3 = MSAA + SMAA).</summary>
    public static int EffectiveAntialiasing
    {
        get
        {
            if (GameSettings.AntiAliasing >= 0) return GameSettings.AntiAliasing;
            var p = presets[Preset];
            return p.smaa ? 2 : p.post ? 1 : 0;
        }
    }
    public static bool BloomOn => GameSettings.Bloom;
    public static bool MotionBlurOn => GameSettings.MotionBlur && !GameSettings.ReduceMotion;
    public static bool DepthOfFieldOn => GameSettings.DepthOfField && !GameSettings.ReduceMotion;

    private static float lastDensity = -1f, origLodBias = -1f;
    private static readonly System.Collections.Generic.Dictionary<Camera, Vector2> camOrig = new System.Collections.Generic.Dictionary<Camera, Vector2>();

    /// <summary>Re-apply after a Settings change (also works before the enhancer exists, e.g. QualitySettings).</summary>
    public static void ApplyOverrides()
    {
        if (Instance != null) Instance.Apply();
        else ApplyQuality();
        float d = DressingDensity;
        if (lastDensity >= 0f && Mathf.Abs(d - lastDensity) > 0.001f) PresetChanged?.Invoke(Preset);   // DesertDressing rebuilds
        lastDensity = d;
    }

    private static void ApplyQuality()
    {
        int q = GameSettings.ShadowQuality < 0 ? Preset + 1 : GameSettings.ShadowQuality;   // Low preset = low shadows
        QualitySettings.shadows = q == 0 ? UnityEngine.ShadowQuality.Disable : q == 1 ? UnityEngine.ShadowQuality.HardOnly : UnityEngine.ShadowQuality.All;
        QualitySettings.shadowResolution = q <= 1 ? UnityEngine.ShadowResolution.Low : q == 2 ? UnityEngine.ShadowResolution.Medium : q == 3 ? UnityEngine.ShadowResolution.High : UnityEngine.ShadowResolution.VeryHigh;
        QualitySettings.shadowDistance = EffectiveShadowDistance;
        // URP ignores QualitySettings shadow resolution: set the main light shadow map on the pipeline asset.
        var urp = Instance != null ? Instance.urpAsset : null;
        if (urp != null)
        {
            if (Instance.origShadowRes < 0) Instance.origShadowRes = urp.mainLightShadowmapResolution;
            urp.mainLightShadowmapResolution = q <= 1 ? 1024 : q == 2 ? 2048 : 4096;
        }
        if (origLodBias < 0f) origLodBias = QualitySettings.lodBias;
        QualitySettings.lodBias = GameSettings.LodBias > 0 ? GameSettings.LodBias / 100f : origLodBias * presetLodBias[Mathf.Clamp(Preset, 0, PresetCount - 1)];
    }

    private void BuildSettingsVolumes()
    {
        if (volume != null && volume.sharedProfile != null)
        {
            motionBlur = volume.sharedProfile.Add<MotionBlur>(true);
            motionBlur.intensity.Override(0.25f);
            depthOfField = volume.sharedProfile.Add<DepthOfField>(true);
            depthOfField.mode.Override(DepthOfFieldMode.Gaussian);
            depthOfField.gaussianStart.Override(80f);
            depthOfField.gaussianEnd.Override(300f);
        }
        // Brightness works on every preset (its own volume, always weight 1).
        var go = new GameObject("Mazraa Brightness");
        go.transform.SetParent(transform, false);
        brightnessVolume = go.AddComponent<Volume>();
        brightnessVolume.isGlobal = true;
        brightnessVolume.priority = 60f;
        brightnessVolume.weight = 1f;
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.name = "Mazraa Brightness (runtime)";
        brightness = profile.Add<ColorAdjustments>(true);
        brightnessVolume.sharedProfile = profile;
    }

    private void ApplySettingsOverrides()
    {
        ApplyQuality();
        if (bloom != null) bloom.active = BloomOn;
        if (motionBlur != null) motionBlur.active = MotionBlurOn;
        if (depthOfField != null) depthOfField.active = DepthOfFieldOn;
        if (brightness != null) brightness.postExposure.Override((float)System.Math.Log(Mathf.Max(0.1f, GameSettings.Brightness), 2.0));
        SetAmbientOcclusion(urpAsset, GameSettings.AmbientOcclusion);
    }

    /// <summary>Settings > Controls > Field of view and Graphics > View distance for perspective game cameras (minimap / menu cameras untouched).</summary>
    public static void ConfigureView(Camera cam)
    {
        if (cam == null || cam.targetTexture != null || cam.orthographic || cam.name.Contains("Menu")) return;
        Vector2 orig;
        if (!camOrig.TryGetValue(cam, out orig)) { orig = new Vector2(cam.fieldOfView, cam.farClipPlane); camOrig[cam] = orig; }
        cam.fieldOfView = GameSettings.CameraFov > 0 ? GameSettings.CameraFov : orig.x;
        cam.farClipPlane = GameSettings.ViewDistance > 0 ? GameSettings.ViewDistance : orig.y;
    }

    private static readonly System.Collections.Generic.Dictionary<object, bool> aoOriginal = new System.Collections.Generic.Dictionary<object, bool>();

    /// <summary>Put SSAO back as it was (in the editor SetActive changes the renderer asset itself).</summary>
    private static void RestoreAmbientOcclusion()
    {
        foreach (var kv in aoOriginal)
        {
            try { var set = kv.Key.GetType().GetMethod("SetActive"); if (set != null) set.Invoke(kv.Key, new object[] { kv.Value }); }
            catch (System.Exception) { }
        }
        aoOriginal.Clear();
    }

    /// <summary>URP's SSAO is a renderer feature: switch it through reflection (the renderer list isn't public), ignore failures.</summary>
    private static void SetAmbientOcclusion(UniversalRenderPipelineAsset asset, bool on)
    {
        if (asset == null) return;
        try
        {
            var f = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var list = f != null ? f.GetValue(asset) as System.Array : null;
            if (list == null) return;
            foreach (var data in list)
            {
                if (data == null) continue;
                var prop = data.GetType().GetProperty("rendererFeatures");
                var features = prop != null ? prop.GetValue(data, null) as System.Collections.IEnumerable : null;
                if (features == null) continue;
                foreach (var feat in features)
                    if (feat != null && feat.GetType().Name.Contains("AmbientOcclusion"))
                    {
                        var set = feat.GetType().GetMethod("SetActive");
                        if (set == null) continue;
                        if (!aoOriginal.ContainsKey(feat))
                        {
                            var act = feat.GetType().GetProperty("isActive");
                            aoOriginal[feat] = act == null || (bool)act.GetValue(feat, null);
                        }
                        set.Invoke(feat, new object[] { on });
                    }
            }
        }
        catch (System.Exception) { }
    }
}
