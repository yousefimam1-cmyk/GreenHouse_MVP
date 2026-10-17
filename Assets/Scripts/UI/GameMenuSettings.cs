using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Settings hub of the main / pause menu: Game, Display, Graphics, Sound, Controls, Accessibility pages and Language.
/// Every page: Up / Down select, Left / Right (D-pad, stick, &lt; &gt; buttons) change, LB / RB (Page Up / Down) switch page,
/// "Reset to defaults" for that page, Esc / B back to the hub. Values live in GameSettings (PlayerPrefs).
/// </summary>
public partial class GameMenu
{
    public enum SettingsTab { Game, Display, Graphics, Sound, Controls, Accessibility }
    public static readonly string[] SettingsTabNames = { "Game", "Display", "Graphics", "Sound", "Controls", "Accessibility" };

    private SettingsTab settingsTab = SettingsTab.Game;
    public SettingsTab CurrentSettingsTab => settingsTab;
    /// <summary>True while one of the settings sub-pages is shown.</summary>
    public bool OnSettingsPage => mode != Mode.Closed && page == PageId.SettingsPage;
    public bool OnSettingsHub => mode != Mode.Closed && page == PageId.Settings;

    /// <summary>Open a settings page straight away (tests, shortcuts).</summary>
    public void OpenSettings(SettingsTab tab)
    {
        if (mode == Mode.Closed) Open(Mode.Pause);
        if (page != PageId.Settings) Go(PageId.Settings);
        OpenTab(tab);
    }

    private void OpenTab(SettingsTab tab) { settingsTab = tab; Go(PageId.SettingsPage); }

    private void ShowSettingsHub(int sel)
    {
        list.Show(() => MenuKit.Title(UiText.T("Settings")), SettingsRows(), () => UiText.T("[Enter] Choose   [Esc] Back"), sel);
    }

    private void ShowSettingsPage(int sel)
    {
        list.Show(() => MenuKit.Title(UiText.T("Settings") + " - " + UiText.T(SettingsTabNames[(int)settingsTab])), TabRows(settingsTab),
                  () => UiText.T("[<- ->] Change   LB / RB Page   [Esc] Back"), sel);
    }

    /// <summary>LB / RB (Page Up / Page Down) turn the settings pages. Returns true when handled.</summary>
    private bool SettingsTabInput()
    {
        if (page != PageId.SettingsPage) return false;
        var kb = Keyboard.current;
        int d = MenuNav.PageNext() || (kb != null && kb.pageDownKey != null && kb.pageDownKey.wasPressedThisFrame) ? 1
              : MenuNav.PagePrev() || (kb != null && kb.pageUpKey != null && kb.pageUpKey.wasPressedThisFrame) ? -1 : 0;
        if (d == 0) return false;
        int n = SettingsTabNames.Length;
        settingsTab = (SettingsTab)(((int)settingsTab + d + n) % n);
        ShowSettingsPage(0);
        return true;
    }

    private List<MenuRow> SettingsRows()
    {
        var rows = new List<MenuRow>();
        foreach (SettingsTab t in Enum.GetValues(typeof(SettingsTab)))
        {
            var tab = t;
            rows.Add(MenuRow.Button(() => UiText.T(SettingsTabNames[(int)tab]), () => OpenTab(tab)));
        }
        rows.Add(MenuRow.Setting(() => UiText.T("Language"), () => Loc.IsArabic ? Loc.Display("العربية") : "English", d => Loc.Toggle()));
        rows.Add(MenuRow.Button(() => UiText.T("Back"), Back));
        return rows;
    }

    private List<MenuRow> TabRows(SettingsTab tab)
    {
        List<MenuRow> rows;
        Action reset;
        switch (tab)
        {
            case SettingsTab.Game: rows = GameRows(); reset = GameSettings.ResetGameDefaults; break;
            case SettingsTab.Display: rows = DisplayRows(); reset = GameSettings.ResetDisplayDefaults; break;
            case SettingsTab.Graphics: rows = GraphicsRows(); reset = GameSettings.ResetGraphicsDefaults; break;
            case SettingsTab.Sound: rows = SoundRows(); reset = GameSettings.ResetSoundDefaults; break;
            case SettingsTab.Controls: rows = ControlSettingRows(); reset = GameSettings.ResetControlsDefaults; break;
            default: rows = AccessibilityRows(); reset = GameSettings.ResetAccessibilityDefaults; break;
        }
        rows.Add(MenuRow.Button(() => UiText.T("Reset to defaults"), () => { reset(); list.Refresh(); }));
        rows.Add(MenuRow.Button(() => UiText.T("Back"), Back));
        return rows;
    }

    // ------------------------------------------------------------------ row helpers
    private static MenuRow Toggle(Func<string> label, Func<bool> get, Action<bool> set) => MenuRow.Setting(label, () => OnOff(get()), d => set(!get()));
    private static MenuRow Volume(Func<string> label, Func<float> get, Action<float> set) => MenuRow.Setting(label, () => Pct(get()), d => set(Step(get(), d * 0.1f, 0f, 1f)));
    /// <summary>-1 = Auto, then 0..names-1.</summary>
    private static int CycleAuto(int v, int count, int d) => ((v + 1 + d) % (count + 1) + count + 1) % (count + 1) - 1;
    private static string Auto() => UiText.T("Auto");
    private static string Named(string[] names, int v) => v < 0 ? Auto() : UiText.T(names[Mathf.Clamp(v, 0, names.Length - 1)]);

    // ------------------------------------------------------------------ Game
    private List<MenuRow> GameRows()
    {
        return new List<MenuRow>
        {
            MenuRow.Setting(() => UiText.T("Time scale"), () => GameSettings.TimeScale == 0 ? UiText.T("Use day length") : "x" + GameSettings.TimeScale,
                            d => GameSettings.TimeScale = GameSettings.Cycle(GameSettings.TimeScales, GameSettings.TimeScale, d)),
            new MenuRow { label = () => UiText.T("Day length"), value = () => GameSettings.DayMinutes + " " + UiText.T("min"), adjust = d => GameSettings.CycleDayLength(d),
                          confirm = () => GameSettings.CycleDayLength(1), enabled = () => GameSettings.TimeScale == 0 },
            MenuRow.Setting(() => UiText.T("Autosave"), () => GameSettings.AutosaveMinutes < 0 ? UiText.T("Every day") : GameSettings.AutosaveMinutes == 0 ? UiText.T("Off") : GameSettings.AutosaveMinutes + " " + UiText.T("min"),
                            d => GameSettings.AutosaveMinutes = GameSettings.Cycle(GameSettings.AutosaveOptions, GameSettings.AutosaveMinutes, d)),
            Toggle(() => UiText.T("Crops wither when dry"), () => GameSettings.Withering, v => GameSettings.Withering = v),
            Toggle(() => UiText.T("Weeds"), () => GameSettings.Weeds, v => GameSettings.Weeds = v),
            Toggle(() => UiText.T("Plowing required (soil compaction)"), () => GameSettings.PlowingRequired, v => GameSettings.PlowingRequired = v),
            Toggle(() => UiText.T("Helpers buy seeds"), () => GameSettings.HelperRefill, v => GameSettings.HelperRefill = v),
            MenuRow.Setting(() => UiText.T("Ambient life"), () => UiText.T(GameSettings.DensityNames[GameSettings.AmbientDensity]), d => GameSettings.AmbientDensity = (GameSettings.AmbientDensity + d + 4) % 4),
            Toggle(() => UiText.T("Help hints"), () => GameSettings.ShowHints, v => GameSettings.ShowHints = v),
            Toggle(() => UiText.T("Camera shake"), () => GameSettings.CameraShake, v => GameSettings.CameraShake = v),
            Toggle(() => UiText.T("Tutorials"), () => GameSettings.Tutorials, v => GameSettings.Tutorials = v),
            // Vehicles (Drivetrain): gearbox, tyre pressure, engine start
            MenuRow.Setting(() => UiText.T("Gearbox"), () => UiText.T(GameSettings.GearboxName(GameSettings.Gearbox)), d => GameSettings.Gearbox = (GearboxMode)(((int)GameSettings.Gearbox + d + 3) % 3)),
            MenuRow.Setting(() => UiText.T("Tyre pressure"), () => UiText.T(GameSettings.LowTyrePressure ? "Low (field)" : "High (road)"), d => GameSettings.LowTyrePressure = !GameSettings.LowTyrePressure),
            Toggle(() => UiText.T("Auto engine start"), () => GameSettings.AutoEngineStart, v => GameSettings.AutoEngineStart = v),
        };
    }

    // ------------------------------------------------------------------ Display
    private List<MenuRow> DisplayRows()
    {
        return new List<MenuRow>
        {
            MenuRow.Setting(() => UiText.T("Resolution"), () => GameSettings.ResolutionWidth > 0 ? GameSettings.ResolutionWidth + " x " + GameSettings.ResolutionHeight : UiText.T("Native"), d => GameSettings.CycleResolution(d)),
            MenuRow.Setting(() => UiText.T("Window mode"), () => UiText.T(GameSettings.WindowModeNames[GameSettings.WindowMode]), d => GameSettings.WindowMode = (GameSettings.WindowMode + d + 3) % 3),
            MenuRow.Setting(() => UiText.T("FPS limit"), () => GameSettings.FpsLimit > 0 ? GameSettings.FpsLimit + " FPS" : UiText.T("Unlimited"), d => GameSettings.FpsLimit = GameSettings.Cycle(GameSettings.FpsLimits, GameSettings.FpsLimit, d)),
            Toggle(() => UiText.T("VSync"), () => GameSettings.VSync, v => GameSettings.VSync = v),
            MenuRow.Setting(() => UiText.T("Render scale"), () => GameSettings.RenderScale > 0 ? GameSettings.RenderScale + "%" : Auto(), d => GameSettings.RenderScale = GameSettings.Cycle(GameSettings.RenderScales, GameSettings.RenderScale, d)),
            MenuRow.Setting(() => UiText.T("Brightness"), () => Pct(GameSettings.Brightness), d => GameSettings.Brightness = GameSettings.Brightness + d * 0.1f),
        };
    }

    // ------------------------------------------------------------------ Graphics
    private List<MenuRow> GraphicsRows()
    {
        return new List<MenuRow>
        {
            MenuRow.Setting(() => UiText.T("Quality preset"), () => UiText.T(GameSettings.PresetName(GameSettings.GraphicsPreset)), d => GameSettings.GraphicsPreset = (GameSettings.GraphicsPreset + d + GraphicsEnhancer.PresetCount) % GraphicsEnhancer.PresetCount),
            MenuRow.Setting(() => UiText.T("Shadow quality"), () => Named(GameSettings.ShadowQualityNames, GameSettings.ShadowQuality), d => GameSettings.ShadowQuality = CycleAuto(GameSettings.ShadowQuality, 4, d)),
            MenuRow.Setting(() => UiText.T("Shadow distance"), () => GameSettings.ShadowDistance > 0 ? GameSettings.ShadowDistance + " m" : Auto(), d => GameSettings.ShadowDistance = GameSettings.Cycle(GameSettings.ShadowDistances, GameSettings.ShadowDistance, d)),
            MenuRow.Setting(() => UiText.T("View distance"), () => GameSettings.ViewDistance > 0 ? GameSettings.ViewDistance + " m" : Auto(), d => GameSettings.ViewDistance = GameSettings.Cycle(GameSettings.ViewDistances, GameSettings.ViewDistance, d)),
            MenuRow.Setting(() => UiText.T("Level of detail"), () => GameSettings.LodBias > 0 ? Mult(GameSettings.LodBias / 100f) : Auto(), d => GameSettings.LodBias = GameSettings.Cycle(GameSettings.LodBiases, GameSettings.LodBias, d)),
            MenuRow.Setting(() => UiText.T("Foliage and props"), () => GameSettings.FoliageDensity >= 0 ? GameSettings.FoliageDensity + "%" : Auto(), d => GameSettings.FoliageDensity = GameSettings.Cycle(GameSettings.FoliageDensities, GameSettings.FoliageDensity, d)),
            MenuRow.Setting(() => UiText.T("Texture quality"), () => UiText.T(GameSettings.TextureQualityNames[GameSettings.TextureQuality]), d => GameSettings.TextureQuality = (GameSettings.TextureQuality + d + 3) % 3),
            MenuRow.Setting(() => UiText.T("Anti-aliasing"), () => Named(GameSettings.AntiAliasingNames, GameSettings.AntiAliasing), d => GameSettings.AntiAliasing = CycleAuto(GameSettings.AntiAliasing, 4, d)),
            Toggle(() => UiText.T("Ambient occlusion"), () => GameSettings.AmbientOcclusion, v => GameSettings.AmbientOcclusion = v),
            Toggle(() => UiText.T("Bloom"), () => GameSettings.Bloom, v => GameSettings.Bloom = v),
            Toggle(() => UiText.T("Motion blur"), () => GameSettings.MotionBlur, v => GameSettings.MotionBlur = v),
            Toggle(() => UiText.T("Depth of field"), () => GameSettings.DepthOfField, v => GameSettings.DepthOfField = v),
            Toggle(() => UiText.T("Fog and haze"), () => GameSettings.FogHaze, v => GameSettings.FogHaze = v),
        };
    }

    // ------------------------------------------------------------------ Sound
    private List<MenuRow> SoundRows()
    {
        return new List<MenuRow>
        {
            Volume(() => UiText.T("Master volume"), () => GameSettings.MasterVolume, v => GameSettings.MasterVolume = v),
            Volume(() => UiText.T("Music volume"), () => GameSettings.MusicVolume, v => GameSettings.MusicVolume = v),
            Volume(() => UiText.T("Sound effects volume"), () => GameSettings.SfxVolume, v => GameSettings.SfxVolume = v),
            Volume(() => UiText.T("Vehicles"), () => GameSettings.VehicleVolume, v => GameSettings.VehicleVolume = v),
            Volume(() => UiText.T("Environment"), () => GameSettings.AmbientVolume, v => GameSettings.AmbientVolume = v),
            Volume(() => UiText.T("Animals"), () => GameSettings.AnimalVolume, v => GameSettings.AnimalVolume = v),
            Volume(() => UiText.T("Menus"), () => GameSettings.UiVolume, v => GameSettings.UiVolume = v),
            Volume(() => UiText.T("Voices"), () => GameSettings.VoiceVolume, v => GameSettings.VoiceVolume = v),
        };
    }

    // ------------------------------------------------------------------ Controls
    private List<MenuRow> ControlSettingRows()
    {
        return new List<MenuRow>
        {
            MenuRow.Button(() => UiText.T("Controls (rebind keys)"), () => Go(PageId.Controls)),
            MenuRow.Setting(() => UiText.T("Mouse sensitivity"), () => Mult(GameSettings.MouseSensitivity), d => GameSettings.MouseSensitivity = Step(GameSettings.MouseSensitivity, d * 0.1f, 0.1f, 3f)),
            MenuRow.Setting(() => UiText.T("Stick sensitivity"), () => Mult(GameSettings.StickSensitivity), d => GameSettings.StickSensitivity = Step(GameSettings.StickSensitivity, d * 0.1f, 0.1f, 3f)),
            Toggle(() => UiText.T("Invert Y axis"), () => GameSettings.InvertY, v => GameSettings.InvertY = v),
            MenuRow.Setting(() => UiText.T("Stick dead zone"), () => Pct(GameSettings.StickDeadZone), d => GameSettings.StickDeadZone = GameSettings.StickDeadZone + d * 0.05f),
            Toggle(() => UiText.T("Vibration"), () => GameSettings.Vibration, v => { GameSettings.Vibration = v; if (v) GameSettings.Rumble(0.3f, 0.3f, 0.2f); else GameSettings.StopRumble(); }),
            MenuRow.Setting(() => UiText.T("Steering sensitivity"), () => Mult(GameSettings.SteeringSensitivity), d => GameSettings.SteeringSensitivity = GameSettings.SteeringSensitivity + d * 0.1f),
            MenuRow.Setting(() => UiText.T("Field of view"), () => GameSettings.CameraFov > 0 ? GameSettings.CameraFov + "°" : Auto(), d => GameSettings.CameraFov = GameSettings.Cycle(GameSettings.Fovs, GameSettings.CameraFov, d)),
            MenuRow.Setting(() => UiText.T("Camera"), () => UiText.T(GameSettings.ThirdPerson ? "Third person" : "First person"), d => ThirdPersonView.SetMode(!GameSettings.ThirdPerson)),
        };
    }

    // ------------------------------------------------------------------ Accessibility
    private List<MenuRow> AccessibilityRows()
    {
        return new List<MenuRow>
        {
            MenuRow.Setting(() => UiText.T("Text and UI size"), () => GameSettings.UiScale + "%", d => GameSettings.UiScale = GameSettings.Cycle(GameSettings.UiScales, GameSettings.UiScale, d)),
            MenuRow.Setting(() => UiText.T("Colour-blind mode"), () => UiText.T(GameSettings.ColorBlindNames[GameSettings.ColorBlindMode]), d => GameSettings.ColorBlindMode = (GameSettings.ColorBlindMode + d + 4) % 4),
            MenuRow.Setting(() => UiText.T("Message duration"), () => Mult(GameSettings.NotificationScale), d => GameSettings.NotificationPercent = GameSettings.Cycle(GameSettings.NotificationPercents, GameSettings.NotificationPercent, d)),
            Toggle(() => UiText.T("Reduce motion"), () => GameSettings.ReduceMotion, v => GameSettings.ReduceMotion = v),
            Toggle(() => UiText.T("High-contrast menus"), () => GameSettings.HighContrast, v => GameSettings.HighContrast = v),
        };
    }
}
