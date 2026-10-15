using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Runtime graphics upgrade for URP: a global post-processing volume (ACES tonemapping, bloom, warm grading,
/// vignette), SMAA, MSAA, longer soft shadows. G toggles High / Low quality (Low for weaker PCs and WebGL). Saved.
/// </summary>
public class GraphicsEnhancer : MonoBehaviour
{
    public static bool High { get; private set; } = true;
    private Volume volume;
    // Original URP asset values, restored on exit so Play mode doesn't permanently change project settings.
    private UniversalRenderPipelineAsset urpAsset;
    private int origMsaa; private float origScale, origShadow;

    private void Start()
    {
        urpAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (urpAsset != null) { origMsaa = urpAsset.msaaSampleCount; origScale = urpAsset.renderScale; origShadow = urpAsset.shadowDistance; }
        High = SaveData.GetInt("mazraa.gfxHigh", Application.platform == RuntimePlatform.WebGLPlayer ? 0 : 1) == 1;
        BuildVolume();
        Apply();
    }

    private void OnDestroy()
    {
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
            High = !High;
            SaveData.SetInt("mazraa.gfxHigh", High ? 1 : 0);
            SaveData.Save();
            Apply();
            if (UIManager.Instance != null) UIManager.Instance.ShowAlertMessage(Loc.T(High ? "Graphics: High" : "Graphics: Low"));
        }
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
        if (volume != null) volume.weight = High ? 1f : 0.6f;
        if (urpAsset is UniversalRenderPipelineAsset urp)
        {
            urp.msaaSampleCount = High ? 4 : 1;
            urp.renderScale = High ? 1f : 0.85f;
            urp.shadowDistance = High ? 90f : 40f;
        }
        foreach (var cam in FindObjectsByType<Camera>(FindObjectsSortMode.None)) ConfigureCamera(cam);
    }

    /// <summary>Turn on post-processing and anti-aliasing for a camera (also used by the truck camera).</summary>
    public static void ConfigureCamera(Camera cam)
    {
        if (cam == null) return;
        var data = cam.GetUniversalAdditionalCameraData();
        if (data == null) return;
        data.renderPostProcessing = true;
        data.antialiasing = High ? AntialiasingMode.SubpixelMorphologicalAntiAliasing : AntialiasingMode.FastApproximateAntialiasing;
        data.antialiasingQuality = High ? AntialiasingQuality.High : AntialiasingQuality.Low;
        data.renderShadows = true;
    }
}
