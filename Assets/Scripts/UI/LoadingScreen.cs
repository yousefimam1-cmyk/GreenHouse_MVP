using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Branded loading screen with music, in the style of big farming sims: full-screen Mazraa artwork (logo, palm-green and
/// sand bands), a progress bar, the farm being loaded and a rotating gameplay tip. Shown when the game starts and while a
/// save is loaded (SaveSlots.Load reloads the scene; the overlay survives the reload). The Mazraa theme (MenuMusic,
/// synthesised) plays here and keeps playing under the main menu, then fades out when the farm starts and the normal
/// farm music resumes. Built in code: one canvas, flat Images, no textures except the logo.
/// </summary>
[MazraaSystem(-50)]
public class LoadingScreen : MonoBehaviour
{
    public const float MinSeconds = 3.2f, FadeSeconds = 0.6f;
    public static bool Visible => overlay != null && overlay.canvasGo != null && overlay.canvasGo.activeSelf;
    public static float Progress => overlay != null ? overlay.progress : 1f;
    public static bool MusicPlaying => overlay != null && overlay.music != null && overlay.music.isPlaying;
    public static string TipText => overlay != null && overlay.tip != null ? overlay.tip.text : "";

    private static LoadingOverlay overlay;
    private static bool firstBoot = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { overlay = null; firstBoot = true; }

    public static readonly string[] Tips =
    {
        "Water early in the morning: less water is lost to the summer heat",
        "Drip irrigation uses far less water than flooding a field",
        "Sell when a crop's price arrow points up - prices drop when everyone sells the same crop",
        "Rotate your crops to keep the soil healthy and the pests away",
        "Fill your tractor's fuel at the loading bay before a long field job",
        "Greenhouses grow vegetables all year, even in the hottest months",
        "Animals need food and water every day - happy animals produce more",
        "Hire workers to harvest while you go to the market",
        "Take a loan to buy land early, then pay it back from your first harvests",
        "Press Esc any time for the map, prices and your farm overview",
        "Dates, wheat and alfalfa each have their own planting season",
        "Solar panels cut your farm's power bill to almost nothing",
    };

    // ------------------------------------------------------------------ scene hooks

    private void Start()
    {
        // First launch: show the loading screen over the starting scene. After a save is loaded the overlay is
        // already up (Show was called before the reload), so just mark the new scene as ready.
        if (firstBoot) { firstBoot = false; Show(); }
        if (overlay != null) overlay.SceneReady();
    }

    /// <summary>Show the loading screen (game start, loading a save). Safe to call more than once.</summary>
    public static void Show()
    {
        if (overlay == null)
        {
            var go = new GameObject("Mazraa Loading Screen");
            DontDestroyOnLoad(go);
            overlay = go.AddComponent<LoadingOverlay>();
            overlay.Build();
        }
        overlay.Begin();
    }

}
