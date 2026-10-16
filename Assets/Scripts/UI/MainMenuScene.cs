using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Main-menu presentation in the style of modern farming sims: a slow cinematic camera circling the farm behind the
/// menu, the MAZRAA wordmark top-left, the menu column on the left (GameMenu's list, moved there in Main mode) and
/// "Your farms" save cards on the right (day, money, last saved for each of the 3 slots, active slot highlighted).
/// Only visible while GameMenu is in Main mode; the pause menu keeps the centred look.
/// Built in code: one canvas, flat Images and texts in the Mazraa palette, no textures (low RAM).
/// </summary>
[MazraaSystem(61)]
public class MainMenuScene : MonoBehaviour
{
    public static MainMenuScene Instance { get; private set; }
    public bool Showing { get; private set; }
    public bool PauseView { get; private set; }
    private TextMeshProUGUI overview;
    private Image overviewBg;
    private float overviewTimer;

    private Canvas canvas;
    private TextMeshProUGUI brand, tagline, version, cardsTitle;
    private readonly TextMeshProUGUI[] cardText = new TextMeshProUGUI[SaveSlots.Count];
    private readonly Image[] cardBg = new Image[SaveSlots.Count];
    private readonly Image[] cardBar = new Image[SaveSlots.Count];
    private Camera menuCam;
    private float angle;
    private Vector3 focus;

    private void Awake() => Instance = this;
    private void OnDestroy() { if (Instance == this) Instance = null; Loc.OnLanguageChanged -= Refresh; }

    private void Start()
    {
        Build();
        Loc.OnLanguageChanged += Refresh;
    }

    private void Build()
    {
        canvas = MenuKit.Canvas("Main Menu Scene UI", transform, 91);

        // Bottom and left shading so text reads over the moving farm.
        var left = MenuKit.Rect("Left Shade", canvas.transform, typeof(Image));
        left.anchorMin = new Vector2(0f, 0f); left.anchorMax = new Vector2(0.42f, 1f);
        left.offsetMin = left.offsetMax = Vector2.zero;
        Img(left, new Color(0.04f, 0.07f, 0.04f, 0.55f));
        var bottom = MenuKit.Rect("Bottom Bar", canvas.transform, typeof(Image));
        bottom.anchorMin = new Vector2(0f, 0f); bottom.anchorMax = new Vector2(1f, 0f);
        bottom.pivot = new Vector2(0.5f, 0f);
        bottom.sizeDelta = new Vector2(0f, 56f);
        Img(bottom, new Color(0.09f, 0.2f, 0.11f, 0.92f));
        var stripe = MenuKit.Rect("Gold Stripe", bottom, typeof(Image));
        stripe.anchorMin = new Vector2(0f, 1f); stripe.anchorMax = new Vector2(1f, 1f);
        stripe.pivot = new Vector2(0.5f, 1f);
        stripe.sizeDelta = new Vector2(0f, 3f);
        Img(stripe, ControlsTutorial.Gold);

        brand = Label(canvas.transform, "Brand", 76, ControlsTutorial.Sand, new Vector2(0f, 1f), new Vector2(64f, -48f), new Vector2(700f, 100f));
        tagline = Label(canvas.transform, "Tagline", 24, new Color(1f, 1f, 1f, 0.8f), new Vector2(0f, 1f), new Vector2(68f, -140f), new Vector2(700f, 36f));
        version = Label(canvas.transform, "Version", 16, new Color(1f, 1f, 1f, 0.6f), new Vector2(0f, 0f), new Vector2(24f, 18f), new Vector2(700f, 24f));

        cardsTitle = Label(canvas.transform, "Farms Title", 26, ControlsTutorial.Sand, new Vector2(1f, 1f), new Vector2(-64f, -170f), new Vector2(460f, 36f));
        for (int i = 0; i < SaveSlots.Count; i++)
        {
            var card = MenuKit.Rect("Farm Card " + (i + 1), canvas.transform, typeof(Image));
            card.anchorMin = card.anchorMax = new Vector2(1f, 1f);
            card.pivot = new Vector2(1f, 1f);
            card.sizeDelta = new Vector2(460f, 118f);
            card.anchoredPosition = new Vector2(-64f, -214f - i * 132f);
            cardBg[i] = Img(card, ControlsTutorial.GreenDark);
            var bar = MenuKit.Rect("Bar", card, typeof(Image));
            bar.anchorMin = new Vector2(0f, 0f); bar.anchorMax = new Vector2(0f, 1f);
            bar.pivot = new Vector2(0f, 0.5f);
            bar.sizeDelta = new Vector2(8f, 0f);
            cardBar[i] = Img(bar, ControlsTutorial.Gold);
            cardText[i] = MenuKit.Text(card, 21, Color.white, "Text");
            var rt = cardText[i].rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(26f, 12f); rt.offsetMax = new Vector2(-18f, -12f);
            Brand(cardText[i]);
        }
        // Pause menu: live farm overview on the right.
        var ov = MenuKit.Rect("Farm Overview", canvas.transform, typeof(Image));
        ov.anchorMin = ov.anchorMax = ov.pivot = new Vector2(1f, 1f);
        ov.sizeDelta = new Vector2(520f, 560f);
        ov.anchoredPosition = new Vector2(-64f, -170f);
        overviewBg = Img(ov, ControlsTutorial.GreenDark);
        overview = MenuKit.Text(ov, 22, Color.white, "Text");
        var ort = overview.rectTransform;
        ort.anchorMin = Vector2.zero; ort.anchorMax = Vector2.one;
        ort.offsetMin = new Vector2(28f, 20f); ort.offsetMax = new Vector2(-24f, -20f);
        Brand(overview);
        foreach (var t in new[] { brand, tagline, version, cardsTitle }) Brand(t);
        canvas.gameObject.SetActive(false);
    }

    private static Image Img(RectTransform rt, Color c)
    {
        var img = rt.GetComponent<Image>();
        img.color = c;
        img.raycastTarget = false;
        return img;
    }

    private static TextMeshProUGUI Label(Transform parent, string name, float size, Color color, Vector2 anchor, Vector2 pos, Vector2 box)
    {
        var t = MenuKit.Text(parent, size, color, name);
        var rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = box;
        return t;
    }

    private static void Brand(TMP_Text t)
    {
        var f = Loc.ArabicFont;          // Tajawal Bold: Mazraa's font in both languages
        if (f != null) t.font = f;
        t.raycastTarget = false;
    }

    // ------------------------------------------------------------------ show / hide

    private void LateUpdate()
    {
        var mode = GameMenu.Instance != null ? GameMenu.Instance.CurrentMode : GameMenu.Mode.Closed;
        bool want = mode != GameMenu.Mode.Closed;
        bool pause = mode == GameMenu.Mode.Pause;
        if (want != Showing || pause != PauseView) { if (Showing) SetShowing(false); PauseView = pause; if (want) SetShowing(true); }
        if (Showing && PauseView) { overviewTimer -= Time.unscaledDeltaTime; if (overviewTimer <= 0f) { overviewTimer = 0.5f; overview.text = OverviewText(); } }
        if (!Showing || PauseView || menuCam == null) return;
        // Slow orbit around the farm (unscaled: the game is paused under the menu).
        angle += Time.unscaledDeltaTime * 3.5f;
        Quaternion rot = Quaternion.Euler(0f, angle, 0f);
        Vector3 pos = focus + rot * new Vector3(0f, 22f, -48f);
        menuCam.transform.position = pos;
        menuCam.transform.LookAt(focus + Vector3.up * 2f);
    }

    private void SetShowing(bool on)
    {
        Showing = on;
        if (canvas != null) canvas.gameObject.SetActive(on);
        HideGameUi(on);
        if (on)
        {
            SetPauseLayout(PauseView);
            focus = CozyFx.FarmStart;
            if (PauseView) { Refresh(); return; }       // pause: the frozen game stays behind, no orbit camera
            if (menuCam == null)
            {
                var go = new GameObject("Main Menu Camera");
                menuCam = go.AddComponent<Camera>();
                menuCam.fieldOfView = 50f;
                menuCam.nearClipPlane = 0.3f;
                menuCam.farClipPlane = 1500f;
                menuCam.depth = 50f;              // draws over the player camera while the menu is up
                GraphicsEnhancer.ConfigureCamera(menuCam);
            }
            menuCam.gameObject.SetActive(true);
            Refresh();
        }
        else if (menuCam != null) menuCam.gameObject.SetActive(false);
    }

    private void SetPauseLayout(bool pause)
    {
        if (overviewBg == null) return;
        overviewBg.gameObject.SetActive(pause);
        cardsTitle.gameObject.SetActive(!pause);
        for (int i = 0; i < SaveSlots.Count; i++) if (cardBg[i] != null) cardBg[i].gameObject.SetActive(!pause);
        if (pause) overview.text = OverviewText();
    }

    /// <summary>Pause-menu overview: date/time/weather, money, campaign chapter and current mission.</summary>
    public static string OverviewText()
    {
        var sb = new StringBuilder(512);
        sb.Append("<size=120%><b><color=#E9D8B4>").Append(Loc.T("Farm overview")).Append("</color></b></size>\n\n");
        var clock = WorldClock.Instance;
        if (clock != null)
            sb.Append(Loc.T("Day")).Append(' ').Append(clock.Day).Append("  -  ").Append(Loc.Display(clock.SeasonName())).Append("  -  ")
              .Append(clock.TimeText()).Append("  -  ").Append(Loc.Display(clock.WeatherName())).Append('\n');
        if (EconomyManager.Instance != null)
            sb.Append(Loc.T("Money")).Append(": <color=#9BE07A>").Append(EconomyManager.Instance.Money.ToString("N0")).Append(' ').Append(Loc.T("SAR")).Append("</color>\n");
        var cm = CampaignManager.Instance;
        if (cm != null && cm.Chapters.Length > 0)
        {
            int ch = cm.CurrentChapter;
            sb.Append("\n<color=#FAC240><b>").Append(Loc.T("CHAPTER")).Append(' ').Append(ch + 1).Append(": ")
              .Append(Loc.Display(CampaignManager.Title(cm.Chapters[ch]))).Append("</b></color>  ")
              .Append(Mathf.RoundToInt(cm.ChapterProgress(ch) * 100f)).Append("%\n");
        }
        var mm = MissionManager.Instance;
        if (mm != null && mm.Current != null)
            sb.Append("<b>").Append(Loc.Display(MissionManager.Title(mm.Current))).Append("</b>\n<size=90%>")
              .Append(Loc.Display(MissionManager.Desc(mm.Current))).Append("</size>\n");
        else if (mm != null && mm.AllDone) sb.Append(Loc.T("Campaign complete!")).Append('\n');
        return sb.ToString();
    }

    // The farm HUD, minimap and hints stay hidden behind the main menu (restored when it closes).
    private readonly System.Collections.Generic.List<Canvas> hidden = new System.Collections.Generic.List<Canvas>();
    private void HideGameUi(bool hide)
    {
        if (hide)
        {
            hidden.Clear();
            Canvas menu = GameMenu.Instance != null ? GameMenu.Instance.MenuCanvas : null;
            foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (c == null || c == canvas || c == menu || !c.enabled || !c.isRootCanvas) continue;
                if (c.renderMode == RenderMode.WorldSpace) continue;
                c.enabled = false;
                hidden.Add(c);
            }
        }
        else
        {
            foreach (var c in hidden) if (c != null) c.enabled = true;
            hidden.Clear();
        }
    }

    // ------------------------------------------------------------------ text

    public void Refresh()
    {
        if (brand == null) return;
        bool ar = Loc.IsArabic;
        string arName = ArabicShaper.Shape("مزرعة");   // always shaped, also in English
        brand.text = ar ? arName + "  <size=55%><color=#FAC240>MAZRAA</color></size>" : "MAZRAA  <size=55%><color=#FAC240>" + arName + "</color></size>";
        tagline.text = Loc.T("Grow, trade and build your farm in Saudi Arabia");
        version.text = "Almadarek Games  -  " + Loc.T("Early access");
        cardsTitle.text = Loc.T("Your farms");
        for (int i = 0; i < SaveSlots.Count; i++) cardText[i].text = CardText(i + 1);
        for (int i = 0; i < SaveSlots.Count; i++)
        {
            bool has = SaveSlots.HasSlot(i + 1);
            bool active = SaveSlots.ActiveSlot == i + 1;
            cardBg[i].color = has ? ControlsTutorial.GreenDark : new Color(0f, 0f, 0f, 0.45f);
            cardBar[i].color = active ? ControlsTutorial.Gold : has ? ControlsTutorial.Sand : new Color(1f, 1f, 1f, 0.15f);
            cardText[i].alignment = ar ? TextAlignmentOptions.TopRight : TextAlignmentOptions.TopLeft;
        }
        brand.alignment = tagline.alignment = ar ? TextAlignmentOptions.Left : TextAlignmentOptions.Left;
    }

    /// <summary>Text of one farm card (slot 1-3).</summary>
    public static string CardText(int slot)
    {
        var sb = new StringBuilder(128);
        int day, money; string saved;
        sb.Append("<b>").Append(Loc.T("Farm")).Append(' ').Append(slot).Append("</b>");
        if (!SaveSlots.Info(slot, out day, out money, out saved))
        {
            sb.Append("\n<color=#FFFFFF99>").Append(Loc.T("Empty - choose New game to start a farm")).Append("</color>");
            return sb.ToString();
        }
        if (SaveSlots.ActiveSlot == slot) sb.Append("   <size=75%><color=#FAC240>").Append(Loc.T("Playing")).Append("</color></size>");
        sb.Append("\n<color=#E9D8B4>").Append(Loc.T("Day")).Append(' ').Append(day).Append("</color>   <color=#9BE07A>")
          .Append(money.ToString("N0")).Append(' ').Append(Loc.T("SAR")).Append("</color>");
        if (!string.IsNullOrEmpty(saved)) sb.Append("\n<size=80%><color=#FFFFFFAA>").Append(Loc.T("Saved")).Append(' ').Append(saved).Append("</color></size>");
        return sb.ToString();
    }
}
