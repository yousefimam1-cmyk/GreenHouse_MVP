using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// The full settings set (Settings hub pages: Game, Display, Graphics, Sound, Controls, Accessibility), kept in
/// PlayerPrefs under "mazraa.settings.*" like the older values in GameSettings.cs, applied at start and on every change
/// (GameSettings.Apply -> ApplyOptions). Each page has its own block and its own Reset...Defaults().
/// Graphics overrides use -1 = "Auto" (follow the Low / Medium / High preset).
/// </summary>
public static partial class GameSettings
{
    private static int GetI(string k, int d) => PlayerPrefs.GetInt(P + k, d);
    private static void SetI(string k, int v) { PlayerPrefs.SetInt(P + k, v); Save(); }
    private static bool GetB(string k, bool d) => PlayerPrefs.GetInt(P + k, d ? 1 : 0) == 1;
    private static void SetB(string k, bool v) { PlayerPrefs.SetInt(P + k, v ? 1 : 0); Save(); }
    /// <summary>Remove keys without raising Changed for each (one Save() at the end).</summary>
    private static void Delete(params string[] keys) { foreach (var k in keys) PlayerPrefs.DeleteKey(P + k); Save(); }

    /// <summary>Next value of an option list, wrapping (used by every Left / Right row).</summary>
    public static int Cycle(int[] options, int current, int d)
    {
        int i = Array.IndexOf(options, current);
        if (i < 0) i = 0;
        return options[(i + d + options.Length) % options.Length];
    }
    private static int Pick(int[] options, int v, int d) => Array.IndexOf(options, v) >= 0 ? v : d;

    // ================================================================== GAME
    /// <summary>Clock speed like big farming sims (x1 = real time ... x120). 0 = use Day length.</summary>
    public static readonly int[] TimeScales = { 0, 1, 2, 3, 5, 10, 15, 30, 60, 120 };
    public static int TimeScale { get => Pick(TimeScales, GetI("timeScale", 0), 0); set => SetI("timeScale", Pick(TimeScales, value, 0)); }
    /// <summary>Real seconds per in-game day from Time scale (or Day length when Time scale is off).</summary>
    public static float SecondsPerDay => TimeScale > 0 ? 86400f / TimeScale : DayMinutes * 60f;

    /// <summary>Autosave: -1 every in-game day (default), 0 off, else every N real minutes.</summary>
    public static readonly int[] AutosaveOptions = { -1, 0, 5, 10, 15, 30 };
    public static int AutosaveMinutes { get => Pick(AutosaveOptions, GetI("autosave", -1), -1); set => SetI("autosave", Pick(AutosaveOptions, value, -1)); }
    /// <summary>Crops on open fields die when left completely dry for a whole in-game day.</summary>
    public static bool Withering { get => GetB("withering", false); set => SetB("withering", value); }
    public static bool Weeds { get => GetB("weeds", true); set => SetB("weeds", value); }
    /// <summary>Soil compaction lowers yields until the field is ploughed deep (Soil model).</summary>
    public static bool PlowingRequired { get => GetB("plowing", true); set => SetB("plowing", value); }
    /// <summary>Hired field hands buy seeds themselves when the store is empty.</summary>
    public static bool HelperRefill { get => GetB("helperRefill", false); set => SetB("helperRefill", value); }
    /// <summary>Ambient people / animals around the farm: 0 Off, 1 Low, 2 Medium, 3 High.</summary>
    public static readonly string[] DensityNames = { "Off", "Low", "Medium", "High" };
    public static int AmbientDensity { get => Mathf.Clamp(GetI("ambientDensity", 3), 0, 3); set => SetI("ambientDensity", Mathf.Clamp(value, 0, 3)); }
    public static bool ShowHints { get => GetB("hints", true); set => SetB("hints", value); }
    public static bool CameraShake { get => GetB("cameraShake", true); set => SetB("cameraShake", value); }
    public static bool Tutorials { get => GetB("tutorials", true); set => SetB("tutorials", value); }

    public static void ResetGameDefaults()
    {
        Delete("timeScale", "dayMinutes", "autosave", "withering", "weeds", "plowing", "helperRefill", "ambientDensity", "hints", "cameraShake", "tutorials",
            "gearbox", "lowTyres", "autoEngine");
    }

    // ================================================================== DISPLAY
    public static readonly string[] WindowModeNames = { "Fullscreen", "Borderless window", "Windowed" };
    public static int WindowMode { get => Mathf.Clamp(GetI("windowMode", 1), 0, 2); set => SetI("windowMode", Mathf.Clamp(value, 0, 2)); }
    /// <summary>Chosen resolution; 0 x 0 = the screen's own resolution.</summary>
    public static int ResolutionWidth => GetI("resW", 0);
    public static int ResolutionHeight => GetI("resH", 0);
    public static void SetResolution(int w, int h) { PlayerPrefs.SetInt(P + "resW", w); SetI("resH", h); }
    /// <summary>Distinct sizes the screen offers (largest last).</summary>
    public static List<Vector2Int> Resolutions()
    {
        var list = new List<Vector2Int>();
        var all = Screen.resolutions;
        if (all != null) foreach (var r in all) { var v = new Vector2Int(r.width, r.height); if (!list.Contains(v)) list.Add(v); }
        if (list.Count == 0) list.Add(new Vector2Int(Screen.width, Screen.height));
        list.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        return list;
    }
    public static void CycleResolution(int d)
    {
        var list = Resolutions();
        int i = list.IndexOf(new Vector2Int(ResolutionWidth, ResolutionHeight));
        if (i < 0) i = list.Count - 1;   // native = the largest
        var r = list[(i + d + list.Count) % list.Count];
        SetResolution(r.x, r.y);
    }
    /// <summary>FPS limit, 0 = unlimited.</summary>
    public static readonly int[] FpsLimits = { 30, 60, 120, 0 };
    public static int FpsLimit { get => Pick(FpsLimits, GetI("fps", 0), 0); set => SetI("fps", Pick(FpsLimits, value, 0)); }
    public static bool VSync { get => GetB("vsync", true); set => SetB("vsync", value); }
    /// <summary>Render scale in percent (50..200), 0 = Auto (preset).</summary>
    public static readonly int[] RenderScales = { 0, 50, 60, 70, 80, 90, 100, 125, 150, 175, 200 };
    public static int RenderScale { get => Pick(RenderScales, GetI("renderScale", 0), 0); set => SetI("renderScale", Pick(RenderScales, value, 0)); }
    /// <summary>Brightness 0.5..1.5 (1 = normal), as post-exposure.</summary>
    public static float Brightness { get => Get("brightness", 1f); set => Set("brightness", Mathf.Clamp(Mathf.Round(value * 10f) / 10f, 0.5f, 1.5f)); }

    public static void ResetDisplayDefaults() => Delete("windowMode", "resW", "resH", "fps", "vsync", "renderScale", "brightness");

    // ================================================================== GRAPHICS (overrides of the preset; -1 = Auto)
    public static readonly string[] ShadowQualityNames = { "Off", "Low", "Medium", "High" };
    public static int ShadowQuality { get => Mathf.Clamp(GetI("shadowQ", -1), -1, 3); set => SetI("shadowQ", Mathf.Clamp(value, -1, 3)); }
    public static readonly int[] ShadowDistances = { -1, 25, 50, 75, 100, 150, 200 };
    public static int ShadowDistance { get => Pick(ShadowDistances, GetI("shadowDist", -1), -1); set => SetI("shadowDist", Pick(ShadowDistances, value, -1)); }
    public static readonly int[] ViewDistances = { -1, 300, 500, 800, 1200, 2000 };
    public static int ViewDistance { get => Pick(ViewDistances, GetI("viewDist", -1), -1); set => SetI("viewDist", Pick(ViewDistances, value, -1)); }
    /// <summary>LOD bias in percent (50 = 0.5).</summary>
    public static readonly int[] LodBiases = { -1, 50, 100, 150, 200 };
    public static int LodBias { get => Pick(LodBiases, GetI("lodBias", -1), -1); set => SetI("lodBias", Pick(LodBiases, value, -1)); }
    public static readonly int[] FoliageDensities = { -1, 0, 25, 50, 75, 100 };
    public static int FoliageDensity { get => Pick(FoliageDensities, GetI("foliage", -1), -1); set => SetI("foliage", Pick(FoliageDensities, value, -1)); }
    public static readonly string[] TextureQualityNames = { "High", "Medium", "Low" };
    /// <summary>0 full, 1 half, 2 quarter resolution textures (mipmap limit).</summary>
    public static int TextureQuality { get => Mathf.Clamp(GetI("textureQ", 0), 0, 2); set => SetI("textureQ", Mathf.Clamp(value, 0, 2)); }
    public static readonly string[] AntiAliasingNames = { "Off", "FXAA", "SMAA", "MSAA 4x + SMAA" };
    public static int AntiAliasing { get => Mathf.Clamp(GetI("aa", -1), -1, 3); set => SetI("aa", Mathf.Clamp(value, -1, 3)); }
    public static bool AmbientOcclusion { get => GetB("ao", true); set => SetB("ao", value); }
    public static bool Bloom { get => GetB("bloom", true); set => SetB("bloom", value); }
    public static bool MotionBlur { get => GetB("motionBlur", false); set => SetB("motionBlur", value); }
    public static bool DepthOfField { get => GetB("dof", false); set => SetB("dof", value); }
    public static bool FogHaze { get => GetB("fog", true); set => SetB("fog", value); }

    /// <summary>Back to the preset (keeps the preset itself).</summary>
    public static void ResetGraphicsDefaults() => Delete("shadowQ", "shadowDist", "viewDist", "lodBias", "foliage", "textureQ", "aa", "ao", "bloom", "motionBlur", "dof", "fog");

    // ================================================================== SOUND (Master / Music / Effects are in GameSettings.cs)
    public static float VehicleVolume { get => Get("volVehicles", 1f); set => Set("volVehicles", Mathf.Clamp01(value)); }
    public static float AmbientVolume { get => Get("volAmbient", 1f); set => Set("volAmbient", Mathf.Clamp01(value)); }
    public static float AnimalVolume { get => Get("volAnimals", 1f); set => Set("volAnimals", Mathf.Clamp01(value)); }
    public static float UiVolume { get => Get("volUi", 1f); set => Set("volUi", Mathf.Clamp01(value)); }
    public static float VoiceVolume { get => Get("volVoice", 1f); set => Set("volVoice", Mathf.Clamp01(value)); }

    public static void ResetSoundDefaults() => Delete("master", "music", "sfx", "volVehicles", "volAmbient", "volAnimals", "volUi", "volVoice");

    // ================================================================== CONTROLS (mouse / stick sensitivity, invert Y, camera view are in GameSettings.cs)
    /// <summary>Extra radial dead zone for the sticks (look and steering), 0..0.5.</summary>
    public static float StickDeadZone { get => Get("deadZone", 0.1f); set => Set("deadZone", Mathf.Clamp(Mathf.Round(value * 100f) / 100f, 0f, 0.5f)); }
    public static bool Vibration { get => GetB("vibration", true); set => SetB("vibration", value); }
    public static float SteeringSensitivity { get => Get("steerSens", 1f); set => Set("steerSens", Mathf.Clamp(Mathf.Round(value * 10f) / 10f, 0.3f, 2f)); }
    /// <summary>Field of view in degrees for the player / vehicle cameras; 0 = Auto (each camera's own).</summary>
    public static readonly int[] Fovs = { 0, 50, 55, 60, 65, 70, 75, 80, 85, 90, 95, 100 };
    public static int CameraFov { get => Pick(Fovs, GetI("fov", 0), 0); set => SetI("fov", Pick(Fovs, value, 0)); }

    public static void ResetControlsDefaults() => Delete("mouseSens", "stickSens", "invertY", "deadZone", "vibration", "steerSens", "fov");

    /// <summary>Radial dead zone then rescale, so small stick drift is ignored and full tilt stays full.</summary>
    public static Vector2 ApplyDeadZone(Vector2 v)
    {
        float dz = StickDeadZone, m = Mathf.Sqrt(v.x * v.x + v.y * v.y);
        if (m <= dz || m < 1e-5f) return Vector2.zero;
        float k = Mathf.Clamp01((m - dz) / Mathf.Max(0.01f, 1f - dz)) / m;
        return v * k;
    }

    /// <summary>Steering input (-1..1) after dead zone and steering sensitivity (vehicles call this).</summary>
    public static float ShapeSteer(float steer)
    {
        float a = Mathf.Abs(steer), dz = StickDeadZone;
        if (a <= dz) return 0f;
        float t = Mathf.Clamp01((a - dz) / Mathf.Max(0.01f, 1f - dz));
        return Mathf.Clamp(Mathf.Sign(steer) * t * SteeringSensitivity, -1f, 1f);
    }

    /// <summary>Short controller rumble (ignored when Vibration is off or no pad is used).</summary>
    public static void Rumble(float low, float high, float seconds)
    {
        var pad = Gamepad.current;
        if (pad == null || !Vibration || !GameInput.UsingGamepad) return;
        try { pad.SetMotorSpeeds(Mathf.Clamp01(low), Mathf.Clamp01(high)); } catch (Exception) { return; }
        RumbleUntil = Time.unscaledTime + seconds;
    }
    public static float RumbleUntil { get; private set; } = -1f;
    public static void StopRumble()
    {
        RumbleUntil = -1f;
        var pad = Gamepad.current;
        if (pad != null) try { pad.SetMotorSpeeds(0f, 0f); } catch (Exception) { }
    }

    // ================================================================== ACCESSIBILITY
    /// <summary>UI scale in percent (90..150) for menus and HUD.</summary>
    public static readonly int[] UiScales = { 90, 100, 110, 120, 130, 140, 150 };
    public static int UiScale { get => Pick(UiScales, GetI("uiScale", 100), 100); set => SetI("uiScale", Pick(UiScales, value, 100)); }
    public static readonly string[] ColorBlindNames = { "Off", "Protanopia", "Deuteranopia", "Tritanopia" };
    public static int ColorBlindMode { get => Mathf.Clamp(GetI("colorBlind", 0), 0, 3); set => SetI("colorBlind", Mathf.Clamp(value, 0, 3)); }
    /// <summary>How long messages / notifications stay (multiplier).</summary>
    public static readonly int[] NotificationPercents = { 100, 150, 200, 300 };
    public static int NotificationPercent { get => Pick(NotificationPercents, GetI("notify", 100), 100); set => SetI("notify", Pick(NotificationPercents, value, 100)); }
    public static float NotificationScale => NotificationPercent / 100f;
    /// <summary>No camera shake / bob, no motion blur or depth of field.</summary>
    public static bool ReduceMotion { get => GetB("reduceMotion", false); set => SetB("reduceMotion", value); }
    public static bool HighContrast { get => GetB("highContrast", false); set => SetB("highContrast", value); }

    public static void ResetAccessibilityDefaults() => Delete("uiScale", "colorBlind", "notify", "reduceMotion", "highContrast");

    /// <summary>Camera shake strength other systems should use (0 when off or Reduce motion is on).</summary>
    public static float ShakeScale => CameraShake && !ReduceMotion ? 1f : 0f;

    // Okabe-Ito colours: distinguishable with every common colour-vision deficiency.
    private static readonly Color Vermillion = new Color(0.84f, 0.37f, 0f), Orange = new Color(0.9f, 0.62f, 0f), SkyBlue = new Color(0.34f, 0.71f, 0.91f),
        BluishGreen = new Color(0f, 0.62f, 0.45f), Yellow = new Color(0.94f, 0.89f, 0.26f), Blue = new Color(0f, 0.45f, 0.7f), Purple = new Color(0.8f, 0.47f, 0.65f);

    /// <summary>A map / HUD colour that matters (vehicle red vs loading bay green...) in the chosen colour-blind palette.</summary>
    public static Color Palette(Color c) => Palette(c, ColorBlindMode);
    public static Color Palette(Color c, int mode)
    {
        if (mode <= 0) return c;
        float h, s, v;
        Color.RGBToHSV(c, out h, out s, out v);
        if (s < 0.25f || v < 0.15f) return c;   // greys / whites / near black keep their look
        float deg = h * 360f;
        Color o;
        if (mode == 3)   // tritanopia: blue-green and yellow-violet confusions
            o = deg < 20f || deg >= 330f ? Vermillion : deg < 50f ? Orange : deg < 75f ? Purple : deg < 170f ? BluishGreen : deg < 260f ? Purple : Vermillion;
        else             // protanopia / deuteranopia: red-green confusions
            o = deg < 20f || deg >= 330f ? Purple : deg < 45f ? Orange : deg < 75f ? Yellow : deg < 170f ? Blue : deg < 210f ? SkyBlue : c;
        o.a = c.a;
        return o;
    }

    // ================================================================== apply
    private static int lastResW = -1, lastResH = -1, lastWindowMode = -1;

    /// <summary>Push the new settings into Unity and the game's systems (called by Apply; null-safe).</summary>
    public static void ApplyOptions()
    {
        // Display (resolution / window mode only when they really change, to avoid a flicker on every setting).
        try
        {
            if (WindowMode != lastWindowMode || ResolutionWidth != lastResW || ResolutionHeight != lastResH)
            {
                lastWindowMode = WindowMode; lastResW = ResolutionWidth; lastResH = ResolutionHeight;
                var mode = WindowMode == 0 ? FullScreenMode.ExclusiveFullScreen : WindowMode == 1 ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
                if (Application.platform != RuntimePlatform.WebGLPlayer && !Application.isMobilePlatform)
                {
                    int w = ResolutionWidth > 0 ? ResolutionWidth : Screen.currentResolution.width;
                    int h = ResolutionHeight > 0 ? ResolutionHeight : Screen.currentResolution.height;
                    if (w > 0 && h > 0) Screen.SetResolution(w, h, mode);
                }
            }
            QualitySettings.vSyncCount = VSync ? 1 : 0;
            Application.targetFrameRate = FpsLimit > 0 ? FpsLimit : -1;
            QualitySettings.globalTextureMipmapLimit = TextureQuality;
            if (LodBias > 0) QualitySettings.lodBias = LodBias / 100f;
        }
        catch (Exception e) { Debug.LogWarning("[Settings] display: " + e.Message); }

        GraphicsEnhancer.ApplyOverrides();
        SettingsApplier.ApplyNow();
    }

    /// <summary>All option names shown by the settings pages (Arabic check in tests).</summary>
    public static IEnumerable<string> OptionNames()
    {
        foreach (var a in new[] { DensityNames, WindowModeNames, ShadowQualityNames, TextureQualityNames, AntiAliasingNames, ColorBlindNames })
            foreach (var n in a) yield return n;
    }
}
