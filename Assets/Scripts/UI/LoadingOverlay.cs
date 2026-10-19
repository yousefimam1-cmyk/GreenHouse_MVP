using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>The loading screen's canvas and music (one object, DontDestroyOnLoad, so it survives the scene reload when a save loads).</summary>
public class LoadingOverlay : MonoBehaviour
{
    internal GameObject canvasGo;
    internal AudioSource music;
    internal TextMeshProUGUI tip, status, title;
    internal float progress;
    private RectTransform bar;
    private CanvasGroup group;
    private RawImage logo;
    private readonly RawImage[] art = new RawImage[4];   // sky, far, mid, fg (Resources/Branding/Loading, Branding/make_brand_kit.py)
    private RectTransform sand, dune;
    private string artRegion;
    private static readonly string[] ArtLayers = { "sky", "far", "mid", "fg" };
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
        Img(bg, UiTheme.Scrim);
        // Golden-hour farm illustration in parallax layers (slightly oversized so they can drift). Missing files: flat bands below.
        for (int i = 0; i < art.Length; i++)
        {
            var lr0 = MenuKit.Rect("Art " + ArtLayers[i], canvas.transform, typeof(RawImage));
            lr0.anchorMin = new Vector2(-0.03f, -0.03f); lr0.anchorMax = new Vector2(1.03f, 1.03f);
            lr0.offsetMin = lr0.offsetMax = Vector2.zero;
            art[i] = lr0.GetComponent<RawImage>();
            art[i].raycastTarget = false;
            art[i].enabled = false;
        }
        // Sand horizon band with a gold rule: reads as desert farmland at sunset behind the logo.
        sand = MenuKit.Rect("Sand", canvas.transform, typeof(Image));
        sand.anchorMin = new Vector2(0f, 0f); sand.anchorMax = new Vector2(1f, 0.3f);
        sand.offsetMin = sand.offsetMax = Vector2.zero;
        Img(sand, new Color(0.62f, 0.5f, 0.32f, 1f));
        dune = MenuKit.Rect("Dune", canvas.transform, typeof(Image));
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
        var trackImg = Img(track, new Color(0f, 0f, 0f, 0.45f));
        UiKit.Sliced(trackImg, "bar_track");
        bar = MenuKit.Rect("Bar Fill", track, typeof(Image));
        bar.anchorMin = new Vector2(0f, 0f); bar.anchorMax = new Vector2(0f, 1f);
        bar.offsetMin = bar.offsetMax = Vector2.zero;
        var fillImg = Img(bar, ControlsTutorial.Gold);
        UiKit.Sliced(fillImg, "bar_fill");
        status = Label(canvas.transform, "Status", 26, Color.white, new Vector2(0.5f, 0.3f), new Vector2(900f, 40f));
        status.rectTransform.anchoredPosition = new Vector2(0f, 130f);

        // Tip box
        var box = MenuKit.Rect("Tip Box", canvas.transform, typeof(Image));
        box.anchorMin = box.anchorMax = box.pivot = new Vector2(0.5f, 0f);
        box.anchoredPosition = new Vector2(0f, 60f);
        box.sizeDelta = new Vector2(1100f, 120f);
        if (!UiKit.Sliced(box.GetComponent<Image>(), "tooltip")) Img(box, new Color(0.06f, 0.12f, 0.07f, 0.88f)); else box.GetComponent<Image>().raycastTarget = false;
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
        tipIndex = Random.Range(0, LoadingScreen.Tips.Length);
        if (group != null) group.alpha = 1f;
        canvasGo.SetActive(true);
        Refresh();
        NextTip();
        if (music != null && music.clip != null && !music.isPlaying) { music.volume = 0f; music.Play(); }
        musicVol = music != null ? music.volume : 0f;
        if (AudioManager.Instance != null) AudioManager.Instance.SetFarmMusicPaused(true);
    }

    internal void SceneReady() { ready = true; }

    /// <summary>Which illustration is shown ("najd", "delta") or "" for the flat bands.</summary>
    public string ArtRegion => artRegion ?? "";

    private void LoadArt()
    {
        string region = Regions.Current != null && Regions.Current.id == "nile_delta" ? "delta" : "najd";
        if (region == artRegion) return;
        artRegion = region;
        bool any = false;
        for (int i = 0; i < art.Length; i++)
        {
            var tex = Resources.Load<Texture2D>("Branding/Loading/" + region + "_" + ArtLayers[i]);
            art[i].texture = tex;
            art[i].enabled = tex != null;
            any |= tex != null;
        }
        if (!any) artRegion = "";
        if (sand != null) sand.gameObject.SetActive(!any);
        if (dune != null) dune.gameObject.SetActive(!any);
        // over the illustration the logo sits higher and smaller, the bar lower
        if (logo != null) logo.rectTransform.anchorMin = logo.rectTransform.anchorMax = logo.rectTransform.pivot = new Vector2(0.5f, any ? 0.7f : 0.62f);
    }

    private void Refresh()
    {
        bool ar = Loc.IsArabic;
        LoadArt();
        var tex = Resources.Load<Texture2D>(ar ? "Branding/mazraa_logo_ar_512" : "Branding/mazraa_logo_en_512");
        if (logo != null)
        {
            logo.texture = tex;
            logo.enabled = tex != null;
            float w = string.IsNullOrEmpty(artRegion) ? 760f : 640f;
            if (tex != null) logo.rectTransform.sizeDelta = new Vector2(w, w * tex.height / Mathf.Max(1, tex.width));
        }
        title.enabled = tex == null;
        title.text = ar ? ArabicShaper.Shape("مزرعة") : "MAZRAA";
    }

    private void NextTip()
    {
        tipIndex = (tipIndex + 1) % LoadingScreen.Tips.Length;
        tip.text = "<color=#FAC240><b>" + Loc.T("Tip") + "</b></color>   " + Loc.T(LoadingScreen.Tips[tipIndex]);
        tipTimer = 6f;
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        UpdateMusic(dt);
        if (!canvasGo.activeSelf) return;
        shown += dt;
        if (!GameSettings.ReduceMotion && !string.IsNullOrEmpty(artRegion))
        {
            // gentle parallax drift: far layers move less than the foreground
            float ph = shown * 0.25f;
            for (int i = 0; i < art.Length; i++)
                if (art[i] != null && art[i].enabled) art[i].rectTransform.anchoredPosition = new Vector2(Mathf.Sin(ph) * 6f * i, Mathf.Cos(ph * 0.7f) * 3f * i);
        }
        if (!ready && shown > 12f) ready = true;      // fail-safe: never stay stuck on the loading screen
        // Fill to 90% over the minimum time, the rest once the scene is ready.
        float target = Mathf.Clamp01(shown / LoadingScreen.MinSeconds) * 0.9f + (ready ? 0.1f : 0f);
        progress = Mathf.MoveTowards(progress, target, dt * 0.8f);
        bar.anchorMax = new Vector2(progress, 1f);
        status.text = (progress >= 0.999f ? Loc.T("Ready") : Loc.T("Loading your farm")) + "  " + Mathf.RoundToInt(progress * 100f) + "%";
        tipTimer -= dt;
        if (tipTimer <= 0f) NextTip();
        if (fade < 0f && ready && shown >= LoadingScreen.MinSeconds && progress >= 0.999f) fade = LoadingScreen.FadeSeconds;
        if (fade >= 0f)
        {
            fade -= dt;
            group.alpha = Mathf.Clamp01(fade / LoadingScreen.FadeSeconds);
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
