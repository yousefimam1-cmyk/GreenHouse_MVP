using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Runtime graphics upgrade for URP: a global post-processing volume (ACES tonemapping, bloom, warm grading,
/// vignette), SMAA, MSAA, longer soft shadows. Three quality presets (Low / Medium / High): shadow distance,
/// render scale, MSAA, post-processing on/off and how much desert dressing is placed. G cycles them; a settings
/// menu can call GraphicsEnhancer.SetPreset(0..2). Saved. Default: High on PC, Low on WebGL and phones.
/// </summary>
public class GraphicsEnhancer : MonoBehaviour
{
    public const int PresetLow = 0, PresetMedium = 1, PresetHigh = 2;
    public static readonly string[] PresetNames = { "Graphics: Low", "Graphics: Medium", "Graphics: High" };

    private struct PresetData { public float shadow, scale, volume, dressing; public int msaa; public bool post, smaa; }
    private static readonly PresetData[] presets =
    {
        new PresetData { shadow = 35f, scale = 0.75f, msaa = 1, post = false, smaa = false, volume = 0f,   dressing = 0.35f },
        new PresetData { shadow = 60f, scale = 0.9f,  msaa = 2, post = true,  smaa = false, volume = 0.75f, dressing = 0.7f },
        new PresetData { shadow = 90f, scale = 1f,    msaa = 4, post = true,  smaa = true,  volume = 1f,   dressing = 1f },
    };

    public static GraphicsEnhancer Instance { get; private set; }
    /// <summary>Current preset: 0 Low, 1 Medium, 2 High.</summary>
    public static int Preset { get; private set; } = 2;
    /// <summary>Kept for older callers: true on the High preset.</summary>
    public static bool High => Preset >= 2;
    /// <summary>Post-processing enabled by the current preset.</summary>
    public static bool PostEnabled => presets[Preset].post;
    /// <summary>0..1 multiplier for decorative props (DesertDressing reads it when it builds).</summary>
    public static float DressingDensity => presets[Preset].dressing;
    /// <summary>Raised after the preset changes (other systems can rebuild or rescale).</summary>
    public static event System.Action<int> PresetChanged;

    private Volume volume;
    // Original URP asset values, restored on exit so Play mode doesn't permanently change project settings.
    private UniversalRenderPipelineAsset urpAsset;
    private int origMsaa; private float origScale, origShadow;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { PresetChanged = null; Instance = null; Preset = 2; }

    /// <summary>Default preset for this device when nothing is saved.</summary>
    public static int DefaultPreset()
    {
        if (Application.isMobilePlatform) return PresetLow;
        if (Application.platform == RuntimePlatform.WebGLPlayer) return PresetLow;
        return PresetHigh;
    }

    private void Awake() => Instance = this;

    private void Start()
    {
        Instance = this;
        urpAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (urpAsset != null) { origMsaa = urpAsset.msaaSampleCount; origScale = urpAsset.renderScale; origShadow = urpAsset.shadowDistance; }
        // The Settings menu (GameSettings, PlayerPrefs) is the source of truth; the old save keys are only a fallback.
        int saved = PlayerPrefs.HasKey("mazraa.settings.graphics") ? PlayerPrefs.GetInt("mazraa.settings.graphics") : SaveData.GetInt("mazraa.gfxPreset", -1);
        if (saved < 0) saved = SaveData.GetInt("mazraa.gfxHigh", -1) == 0 ? PresetLow : (SaveData.GetInt("mazraa.gfxHigh", -1) == 1 ? PresetHigh : DefaultPreset());
        Preset = Mathf.Clamp(saved, 0, 2);
        BuildVolume();
        Apply();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (urpAsset == null) return;
        urpAsset.msaaSampleCount = origMsaa;
        urpAsset.renderScale = origScale;
        urpAsset.shadowDistance = origShadow;
    }

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && !NetSession.Typing && kb.gKey.wasPressedThisFrame)
        {
            SetPreset((Preset + 1) % 3);
            if (UIManager.Instance != null) UIManager.Instance.ShowAlertMessage(Loc.T(PresetNames[Preset]));
        }
    }

    /// <summary>Switch quality preset (0 Low, 1 Medium, 2 High). Saved; safe to call before the game has started.</summary>
    public static void SetPreset(int preset)
    {
        Preset = Mathf.Clamp(preset, 0, 2);
        SaveData.SetInt("mazraa.gfxPreset", Preset);
        SaveData.SetInt("mazraa.gfxHigh", Preset >= 2 ? 1 : 0);
        PlayerPrefs.SetInt("mazraa.settings.graphics", Preset);   // keep the Settings menu in step (G key)
        SaveData.Save();
        if (Instance != null) Instance.Apply();
        PresetChanged?.Invoke(Preset);
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
        var bloom = profile.Add<Bloom>(true);
        bloom.intensity.Override(0.55f);
        bloom.threshold.Override(1.05f);
        bloom.scatter.Override(0.65f);
        var color = profile.Add<ColorAdjustments>(true);
        color.postExposure.Override(0.15f);
        color.contrast.Override(14f);
        color.saturation.Override(12f);
        var wb = profile.Add<WhiteBalance>(true);
        wb.temperature.Override(8f);
        var vig = profile.Add<Vignette>(true);
        vig.intensity.Override(0.22f);
        vig.smoothness.Override(0.45f);
        volume.sharedProfile = profile;
    }

    private void Apply()
    {
        var p = presets[Preset];
        if (volume != null) volume.weight = p.volume;
        if (urpAsset != null)
        {
            urpAsset.msaaSampleCount = p.msaa;
            urpAsset.renderScale = p.scale;
            urpAsset.shadowDistance = p.shadow;
        }
        foreach (var cam in FindObjectsByType<Camera>(FindObjectsSortMode.None)) ConfigureCamera(cam);
    }

    /// <summary>Turn on post-processing and anti-aliasing for a camera (also used by the truck camera).</summary>
    public static void ConfigureCamera(Camera cam)
    {
        if (cam == null || cam.targetTexture != null) return;   // minimap / render-texture cameras stay plain
        var data = cam.GetUniversalAdditionalCameraData();
        if (data == null) return;
        var p = presets[Preset];
        data.renderPostProcessing = p.post;
        data.antialiasing = p.smaa ? AntialiasingMode.SubpixelMorphologicalAntiAliasing : (p.post ? AntialiasingMode.FastApproximateAntialiasing : AntialiasingMode.None);
        data.antialiasingQuality = p.smaa ? AntialiasingQuality.High : AntialiasingQuality.Low;
        data.renderShadows = true;
    }
}
