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

    // ------------------------------------------------------------------ overlay (survives scene reloads)

    public class LoadingOverlay : MonoBehaviour
    {
        internal GameObject canvasGo;
        internal AudioSource music;
        internal TextMeshProUGUI tip, status, title;
        internal float progress;
        private RectTransform bar;
        private CanvasGroup group;
        private RawImage logo;
        private float shown, tipTimer, fade = -1f, musicVol, musicTarget;
        private bool ready;
        private int tipIndex;

        internal void Build()
        {
            var canvas = MenuKit.Canvas("Loading Canvas", transform, 200);
            canvasGo = canvas.gameObject;
            group = canvasGo.AddComponent<CanvasGroup>();
            group.alpha = 1f;

            var bg = MenuKit.Rect("Background", canvas.transform, typeof(Image));
            MenuKit.Stretch(bg);
            Img(bg, new Color(0.07f, 0.16f, 0.09f, 1f));
            // Sand horizon band with a gold rule: reads as desert farmland at sunset behind the logo.
            var sand = MenuKit.Rect("Sand", canvas.transform, typeof(Image));
            sand.anchorMin = new Vector2(0f, 0f); sand.anchorMax = new Vector2(1f, 0.3f);
            sand.offsetMin = sand.offsetMax = Vector2.zero;
            Img(sand, new Color(0.62f, 0.5f, 0.32f, 1f));
            var dune = MenuKit.Rect("Dune", canvas.transform, typeof(Image));
            dune.anchorMin = new Vector2(0f, 0f); dune.anchorMax = new Vector2(1f, 0.16f);
            dune.offsetMin = dune.offsetMax = Vector2.zero;
            Img(dune, new Color(0.5f, 0.39f, 0.24f, 1f));
            var rule = MenuKit.Rect("Gold Rule", canvas.transform, typeof(Image));
            rule.anchorMin = new Vector2(0f, 0.3f); rule.anchorMax = new Vector2(1f, 0.3f);
            rule.sizeDelta = new Vector2(0f, 4f);
            Img(rule, ControlsTutorial.Gold);

            var lr = MenuKit.Rect("Logo", canvas.transform, typeof(RawImage));
            lr.anchorMin = lr.anchorMax = lr.pivot = new Vector2(0.5f, 0.62f);
            logo = lr.GetComponent<RawImage>();
            logo.raycastTarget = false;
            title = Label(canvas.transform, "Title", 96, ControlsTutorial.Sand, new Vector2(0.5f, 0.62f), new Vector2(1200f, 140f));

            // Progress bar
            var track = MenuKit.Rect("Bar Track", canvas.transform, typeof(Image));
            track.anchorMin = track.anchorMax = track.pivot = new Vector2(0.5f, 0.3f);
            track.anchoredPosition = new Vector2(0f, 90f);
            track.sizeDelta = new Vector2(900f, 14f);
            Img(track, new Color(0f, 0f, 0f, 0.45f));
            bar = MenuKit.Rect("Bar Fill", track, typeof(Image));
            bar.anchorMin = new Vector2(0f, 0f); bar.anchorMax = new Vector2(0f, 1f);
            bar.offsetMin = bar.offsetMax = Vector2.zero;
            Img(bar, ControlsTutorial.Gold);
            status = Label(canvas.transform, "Status", 26, Color.white, new Vector2(0.5f, 0.3f), new Vector2(900f, 40f));
            status.rectTransform.anchoredPosition = new Vector2(0f, 130f);

            // Tip box
            var box = MenuKit.Rect("Tip Box", canvas.transform, typeof(Image));
            box.anchorMin = box.anchorMax = box.pivot = new Vector2(0.5f, 0f);
            box.anchoredPosition = new Vector2(0f, 60f);
            box.sizeDelta = new Vector2(1100f, 120f);
            Img(box, new Color(0.06f, 0.12f, 0.07f, 0.88f));
            tip = MenuKit.Text(box, 26, Color.white, "Tip");
            var trt = tip.rectTransform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(32f, 14f); trt.offsetMax = new Vector2(-32f, -14f);
            tip.alignment = TextAlignmentOptions.Center;
            Brand(tip);

            music = gameObject.AddComponent<AudioSource>();
            music.playOnAwake = false;
            music.loop = true;
            music.spatialBlend = 0f;
            music.clip = ProceduralAudio.Get("theme");
            canvasGo.SetActive(false);
        }

        internal void Begin()
        {
            shown = 0f; progress = 0f; ready = false; fade = -1f; tipTimer = 0f;
            tipIndex = Random.Range(0, Tips.Length);
            if (group != null) group.alpha = 1f;
            canvasGo.SetActive(true);
            Refresh();
            NextTip();
            if (music != null && music.clip != null && !music.isPlaying) { music.volume = 0f; music.Play(); }
            musicVol = music != null ? music.volume : 0f;
            if (AudioManager.Instance != null) AudioManager.Instance.SetFarmMusicPaused(true);
        }

        internal void SceneReady() { ready = true; }

        private void Refresh()
        {
            bool ar = Loc.IsArabic;
            var tex = Resources.Load<Texture2D>(ar ? "Branding/mazraa_logo_ar_512" : "Branding/mazraa_logo_en_512");
            if (logo != null)
            {
                logo.texture = tex;
                logo.enabled = tex != null;
                if (tex != null) logo.rectTransform.sizeDelta = new Vector2(760f, 760f * tex.height / Mathf.Max(1, tex.width));
            }
            title.enabled = tex == null;
            title.text = ar ? ArabicShaper.Shape("مزرعة") : "MAZRAA";
        }

        private void NextTip()
        {
            tipIndex = (tipIndex + 1) % Tips.Length;
            tip.text = "<color=#FAC240><b>" + Loc.T("Tip") + "</b></color>   " + Loc.T(Tips[tipIndex]);
            tipTimer = 6f;
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            UpdateMusic(dt);
            if (!canvasGo.activeSelf) return;
            shown += dt;
            if (!ready && shown > 12f) ready = true;      // fail-safe: never stay stuck on the loading screen
            // Fill to 90% over the minimum time, the rest once the scene is ready.
            float target = Mathf.Clamp01(shown / MinSeconds) * 0.9f + (ready ? 0.1f : 0f);
            progress = Mathf.MoveTowards(progress, target, dt * 0.8f);
            bar.anchorMax = new Vector2(progress, 1f);
            status.text = (progress >= 0.999f ? Loc.T("Ready") : Loc.T("Loading your farm")) + "  " + Mathf.RoundToInt(progress * 100f) + "%";
            tipTimer -= dt;
            if (tipTimer <= 0f) NextTip();
            if (fade < 0f && ready && shown >= MinSeconds && progress >= 0.999f) fade = FadeSeconds;
            if (fade >= 0f)
            {
                fade -= dt;
                group.alpha = Mathf.Clamp01(fade / FadeSeconds);
                if (fade <= 0f) canvasGo.SetActive(false);
            }
        }

        /// <summary>Theme plays while loading and under the main menu; it fades out once the farm is being played.</summary>
        private void UpdateMusic(float dt)
        {
            if (music == null || music.clip == null) return;
            bool menu = GameMenu.Instance != null && GameMenu.Instance.CurrentMode == GameMenu.Mode.Main;
            bool want = canvasGo.activeSelf || menu;
            musicTarget = want ? 0.55f * GameSettings.MusicVolume : 0f;
            musicVol = Mathf.MoveTowards(musicVol, musicTarget, dt * (want ? 0.5f : 0.35f));
            music.volume = musicVol;
            if (want && !music.isPlaying) music.Play();
            if (!want && musicVol <= 0.001f && music.isPlaying)
            {
                music.Stop();
                if (AudioManager.Instance != null) AudioManager.Instance.SetFarmMusicPaused(false);
            }
        }

        private static Image Img(RectTransform rt, Color c)
        {
            var img = rt.GetComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
            return img;
        }

        private static TextMeshProUGUI Label(Transform parent, string name, float size, Color color, Vector2 anchor, Vector2 box)
        {
            var t = MenuKit.Text(parent, size, color, name);
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = box;
            t.alignment = TextAlignmentOptions.Center;
            Brand(t);
            return t;
        }

        private static void Brand(TMP_Text t)
        {
            var f = Loc.ArabicFont;
            if (f != null) t.font = f;
            t.raycastTarget = false;
        }
    }
}
