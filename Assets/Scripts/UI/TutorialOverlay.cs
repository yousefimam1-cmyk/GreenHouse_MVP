using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Replayable tutorial (Tutorial button in the main / pause menu): walks through the hints of the first 6 missions
/// in a small overlay, without touching mission progress or the save. A step ticks off when the player does it
/// (well started, 2 modules optimized, a healthy greenhouse, a harvest, a field planted, a field harvested)
/// or with Enter / D-pad right; Backspace / D-pad down closes it.
/// </summary>
[MazraaSystem(65)]
public class TutorialOverlay : MonoBehaviour
{
    public const int StepCount = 6;
    public static TutorialOverlay Instance { get; private set; }

    public bool Running { get; private set; }
    public int Step { get; private set; }
    public bool StepDone { get; private set; }

    // Hints per mission type (English Loc keys in 50_ui.json); [E] etc. follow the device in use.
    private static readonly string[] Types = { "startWell", "optimize", "healthyGreenhouse", "harvestKg", "plantField", "harvestField" };
    private static readonly string[] Hints =
    {
        "Walk to the well next to Greenhouse 1 and press [E] to start the pump.",
        "Open a Control Station with [E] and press Optimize on 2 modules.",
        "Keep optimizing until every bed in one greenhouse turns green.",
        "When a bed is fully grown, look at it and press [E] to harvest.",
        "At a field plot press [E] to plow, then [E] again to plant seeds.",
        "Water the plot with [E] while the well runs, then harvest it when ripe.",
    };

    private Canvas canvas;
    private RectTransform panel;
    private TextMeshProUGUI text;
    private int optimizedThisStep;
    private float advanceAt = -1f, pollTimer;

    private void Awake() => Instance = this;

    private void OnEnable()
    {
        GameEvents.WellStarted += OnWell;
        GameEvents.ModuleOptimized += OnOptimized;
        GameEvents.Harvested += OnHarvested;
        FieldEvents.Planted += OnPlanted;
        FieldEvents.Harvested += OnFieldHarvested;
        Loc.OnLanguageChanged += Refresh;
    }

    private void OnDisable()
    {
        GameEvents.WellStarted -= OnWell;
        GameEvents.ModuleOptimized -= OnOptimized;
        GameEvents.Harvested -= OnHarvested;
        FieldEvents.Planted -= OnPlanted;
        FieldEvents.Harvested -= OnFieldHarvested;
        Loc.OnLanguageChanged -= Refresh;
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    private void Start()
    {
        canvas = MenuKit.Canvas("Tutorial UI", transform, 45);
        panel = MenuKit.Panel("Tutorial", canvas.transform, 520);
        panel.GetComponent<Image>().color = UiTheme.GlassStrong;
        panel.GetComponent<Image>().raycastTarget = false;
        text = MenuKit.Text(panel, 22, Color.white);
        canvas.gameObject.SetActive(false);
    }

    /// <summary>Start (or restart) the tutorial from step 1. Mission progress is not changed.</summary>
    public void Begin()
    {
        if (ControlsTutorial.Instance != null && ControlsTutorial.Instance.Running) ControlsTutorial.Instance.Stop();
        Running = true;
        GoTo(0);
        if (canvas != null) canvas.gameObject.SetActive(true);
    }

    public void Stop()
    {
        Running = false;
        advanceAt = -1f;
        if (canvas != null) canvas.gameObject.SetActive(false);
    }

    public void Next()
    {
        if (!Running) return;
        if (Step + 1 >= StepCount) { Stop(); if (UIManager.Instance != null) UIManager.Instance.ShowAlertMessage(UiText.T("Tutorial complete")); return; }
        GoTo(Step + 1);
    }

    private void GoTo(int step)
    {
        Step = step;
        StepDone = false;
        optimizedThisStep = 0;
        advanceAt = -1f;
        Refresh();
    }

    private void Complete(int step)
    {
        if (!Running || Step != step || StepDone) return;
        StepDone = true;
        advanceAt = Time.unscaledTime + 1.6f;
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX();
        Refresh();
    }

    private void OnWell(WellController w) => Complete(0);
    private void OnOptimized(GreenhouseManager g, ModuleType t) { if (Running && Step == 1 && ++optimizedThisStep >= 2) Complete(1); }
    private void OnHarvested(Cropbed b, float kg) { if (b != null) Complete(3); }
    private void OnPlanted(FarmField p, string crop) => Complete(4);
    private void OnFieldHarvested(FarmField p, string crop, float kg) => Complete(5);

    private void Update()
    {
        if (!Running || canvas == null) return;
        if (advanceAt > 0f && Time.unscaledTime >= advanceAt) { Next(); return; }
        pollTimer -= Time.unscaledDeltaTime;
        if (pollTimer <= 0f) { pollTimer = 0.5f; PollState(); }
        bool blocked = Time.timeScale == 0f || (UIManager.Instance != null && UIManager.Instance.IsUIOpened);
        canvas.enabled = !blocked;
        if (blocked) return;
        if (MenuNav.TutorialClose()) Stop();
        else if (MenuNav.TutorialNext()) Next();
    }

    // Steps that are about the farm's state also tick when that state is already true.
    private void PollState()
    {
        if (Step == 0)
            foreach (var w in FindObjectsByType<WellController>(FindObjectsSortMode.None)) if (w.IsRunning) { Complete(0); return; }
        if (Step == 2 && GameManager.Instance != null)
            foreach (var gh in GameManager.Instance.Greenhouses)
                if (gh != null && gh.Cropbeds.Count > 0 && gh.HealthyCount == gh.Cropbeds.Count) { Complete(2); return; }
    }

    private static MissionDef Mission(int step)
    {
        var mm = MissionManager.Instance;
        if (mm == null) return null;
        foreach (var m in mm.Missions) if (m != null && m.type == Types[step]) return m;
        return step < mm.Missions.Length ? mm.Missions[step] : null;
    }

    public string BodyText()
    {
        var sb = new StringBuilder();
        sb.Append(MenuKit.Title(Loc.Display(UiText.Raw("Tutorial") + " " + (Step + 1) + "/" + StepCount)));
        var m = Mission(Step);
        if (m != null)
        {
            sb.Append("\n<b>").Append(Loc.Display(MissionManager.Title(m))).Append("</b>");
            sb.Append("\n<size=90%>").Append(Loc.Display(MissionManager.Desc(m))).Append("</size>");
        }
        sb.Append("\n\n").Append(Glyphs.Apply(UiText.T(Hints[Step])));
        if (StepDone) sb.Append("\n<color=#8CD95A><b>").Append(UiText.T("Done!")).Append("</b></color>");
        return sb.ToString();
    }

    private void Refresh()
    {
        if (text == null || !Running) return;
        bool rtl = Loc.IsArabic;
        float x = rtl ? 0f : 1f;
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(x, 0.62f);
        panel.anchoredPosition = new Vector2(rtl ? 24f : -24f, 0f);
        Loc.ApplyFont(text);
        text.alignment = rtl ? TextAlignmentOptions.TopRight : TextAlignmentOptions.TopLeft;
        text.text = BodyText();
    }

    private int glyphVersion = -1;
    private void LateUpdate() { if (Running && glyphVersion != GameInput.Version) { glyphVersion = GameInput.Version; Refresh(); } }
}
