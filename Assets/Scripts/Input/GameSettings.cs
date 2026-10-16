using System;
using UnityEngine;

/// <summary>
/// Player settings kept in PlayerPrefs under "mazraa.settings.*" (never part of the save file, so New Game and
/// loading a slot keep them): volumes, graphics preset, mouse / stick sensitivity, invert Y, camera view.
/// Language is stored by Loc itself (also kept on New Game).
/// </summary>
public static class GameSettings
{
    private const string P = "mazraa.settings.";
    public static event Action Changed;

    public static float MasterVolume { get => Get("master", 1f); set => Set("master", Mathf.Clamp01(value)); }
    public static float MusicVolume { get => Get("music", 0.8f); set => Set("music", Mathf.Clamp01(value)); }
    public static float SfxVolume { get => Get("sfx", 1f); set => Set("sfx", Mathf.Clamp01(value)); }
    /// <summary>0 Low, 1 Medium, 2 High.</summary>
    public static int GraphicsPreset
    {
        get => Mathf.Clamp(PlayerPrefs.GetInt(P + "graphics", DefaultPreset()), 0, 2);
        set { PlayerPrefs.SetInt(P + "graphics", Mathf.Clamp(value, 0, 2)); Save(); }
    }
    public static float MouseSensitivity { get => Get("mouseSens", 1f); set => Set("mouseSens", Mathf.Clamp(value, 0.1f, 4f)); }
    public static float StickSensitivity { get => Get("stickSens", 1f); set => Set("stickSens", Mathf.Clamp(value, 0.1f, 4f)); }
    public static bool InvertY { get => PlayerPrefs.GetInt(P + "invertY", 0) == 1; set { PlayerPrefs.SetInt(P + "invertY", value ? 1 : 0); Save(); } }
    public static bool ThirdPerson { get => PlayerPrefs.GetInt(P + "thirdPerson", 0) == 1; set { PlayerPrefs.SetInt(P + "thirdPerson", value ? 1 : 0); Save(); } }

    /// <summary>Real minutes per in-game day (6 = fast, 24 = default, 48 = relaxed).</summary>
    public static readonly int[] DayLengths = { 6, 12, 24, 48 };
    public static int DefaultDayMinutes = 24;   // headless tests use 6 (the old day length)
    public static int DayMinutes
    {
        get { int v = PlayerPrefs.GetInt(P + "dayMinutes", DefaultDayMinutes); return System.Array.IndexOf(DayLengths, v) >= 0 ? v : DefaultDayMinutes; }
        set { PlayerPrefs.SetInt(P + "dayMinutes", value); Save(); }
    }
    public static void CycleDayLength(int d)
    {
        int i = System.Array.IndexOf(DayLengths, DayMinutes);
        DayMinutes = DayLengths[(i + d + DayLengths.Length) % DayLengths.Length];
    }

    private static int DefaultPreset()
    {
        // Follow the old G-key setting if the player used it before settings existed.
        int high = SaveData.GetInt("mazraa.gfxHigh", Application.platform == RuntimePlatform.WebGLPlayer ? 0 : 1);
        return high == 1 ? 2 : 0;
    }

    private static float Get(string k, float d) => PlayerPrefs.GetFloat(P + k, d);
    private static void Set(string k, float v) { PlayerPrefs.SetFloat(P + k, v); Save(); }
    private static void Save() { PlayerPrefs.Save(); if (Changed != null) Changed(); }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Changed = null;

    /// <summary>Push audio and graphics values into the game (called at start and whenever a value changes).</summary>
    public static void Apply()
    {
        AudioListener.volume = MasterVolume;
        if (AudioManager.Instance != null) AudioManager.Instance.SetVolumes(MusicVolume, SfxVolume);
        GraphicsEnhancer.SetPreset(GraphicsPreset);
        if (WorldClock.Instance != null) WorldClock.Instance.secondsPerDay = DayMinutes * 60f;
    }

    public static string PresetName(int p) => p <= 0 ? "Low" : p == 1 ? "Medium" : "High";
}
