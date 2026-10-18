using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Front-end presentation in the style of modern farming sims (Mazraa's own look: palm green, sand and gold, Tajawal):
/// a slow cinematic camera circling the farm behind every front-end screen, and per screen (GameMenu.Front):
///  - Title: the big MAZRAA logo in the middle, "Press any key / A to start", studio and version at the bottom.
///  - Main menu: the logo top-left over the menu column, and on the right a news / tips panel (rotating tips, the
///    last-played farm) and the "Egypt: Nile Delta" DLC card (owned / not owned).
///  - Career: details of the selected slot (or what an empty slot does); new-farm wizard: summary of the choices.
///  - Multiplayer: how co-op works (or that it is coming in a future update).
/// The pause menu shows the live farm overview on the right instead. Built in code: one canvas, flat Images and texts,
/// no textures except the logo (low RAM).
/// </summary>
[MazraaSystem(61)]
public class MainMenuScene : MonoBehaviour
{
    public const float TipSeconds = 7f;
    public static MainMenuScene Instance { get; private set; }
    public bool Showing { get; private set; }
    public bool PauseView { get; private set; }
    /// <summary>Front-end screen laid out right now.</summary>
    public GameMenu.FrontScreen Layout { get; private set; } = GameMenu.FrontScreen.None;

    // texts (also read by the tests)
    public string PromptText => prompt != null && prompt.gameObject.activeSelf ? prompt.text : "";
    public string NewsText => news != null && newsBg.gameObject.activeSelf ? news.text : "";
    public string DlcText => dlc != null && dlcBg.gameObject.activeSelf ? dlc.text : "";
    public string InfoText => info != null && infoBg.gameObject.activeSelf ? info.text : "";
    public string VersionText => version != null ? version.text : "";
    public bool LogoCentered { get; private set; }

    private Canvas canvas;
    private RawImage logo;
    private RectTransform leftShade;
    private TextMeshProUGUI brand, tagline, version, prompt, news, dlc, info, overview;
    private Image newsBg, dlcBg, dlcBar, infoBg, overviewBg;
    private float overviewTimer, textTimer, tipTimer, pulse;
    private int tipIndex;
    private Camera menuCam;
    private float angle;
    private Vector3 focus;

    private void Awake() => Instance = this;
    private void OnDestroy() { if (Instance == this) Instance = null; Loc.OnLanguageChanged -= Refresh; }

    private void Start()
    {
        Build();
        tipIndex = Random.Range(0, LoadingScreen.Tips.Length);
        Loc.OnLanguageChanged += Refresh;
    }

    private void Build()
    {
        canvas = MenuKit.Canvas("Main Menu Scene UI", transform, 91);

        // Shading so text reads over the moving farm: a column on the reading side and a palm-green bottom bar.
        leftShade = MenuKit.Rect("Left Shade", canvas.transform, typeof(Image));
        leftShade.anchorMin = new Vector2(0f, 0f); leftShade.anchorMax = new Vector2(0.42f, 1f);
        leftShade.offsetMin = leftShade.offsetMax = Vector2.zero;
        Img(leftShade, new Color(0.04f, 0.07f, 0.04f, 0.55f));
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
        // Logo image (English / Arabic lockup, 512 px each); the text wordmark stays as a fallback.
        var lr = MenuKit.Rect("Logo", canvas.transform, typeof(RawImage));
        logo = lr.GetComponent<RawImage>();
        logo.raycastTarget = false;
        tagline = Label(canvas.transform, "Tagline", 24, new Color(1f, 1f, 1f, 0.8f), new Vector2(0f, 1f), new Vector2(68f, -140f), new Vector2(700f, 36f));
        version = Label(canvas.transform, "Version", 16, new Color(1f, 1f, 1f, 0.7f), new Vector2(0f, 0f), new Vector2(24f, 16f), new Vector2(1200f, 24f));
        prompt = Label(canvas.transform, "Press Start", 34, ControlsTutorial.Sand, new Vector2(0.5f, 0f), new Vector2(0f, 120f), new Vector2(1000f, 50f));
        prompt.alignment = TextAlignmentOptions.Center;

        // Right side: news / tips, the DLC card, and an info panel (Career slot, wizard summary, multiplayer).
        newsBg = Panel("News", new Vector2(-64f, -170f), new Vector2(520f, 330f), out news, 21);
        dlcBg = Panel("DLC Card", new Vector2(-64f, -516f), new Vector2(520f, 130f), out dlc, 21);
        var bar = MenuKit.Rect("Flag", dlcBg.rectTransform, typeof(Image));
        bar.anchorMin = new Vector2(0f, 0f); bar.anchorMax = new Vector2(0f, 1f); bar.pivot = new Vector2(0f, 0.5f);
        bar.sizeDelta = new Vector2(10f, 0f);
        dlcBar = Img(bar, GameMenu.FlagColor("nile_delta"));
        infoBg = Panel("Info", new Vector2(-64f, -170f), new Vector2(520f, 480f), out info, 22);
        // Pause menu: live farm overview on the right.
        overviewBg = Panel("Farm Overview", new Vector2(-64f, -170f), new Vector2(520f, 560f), out overview, 22);
        foreach (var t in new[] { brand, tagline, version, prompt }) Brand(t);
        canvas.gameObject.SetActive(false);
    }

    private Image Panel(string name, Vector2 pos, Vector2 size, out TextMeshProUGUI text, float fontSize)
    {
        var rt = MenuKit.Rect(name, canvas.transform, typeof(Image));
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
        var bg = Img(rt, ControlsTutorial.GreenDark);
        text = MenuKit.Text(rt, fontSize, Color.white, "Text");
        var trt = text.rectTransform;
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(28f, 18f); trt.offsetMax = new Vector2(-24f, -18f);
        Brand(text);
        return bg;
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
        var gm = GameMenu.Instance;
        var mode = gm != null ? gm.CurrentMode : GameMenu.Mode.Closed;
        bool want = mode != GameMenu.Mode.Closed;
        bool pause = mode == GameMenu.Mode.Pause;
        if (want != Showing || pause != PauseView) { if (Showing) SetShowing(false); PauseView = pause; if (want) SetShowing(true); }
        if (!Showing) return;
        if (PauseView) { overviewTimer -= Time.unscaledDeltaTime; if (overviewTimer <= 0f) { overviewTimer = 0.5f; overview.text = OverviewText(); } return; }

        var screen = gm != null ? gm.Front : GameMenu.FrontScreen.Main;
        if (screen != Layout) { Layout = screen; ApplyLayout(); Refresh(); }
        float dt = Time.unscaledDeltaTime;
        tipTimer -= dt;
        if (tipTimer <= 0f) { tipTimer = TipSeconds; tipIndex = (tipIndex + 1) % LoadingScreen.Tips.Length; textTimer = 0f; }
        textTimer -= dt;
        if (textTimer <= 0f) { textTimer = 0.2f; RefreshTexts(); }
        if (Layout == GameMenu.FrontScreen.Title && prompt != null)
        {
            // "Press any key" breathes gently (still in Reduce motion: just steady).
            pulse += dt;
            var c = prompt.color; c.a = GameSettings.ReduceMotion ? 1f : 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(pulse * 2f)); prompt.color = c;
        }
        if (menuCam == null) return;
        // Slow orbit around the farm (unscaled: the game is paused under the menu).
        angle += dt * 3.5f;
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
            Layout = PauseView ? GameMenu.FrontScreen.None : GameMenu.Instance != null ? GameMenu.Instance.Front : GameMenu.FrontScreen.Main;
            ApplyLayout();
            focus = CozyFx.FarmStart;
            if (PauseView) { if (overview != null) overview.text = OverviewText(); Refresh(); return; }   // pause: the frozen game stays behind, no orbit camera
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

    /// <summary>Show the pieces of the current screen.</summary>
    private void ApplyLayout()
    {
        if (overviewBg == null) return;
        var s = Layout;
        bool front = !PauseView;
        bool title = front && s == GameMenu.FrontScreen.Title;
        overviewBg.gameObject.SetActive(PauseView);
        leftShade.gameObject.SetActive(!title);
        prompt.gameObject.SetActive(title);
        tagline.gameObject.SetActive(front);
        newsBg.gameObject.SetActive(front && s == GameMenu.FrontScreen.Main);
        dlcBg.gameObject.SetActive(front && s == GameMenu.FrontScreen.Main);
        infoBg.gameObject.SetActive(front && (s == GameMenu.FrontScreen.Career || s == GameMenu.FrontScreen.NewFarm || s == GameMenu.FrontScreen.Multiplayer));
        LogoCentered = title;
        // Title: big centred logo; elsewhere top-left over the menu column.
        var lr = logo.rectTransform;
        var anchor = title ? new Vector2(0.5f, 0.62f) : new Vector2(0f, 1f);
        lr.anchorMin = lr.anchorMax = lr.pivot = anchor;
        lr.anchoredPosition = title ? Vector2.zero : new Vector2(48f, -24f);
        var brt = brand.rectTransform;
        brt.anchorMin = brt.anchorMax = brt.pivot = anchor;
        brt.anchoredPosition = title ? Vector2.zero : new Vector2(64f, -48f);
        brt.sizeDelta = title ? new Vector2(1200f, 160f) : new Vector2(700f, 100f);
        brand.fontSize = title ? 120 : 76;
        brand.alignment = title ? TextAlignmentOptions.Center : TextAlignmentOptions.Left;
        var trt = tagline.rectTransform;
        trt.anchorMin = trt.anchorMax = trt.pivot = title ? new Vector2(0.5f, 0.38f) : new Vector2(0f, 1f);
        trt.anchoredPosition = title ? Vector2.zero : new Vector2(68f, -140f);
        tagline.alignment = title ? TextAlignmentOptions.Center : TextAlignmentOptions.Left;
        tipTimer = TipSeconds;
    }

    /// <summary>Pause-menu overview: date/time/weather, money, play mode, campaign chapter and current mission.</summary>
    public static string OverviewText()
    {
        var sb = new StringBuilder(512);
        sb.Append("<size=120%><b><color=#E9D8B4>").Append(Loc.T("Farm overview")).Append("</color></b></size>\n");
        sb.Append("<size=85%><color=#FFFFFFAA>").Append(Loc.Display(GameModes.CurrentFarmNameRaw + "  -  " + Loc.Raw(GameModes.ModeName(GameModes.Current)))).Append("</color></size>\n\n");
        var clock = WorldClock.Instance;
        if (clock != null)
            sb.Append(Loc.T("Day")).Append(' ').Append(clock.Day).Append("  -  ").Append(Loc.Display(clock.SeasonName())).Append("  -  ")
              .Append(clock.TimeText()).Append("  -  ").Append(Loc.Display(clock.WeatherName())).Append('\n');
        if (EconomyManager.Instance != null)
            sb.Append(Loc.T("Money")).Append(": <color=#9BE07A>").Append(EconomyManager.Instance.Money.ToString("N0")).Append(' ').Append(Loc.T("SAR")).Append("</color>\n");
        var ch0 = GameModes.CurrentChallenge;
        if (ch0 != null) sb.Append("\n<color=#FAC240><b>").Append(Loc.T(ch0.name)).Append("</b></color>\n<size=90%>").Append(GameModes.GoalText(ch0)).Append("</size>\n");
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
        else if (mm != null && mm.AllDone && GameModes.HasStory) sb.Append(Loc.T("Campaign complete!")).Append('\n');
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
        var logoTex = Resources.Load<Texture2D>(ar ? "Branding/mazraa_logo_ar_512" : "Branding/mazraa_logo_en_512");
        if (logo != null)
        {
            logo.texture = logoTex;
            logo.enabled = logoTex != null;
            float w = LogoCentered ? 820f : 560f;
            if (logoTex != null) logo.rectTransform.sizeDelta = new Vector2(w, w * logoTex.height / Mathf.Max(1, logoTex.width));
        }
        brand.enabled = logoTex == null;
        string arName = ArabicShaper.Shape("مزرعة");   // always shaped, also in English
        brand.text = ar ? arName + "  <size=55%><color=#FAC240>MAZRAA</color></size>" : "MAZRAA  <size=55%><color=#FAC240>" + arName + "</color></size>";
        tagline.text = Loc.T("Grow, trade and build your farm");
        version.text = "Almadarek Games  -  " + Loc.T("Early access") + "  -  v" + Application.version;
        version.alignment = ar ? TextAlignmentOptions.Right : TextAlignmentOptions.Left;
        RefreshTexts();
    }

    private void RefreshTexts()
    {
        if (news == null) return;
        bool ar = Loc.IsArabic;
        var align = ar ? TextAlignmentOptions.TopRight : TextAlignmentOptions.TopLeft;
        news.alignment = dlc.alignment = info.alignment = overview.alignment = align;
        prompt.text = GameInput.UsingGamepad ? Glyphs.Apply(Loc.T("Press [Enter] to start")) : Loc.T("Press any key to start");
        switch (Layout)
        {
            case GameMenu.FrontScreen.Main: news.text = NewsPanelText(tipIndex); dlc.text = DlcCardText(); dlcBar.color = Dlc.Owns("nile_delta") ? GameMenu.FlagColor("nile_delta") : new Color(1f, 1f, 1f, 0.3f); break;
            case GameMenu.FrontScreen.Career: info.text = CareerInfo(); break;
            case GameMenu.FrontScreen.NewFarm: info.text = GameMenu.Instance != null ? GameMenu.Instance.WizardSummary() : ""; break;
            case GameMenu.FrontScreen.Multiplayer: info.text = MultiplayerInfo(); break;
        }
    }

    /// <summary>News / tips panel of the main menu: a rotating tip and the last-played farm.</summary>
    public static string NewsPanelText(int tip)
    {
        var sb = new StringBuilder(512);
        sb.Append("<size=115%><b><color=#E9D8B4>").Append(Loc.T("News and tips")).Append("</color></b></size>\n\n");
        sb.Append("<color=#FAC240><b>").Append(Loc.T("Tip")).Append("</b></color>  ").Append(Loc.T(LoadingScreen.Tips[((tip % LoadingScreen.Tips.Length) + LoadingScreen.Tips.Length) % LoadingScreen.Tips.Length])).Append("\n\n");
        sb.Append("<color=#FAC240><b>").Append(Loc.T("Last played")).Append("</b></color>\n");
        int slot = SaveSlots.ActiveSlot;
        SaveSlots.Summary s;
        if (slot > 0 && SaveSlots.Info(slot, out s))
        {
            sb.Append(Loc.Display(s.FarmNameRaw + "  -  " + Regions.Name(s.Map))).Append('\n')
              .Append("<size=90%>").Append(Loc.Display(Loc.Raw("Day") + " " + s.day + "  -  " + s.money.ToString("N0") + "  -  " + Loc.Raw(GameModes.ModeName(s.mode)))).Append("</size>");
        }
        else if (SaveData.HasKey("mazraa.money"))
            sb.Append(GameModes.CurrentFarmName).Append("\n<size=90%>").Append(Loc.Display(Loc.Raw("Day") + " " + (WorldClock.Instance != null ? WorldClock.Instance.Day : 1))).Append("</size>");
        else sb.Append("<size=90%>").Append(Loc.T("No farm yet - open Career to start one")).Append("</size>");
        return sb.ToString();
    }

    /// <summary>DLC card of the main menu: Egypt's Nile Delta, owned or not.</summary>
    public static string DlcCardText()
    {
        bool owned = Dlc.Owns("nile_delta");
        return "<b>" + Loc.T("Egypt: Nile Delta DLC") + "</b>   <size=80%>" + (owned ? "<color=#9BE07A>" + Loc.T("Owned") + "</color>" : "<color=#FFB070>" + Loc.T("Not owned") + "</color>")
             + "</size>\n<size=85%><color=#FFFFFFCC>" + Loc.T("Rice, cotton and berseem on the canals of the Delta, with its own story.") + "</color></size>";
    }

    /// <summary>Career: the selected slot in detail (or what an empty slot does).</summary>
    private static string CareerInfo()
    {
        var gm = GameMenu.Instance;
        if (gm == null) return "";
        int slot = gm.PageName == "Career" ? gm.List.Selected + 1 : gm.SelectedSlot;
        if (slot < 1 || slot > SaveSlots.Count) return "<b><color=#E9D8B4>" + Loc.T("Career") + "</color></b>\n\n" + Loc.T("Choose a slot: a saved farm to load or delete, an empty one to start a new farm.");
        var sb = new StringBuilder(512);
        SaveSlots.Summary s;
        if (!SaveSlots.Info(slot, out s))
        {
            sb.Append("<b><color=#E9D8B4>").Append(Loc.Display(Loc.Raw("Farm") + " " + slot)).Append("</color></b>\n\n").Append(Loc.T("Empty slot - start a new farm")).Append("\n\n")
              .Append("<size=90%>").Append(Loc.T("Campaign, free play or a challenge, on any map you own.")).Append("</size>");
            return sb.ToString();
        }
        var map = s.Map;
        sb.Append("<size=115%><b><color=#E9D8B4>").Append(s.FarmName).Append("</color></b></size>\n");
        sb.Append(Loc.Display(Regions.Name(map) + " - " + Regions.Country(map))).Append('\n');
        sb.Append("<color=#FAC240>").Append(Loc.T(GameModes.ModeName(s.mode))).Append("</color>\n\n");
        sb.Append(Loc.T("Day")).Append(' ').Append(s.day).Append('\n');
        sb.Append(Loc.T("Money")).Append(": <color=#9BE07A>").Append(s.money.ToString("N0")).Append("</color>\n");
        sb.Append(Loc.T("Play time")).Append(": ").Append(GameModes.FormatPlayTime(s.playSeconds)).Append('\n');
        if (!string.IsNullOrEmpty(s.saved)) sb.Append(Loc.T("Last saved")).Append(": ").Append(s.saved).Append('\n');
        return sb.ToString();
    }

    private static string MultiplayerInfo()
    {
        var sb = new StringBuilder(256);
        sb.Append("<b><color=#E9D8B4>").Append(Loc.T("Multiplayer")).Append("</color></b>\n\n");
        var mp = MultiplayerManager.Instance;
        if (!GameMenu.MultiplayerAvailable || mp == null)
        {
            sb.Append(Loc.T("Multiplayer is coming in a future update")).Append("\n\n<size=90%>").Append(Loc.T("Farm together with up to 3 friends - stay tuned!")).Append("</size>");
            return sb.ToString();
        }
        if (NetSession.IsHost && mp.IsOnlineSession)
        {
            // The join code, big: friends read it off the screen (Multiplayer > Join with a code).
            sb.Append(Loc.T("Join code")).Append("\n<size=260%><b><color=#FAC240>").Append(JoinCodes.Pretty(mp.JoinCode)).Append("</color></b></size>\n\n");
            sb.Append(Loc.Display(Loc.Raw("Players") + ": " + mp.Players.Count + "/" + MultiplayerManager.MaxPlayers)).Append('\n');
            sb.Append(Loc.Display(Loc.Raw("Who can join") + ": " + Loc.Raw(MultiplayerManager.VisibilityLabel(MultiplayerManager.Visibility)))).Append('\n');
            sb.Append(Loc.Display(Loc.Raw("Chat") + ": " + Loc.Raw(ChatSafety.ChatModeLabel(ChatSafety.Chat)))).Append("\n\n<size=85%>");
            sb.Append(Loc.T("Only share the code with people you know. You can remove or ban players under Players.")).Append("</size>");
            return sb.ToString();
        }
        if (NetSession.IsOnline)
        {
            sb.Append(Loc.T(NetSession.IsHost ? "You are hosting - friends join with your IP address" : "You are visiting a friend's farm")).Append("\n\n");
            foreach (var kv in mp.Players) sb.Append("- ").Append(Mp.Name(kv.Value)).Append('\n');
            return sb.ToString();
        }
        if (mp.Busy != MultiplayerManager.OnlineStep.Idle || !string.IsNullOrEmpty(mp.Status))
            sb.Append("<color=#FAC240>").Append(Mp.Tr(mp.Status)).Append("</color>\n\n");
        if (GameMenu.OnlineAvailable)
            sb.Append(Loc.T("Online: host your farm and give friends the 6-letter join code, or join with a friend's code. Public farms can be browsed."))
              .Append("\n\n");
        sb.Append(Loc.T("Host your farm, or join a friend's farm with their IP address.")).Append("\n<size=90%>")
          .Append(Loc.T("Port 7777 (UDP) - allow the game in your firewall")).Append("</size>\n\n<size=85%>")
          .Append(Loc.Display(Loc.Raw("Your name") + ": " + PlayerNames.Display(PlayerNames.Current))).Append("</size>");
        return sb.ToString();
    }

    /// <summary>Text of one farm card (slot 1-3).</summary>
    public static string CardText(int slot) => GameMenu.SlotCard(slot);
}
