using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// "Mazraa Academy" - a short, branded controls tutorial: look, walk, sprint, jump, camera view, map, interact,
/// then get in the tractor, drive, brake and get out. Each step ticks off by itself when the player does it,
/// with a progress bar, step dots, a running timer and a medal at the end (gold / silver / bronze by time),
/// 150 SAR and a "Mazraa Academy" entry in the collection book (first time only).
/// Starts by itself the first time the farm is played (after the main menu closes) and from the menu
/// ("Controls tutorial"). [Enter] / D-pad right skips a step, [Backspace] / D-pad down closes.
/// UI is built in code (one canvas, a few Images and texts, no textures), text only rebuilt when something changes.
/// </summary>
[MazraaSystem(66)]
public class ControlsTutorial : MonoBehaviour
{
    public const string SeenKey = "mazraa.academy.seen";
    public const string DoneKey = "mazraa.academy.done";
    public const int Reward = 150;

    public enum StepId { Look, Walk, Sprint, Jump, View, Map, Interact, EnterVehicle, Drive, Brake, ExitVehicle }

    public static ControlsTutorial Instance { get; private set; }

    // Mazraa brand palette: palm green, desert sand, date brown, sun gold.
    public static readonly Color Green = UiTheme.Glass;          // design tokens (UiTheme)
    public static readonly Color GreenDark = UiTheme.GlassStrong;
    public static readonly Color Sand = UiTheme.Text;
    public static readonly Color Gold = UiTheme.Accent;
    private const string SandHex = "#E9D8B4", GoldHex = "#FAC240", BrownHex = "#5A3A1C", MintHex = "#9BE07A";

    // English Loc keys. [X] tokens become the current device's key / button through Glyphs.Apply.
    private static readonly string[] Titles =
    {
        "Look around", "Walk", "Sprint", "Jump", "Switch camera", "Open the map", "Interact",
        "Get in the tractor", "Drive", "Brake", "Get out",
    };
    private static readonly string[] Hints =
    {
        "Move the mouse (or right stick) to look around the farm.",
        "Walk 10 metres with W A S D (or the left stick).",
        "Hold Shift while walking to sprint (left stick click on a controller).",
        "Press Space to jump (A / Cross on a controller).",
        "Press [V] to switch between first and third person.",
        "Press [N] to open the map, then [N] again to close it.",
        "Look at anything with a prompt and press [E].",
        "Walk to the tractor and press [E] to drive it.",
        "Drive 25 metres. W / S to drive, A / D to steer.",
        "Press [Space] to brake.",
        "Press [E] to get out of the tractor.",
    };
    private static readonly string[] Cheers = { "Mashallah!", "Yalla, well done!", "Nice one!", "Like a pro!", "Ya salam!" };

    public bool Running { get; private set; }
    public StepId Step { get; private set; }
    public float Progress { get; private set; }   // 0..1 for the current step
    public float Elapsed { get; private set; }
    public int StepCount => hasVehicle ? Titles.Length : (int)StepId.EnterVehicle;

    private Canvas canvas;
    private RectTransform card, barFill;
    private TextMeshProUGUI header, body, footer;
    private Image[] dots;
    private StarterAssets.StarterAssetsInputs inputs;
    private Vector3 lastPos;
    private bool hasPos, hasVehicle, wasThird, stepDone;
    private float doneAt = -1f, lookSum, autoCheck = 2f, refreshTimer;
    private int shownSecond = -1, shownPercent = -1, cheer;
    private Vehicle tractor;
    private bool finished;
    private string medal;

    private void Awake() => Instance = this;
    private void OnDestroy() { if (Instance == this) Instance = null; }

    private void Start() => Build();

    // ------------------------------------------------------------------ UI

    private void Build()
    {
        canvas = MenuKit.Canvas("Mazraa Academy UI", transform, 46);
        card = MenuKit.Panel("Academy Card", canvas.transform, 640);
        var bg = card.GetComponent<Image>();
        bg.color = GreenDark;
        bg.raycastTarget = false;
        card.anchorMin = card.anchorMax = new Vector2(0.5f, 0f);
        card.pivot = new Vector2(0.5f, 0f);
        card.anchoredPosition = new Vector2(0f, 96f);

        // Sand accent strip on top of the card.
        var strip = MenuKit.Rect("Accent", card, typeof(Image));
        strip.GetComponent<Image>().color = Sand;
        strip.GetComponent<Image>().raycastTarget = false;
        strip.anchorMin = new Vector2(0f, 1f); strip.anchorMax = new Vector2(1f, 1f);
        strip.pivot = new Vector2(0.5f, 1f);
        strip.sizeDelta = new Vector2(0f, 6f);
        strip.anchoredPosition = Vector2.zero;

        header = MenuKit.Text(card, 20, Sand, "Header");
        body = MenuKit.Text(card, 24, Color.white, "Body");
        footer = MenuKit.Text(card, 16, new Color(1f, 1f, 1f, 0.7f), "Footer");
        foreach (var t in new[] { header, body, footer }) BrandFont(t);

        // Progress bar.
        var bar = MenuKit.Rect("Bar", card, typeof(Image));
        bar.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.35f);
        bar.GetComponent<Image>().raycastTarget = false;
        bar.anchorMin = new Vector2(0f, 0f); bar.anchorMax = new Vector2(1f, 0f);
        bar.pivot = new Vector2(0.5f, 0f);
        bar.sizeDelta = new Vector2(-40f, 8f);
        bar.anchoredPosition = new Vector2(0f, 44f);
        barFill = MenuKit.Rect("Fill", bar, typeof(Image));
        barFill.GetComponent<Image>().color = Gold;
        barFill.GetComponent<Image>().raycastTarget = false;
        barFill.anchorMin = Vector2.zero; barFill.anchorMax = new Vector2(0f, 1f);
        barFill.pivot = new Vector2(0f, 0.5f);
        barFill.sizeDelta = Vector2.zero;

        // Step dots.
        dots = new Image[Titles.Length];
        for (int i = 0; i < dots.Length; i++)
        {
            var d = MenuKit.Rect("Dot" + i, card, typeof(Image));
            dots[i] = d.GetComponent<Image>();
            dots[i].raycastTarget = false;
            d.anchorMin = d.anchorMax = new Vector2(0.5f, 0f);
            d.sizeDelta = new Vector2(10f, 10f);
            d.anchoredPosition = new Vector2((i - (dots.Length - 1) * 0.5f) * 18f, 24f);
        }
        canvas.gameObject.SetActive(false);
    }

    private static void BrandFont(TMP_Text t)
    {
        if (t == null) return;
        var f = Loc.ArabicFont;            // Tajawal Bold has Latin and Arabic: Mazraa's brand font in both languages.
        if (f != null) t.font = f;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.Normal;
    }

    // ------------------------------------------------------------------ flow

    /// <summary>Start (or restart) from the first step.</summary>
    public void Begin()
    {
        if (canvas == null) Build();
        Running = true;
        finished = false;
        medal = null;
        Elapsed = 0f;
        tractor = FindAnyObjectByType<Tractor>();
        hasVehicle = tractor != null || FindAnyObjectByType<Vehicle>() != null;
        if (tractor == null) tractor = FindAnyObjectByType<Vehicle>();
        SaveData.SetInt(SeenKey, 1);
        GoTo(StepId.Look);
        canvas.gameObject.SetActive(true);
        if (TutorialOverlay.Instance != null && TutorialOverlay.Instance.Running) TutorialOverlay.Instance.Stop();
    }

    public void Stop()
    {
        Running = false;
        finished = false;
        doneAt = -1f;
        if (canvas != null) canvas.gameObject.SetActive(false);
    }

    /// <summary>Skip the current step (counts as done, no medal speed bonus lost).</summary>
    public void Skip() { if (Running && !finished) Complete(); }

    private void GoTo(StepId s)
    {
        Step = s;
        Progress = 0f;
        stepDone = false;
        doneAt = -1f;
        lookSum = 0f;
        hasPos = false;
        wasThird = ThirdPersonView.Instance != null && ThirdPersonView.Instance.IsThirdPerson;
        Refresh();
    }

    private void Complete()
    {
        if (stepDone) return;
        stepDone = true;
        Progress = 1f;
        cheer = (cheer + 1) % Cheers.Length;
        doneAt = Time.unscaledTime + 1.1f;
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX();
        Refresh();
    }

    private void Advance()
    {
        int next = (int)Step + 1;
        if (next >= StepCount) { Finish(); return; }
        GoTo((StepId)next);
    }

    private void Finish()
    {
        finished = true;
        medal = Elapsed < 120f ? "Gold" : Elapsed < 240f ? "Silver" : "Bronze";
        bool first = SaveData.GetInt(DoneKey, 0) == 0;
        if (first)
        {
            SaveData.SetInt(DoneKey, 1);
            if (EconomyManager.Instance != null) { using (Ledger.As(Ledger.Rewards)) EconomyManager.Instance.AddMoney(Reward); }
            CollectionBook.Record("feats", "Mazraa Academy");
        }
        SaveData.Save();
        doneAt = Time.unscaledTime + 7f;
        if (AudioManager.Instance != null) AudioManager.Instance.PlayHarvest();
        Refresh();
    }

    // ------------------------------------------------------------------ per frame

    private void Update()
    {
        if (!Running)
        {
            AutoStart();
            return;
        }
        if (canvas == null) return;
        bool blocked = Time.timeScale == 0f || GameMenu.IsOpen || (UIManager.Instance != null && UIManager.Instance.IsUIOpened && Step != StepId.Map);
        canvas.enabled = !blocked;
        if (blocked) return;

        if (finished)
        {
            if (Time.unscaledTime >= doneAt || MenuNav.TutorialClose() || MenuNav.TutorialNext()) Stop();
            return;
        }
        Elapsed += Time.unscaledDeltaTime;
        if (MenuNav.TutorialClose()) { Stop(); return; }
        if (MenuNav.TutorialNext()) Skip();

        if (stepDone) { if (Time.unscaledTime >= doneAt) Advance(); }
        else Track();

        int sec = (int)Elapsed, pct = (int)(Progress * 100f);
        if (sec != shownSecond || pct != shownPercent) Refresh();
    }

    // Starts once, the first time the player is on the farm with nothing else open.
    private void AutoStart()
    {
        autoCheck -= Time.unscaledDeltaTime;
        if (autoCheck > 0f) return;
        autoCheck = 1f;
        if (SaveData.GetInt(SeenKey, 0) != 0 || SaveData.InSession || !GameSettings.Tutorials) return;   // Settings > Game > Tutorials
        if (Time.timeScale == 0f || GameMenu.IsOpen || (UIManager.Instance != null && UIManager.Instance.IsUIOpened)) return;
        if (Player() == null) return;
        Begin();
    }

    private StarterAssets.StarterAssetsInputs Inputs()
    {
        if (inputs == null) inputs = FindAnyObjectByType<StarterAssets.StarterAssetsInputs>();
        return inputs;
    }

    private static Transform Player() => CozyFx.Player;

    private void Track()
    {
        var inp = Inputs();
        switch (Step)
        {
            case StepId.Look:
                if (inp != null) lookSum += Mathf.Sqrt(inp.look.x * inp.look.x + inp.look.y * inp.look.y);
                Progress = Mathf.Clamp01(lookSum / 400f);
                if (Progress >= 1f) Complete();
                break;
            case StepId.Walk:
                Distance(Player(), 10f);
                break;
            case StepId.Sprint:
                if (inp != null && inp.sprint && (inp.move.x * inp.move.x + inp.move.y * inp.move.y) > 0.01f) Progress += Time.unscaledDeltaTime / 1.5f;
                if (Progress >= 1f) Complete();
                break;
            case StepId.Jump:
                if (inp != null && inp.jump) Complete();
                break;
            case StepId.View:
                bool third = ThirdPersonView.Instance != null && ThirdPersonView.Instance.IsThirdPerson;
                if (third != wasThird || ThirdPersonView.Instance == null) Complete();
                break;
            case StepId.Map:
                if (GameInput.Pressed(GameAction.Map)) { Progress += 0.5f; if (Progress >= 1f) Complete(); }
                break;
            case StepId.Interact:
                if (GameInput.Pressed(GameAction.Interact)) Complete();
                break;
            case StepId.EnterVehicle:
                if (Vehicle.Current != null) Complete();
                break;
            case StepId.Drive:
                if (Vehicle.Current == null) { hasPos = false; break; }
                Distance(Vehicle.Current.transform, 25f);
                break;
            case StepId.Brake:
                if (Vehicle.Current == null || GameInput.Pressed(GameAction.Brake)) Complete();
                break;
            case StepId.ExitVehicle:
                if (Vehicle.Current == null) Complete();
                break;
        }
    }

    private void Distance(Transform t, float metres)
    {
        if (t == null) return;
        Vector3 p = t.position;
        if (!hasPos) { lastPos = p; hasPos = true; return; }
        Vector3 d = p - lastPos; d.y = 0f;
        float step = d.magnitude;
        if (step < 5f) Progress += step / metres;     // ignore teleports (respawn, vehicle reset)
        lastPos = p;
        if (Progress >= 1f) Complete();
    }

    // ------------------------------------------------------------------ text

    private static string Cap(string key) => "<color=" + BrownHex + "><mark=" + SandHex + "EE padding=\"10,10,4,4\"><b> " + key + " </b></mark></color>";

    /// <summary>Big key caps for the step (current device).</summary>
    public string Keys()
    {
        bool pad = GameInput.UsingGamepad;
        switch (Step)
        {
            case StepId.Look: return Cap(pad ? "R" : Loc.T("Mouse"));
            case StepId.Walk: return pad ? Cap("L") : Cap("W") + " " + Cap("A") + " " + Cap("S") + " " + Cap("D");
            case StepId.Sprint: return pad ? Cap("L3") : Cap("Shift") + " + " + Cap("W");
            case StepId.Jump: return Cap(pad ? Glyphs.PadLabel(Glyphs.Pad.South) : "Space");
            case StepId.View: return Cap(Glyphs.For(GameAction.View));
            case StepId.Map: return Cap(Glyphs.For(GameAction.Map));
            case StepId.Interact:
            case StepId.EnterVehicle:
            case StepId.ExitVehicle: return Cap(Glyphs.For(GameAction.Interact));
            case StepId.Drive: return pad ? Cap("L") + " " + Cap("RT") : Cap("W") + " " + Cap("A") + " " + Cap("S") + " " + Cap("D");
            case StepId.Brake: return Cap(Glyphs.For(GameAction.Brake));
            default: return "";
        }
    }

    public string HeaderText()
    {
        var sb = new StringBuilder(96);
        sb.Append("<b>MAZRAA</b>  <color=").Append(GoldHex).Append(">").Append(Loc.T("Academy")).Append("</color>");
        if (!finished)
            sb.Append("   <size=85%><color=#FFFFFFAA>").Append(Loc.T("Step")).Append(' ').Append((int)Step + 1).Append('/').Append(StepCount)
              .Append("   ").Append(Clock(Elapsed)).Append("</color></size>");
        return sb.ToString();
    }

    public string BodyText()
    {
        var sb = new StringBuilder(256);
        if (finished)
        {
            string color = medal == "Gold" ? GoldHex : medal == "Silver" ? "#D8D8D8" : "#D08A4A";
            sb.Append("<size=120%><b><color=").Append(color).Append(">").Append(Loc.T(medal + " medal")).Append("</color></b></size>\n");
            sb.Append(Loc.T("You know the controls! Time: {0}", Clock(Elapsed))).Append('\n');
            sb.Append("<color=").Append(MintHex).Append(">").Append(Loc.T("+{0} SAR and a new collection book entry", Reward)).Append("</color>");
            return sb.ToString();
        }
        int i = (int)Step;
        sb.Append("<size=115%><b>").Append(Loc.T(Titles[i])).Append("</b></size>   ").Append(Keys()).Append('\n');
        sb.Append("<size=90%>").Append(Glyphs.Apply(Loc.T(Hints[i]))).Append("</size>");
        if (Step == StepId.EnterVehicle && tractor != null && Player() != null)
            sb.Append("\n<size=80%><color=").Append(GoldHex).Append(">").Append(Loc.T("Tractor: {0} m", Mathf.RoundToInt(Vector3.Distance(Player().position, tractor.transform.position)))).Append("</color></size>");
        if (stepDone) sb.Append("\n<color=").Append(MintHex).Append("><b>").Append(Loc.T(Cheers[cheer])).Append("</b></color>");
        return sb.ToString();
    }

    private static string Clock(float s)
    {
        int t = Mathf.Max(0, (int)s);
        return (t / 60) + ":" + (t % 60).ToString("00");
    }

    private void Refresh()
    {
        if (!Running || header == null) return;
        shownSecond = (int)Elapsed;
        shownPercent = (int)(Progress * 100f);
        bool rtl = Loc.IsArabic;
        var align = rtl ? TextAlignmentOptions.Right : TextAlignmentOptions.Left;
        float h = finished ? 150f : 170f;
        card.sizeDelta = new Vector2(640f, h);
        Place(header, h - 14f, 30f, align);
        Place(body, h - 48f, 92f, align);
        Place(footer, 18f, 22f, rtl ? TextAlignmentOptions.Left : TextAlignmentOptions.Right);
        header.text = HeaderText();
        body.text = BodyText();
        footer.text = finished ? "" : (GameInput.UsingGamepad ? Loc.T("[D-pad right] Skip   [D-pad down] Close") : Loc.T("[Enter] Skip   [Backspace] Close"));
        barFill.anchorMax = new Vector2(finished ? 1f : Mathf.Clamp01(((int)Step + Progress) / StepCount), 1f);
        for (int i = 0; i < dots.Length; i++)
        {
            dots[i].enabled = i < StepCount;
            dots[i].color = finished || i < (int)Step || (i == (int)Step && stepDone) ? Gold : i == (int)Step ? Sand : new Color(1f, 1f, 1f, 0.25f);
        }
    }

    private void Place(TextMeshProUGUI t, float top, float height, TextAlignmentOptions align)
    {
        var rt = t.rectTransform;
        rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(-40f, height);
        rt.anchoredPosition = new Vector2(0f, top);
        t.alignment = align;
    }

    private int glyphVersion = -1;
    private void LateUpdate() { if (Running && glyphVersion != GameInput.Version) { glyphVersion = GameInput.Version; Refresh(); } }
}
