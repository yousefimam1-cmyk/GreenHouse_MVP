using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Main menu (shown at start), pause menu (P / Start), save slots, settings and key rebinding, all built in code.
/// Keyboard, mouse and gamepad: Up/Down select, Left/Right change a setting, Enter / A choose, Esc / B back.
/// The game is paused (timeScale 0) while the menu is open. Autosaves every in-game day (SaveSlots).
/// </summary>
[MazraaSystem(60)]
public partial class GameMenu : MonoBehaviour   // settings hub and pages: GameMenuSettings.cs
{
    public enum Mode { Closed, Main, Pause }
    private enum PageId { Root, Save, Load, Settings, Controls, ConfirmNew, Campaign, Map, Start, Economy, Credits, Achievements, SettingsPage }

    /// <summary>Show the main menu when the game starts (headless tests turn this off).</summary>
    public static bool ShowOnStart = true;
    public static GameMenu Instance { get; private set; }
    public static bool IsOpen => Instance != null && Instance.mode != Mode.Closed;

    public Mode CurrentMode => mode;
    public MenuList List => list;
    public Canvas MenuCanvas => canvas;

    private Mode mode = Mode.Closed;
    private PageId page = PageId.Root;
    private readonly Stack<PageId> history = new Stack<PageId>();
    private Canvas canvas;
    private MenuList list;
    private int openedFrame, rebindEndFrame = -10;
    private float timeScaleBefore = 1f;
    private bool waitingForKey;
    private string pendingRegion = Regions.DefaultId;
    private Career.Start pendingStart = Career.Start.NewFarmer;
    private UnityEngine.UI.Image backdrop;

    private void Awake() => Instance = this;

    private void OnEnable() { WorldClock.DayStarted += OnDay; GameEvents.NewGameStarted += OnNewGame; }
    private void OnDisable() { WorldClock.DayStarted -= OnDay; GameEvents.NewGameStarted -= OnNewGame; }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        Loc.OnLanguageChanged -= OnLanguage;
        GameInput.CancelRebind();
        if (mode != Mode.Closed) Time.timeScale = 1f;
    }

    private void Start()
    {
        canvas = MenuKit.Canvas("Game Menu UI", transform, 90);
        backdrop = MenuKit.Backdrop(canvas.transform, 0.7f);
        list = new MenuList(canvas.transform, 860);
        canvas.gameObject.SetActive(false);
        Loc.OnLanguageChanged += OnLanguage;
        bool skip = SaveSlots.JustLoaded;
        SaveSlots.JustLoaded = false;
        // A moment after start, so UIManager has found the player's input (cursor / look) first.
        if (ShowOnStart && !skip) Invoke(nameof(OpenMainMenu), 0.05f);
        else if (skip && UIManager.Instance != null) UIManager.Instance.ShowAlertMessage(UiText.T("Game loaded"));
    }

    private void OpenMainMenu() { if (mode == Mode.Closed) Open(Mode.Main); }

    private void OnLanguage() { if (mode != Mode.Closed) list.Refresh(); }

    private void OnNewGame() { SaveSlots.ActiveSlot = 0; }

    // Autosave a moment after midnight, once the other systems have rolled the day over.
    private void OnDay(int day) { if (GameSettings.AutosaveMinutes != -1) return; CancelInvoke(nameof(Autosave)); Invoke(nameof(Autosave), 0.5f); }   // Settings > Game > Autosave

    private void Autosave()
    {
        SaveSlots.Autosave();
        if (UIManager.Instance != null && !IsOpen) UIManager.Instance.ShowAlertMessage(UiText.T("Autosaved"));
    }

    // ------------------------------------------------------------------ open / close

    public void Open(Mode m)
    {
        if (canvas == null || m == Mode.Closed) return;
        if (mode == Mode.Closed)
        {
            timeScaleBefore = Time.timeScale > 0f ? Time.timeScale : 1f;
            Time.timeScale = 0f;
            canvas.gameObject.SetActive(true);
            if (UIManager.Instance != null) UIManager.Instance.SetExternalPanel(true);
        }
        mode = m;
        ApplyLayout();
        openedFrame = Time.frameCount;
        history.Clear();
        ShowPage(PageId.Root);
    }

    /// <summary>Main and pause menu: list on the left (main: over the moving farm; pause: over the frozen game, lightly dimmed).</summary>
    private void ApplyLayout()
    {
        var p = list.Panel;
        bool main = mode == Mode.Main;
        // Both menus: column on the left (MainMenuScene draws the farm cards / overview on the right).
        if (backdrop != null) backdrop.color = new Color(0.02f, 0.02f, 0.015f, main ? 0.05f : 0.35f);
        p.anchorMin = p.anchorMax = new Vector2(0f, 0.5f);
        p.pivot = new Vector2(0f, 0.6f);
        p.anchoredPosition = new Vector2(64f, 0f);
        p.sizeDelta = new Vector2(main ? 560f : 600f, p.sizeDelta.y);
    }

    public void Close()
    {
        if (mode == Mode.Closed) return;
        GameInput.CancelRebind();
        waitingForKey = false;
        mode = Mode.Closed;
        canvas.gameObject.SetActive(false);
        Time.timeScale = timeScaleBefore;
        if (UIManager.Instance != null) UIManager.Instance.SetExternalPanel(false);
    }

    private void Update()
    {
        if (canvas == null) return;
        if (mode == Mode.Closed)
        {
            if (GameInput.Pressed(GameAction.Pause)) Open(Mode.Pause);
            return;
        }
        if (Time.frameCount == openedFrame || GameInput.Rebinding || Time.frameCount - rebindEndFrame < 3) return;
        if (mode == Mode.Pause && page == PageId.Root && GameInput.Pressed(GameAction.Pause)) { Close(); return; }
        if (MenuNav.Back()) { Back(); return; }
        if (SettingsTabInput()) return;
        list.HandleInput();
    }

    public void Back()
    {
        if (history.Count > 0) { ShowPage(history.Pop(), false); return; }
        if (mode == Mode.Pause) Close();
        else if (mode == Mode.Main && HasSave()) Close();   // Esc on the main menu = Continue
    }

    private void Go(PageId p) { history.Push(page); ShowPage(p); }

    private void ShowPage(PageId p, bool fresh = true)
    {
        page = p;
        int sel = fresh ? 0 : list.Selected;
        switch (p)
        {
            case PageId.Root: list.Show(() => MenuKit.Title(Loc.T(mode == Mode.Main ? "Mazraa" : "Paused")), RootRows(), () => UiText.T("[Enter] Choose   [Esc] Back"), sel); break;
            case PageId.Save: list.Show(() => MenuKit.Title(UiText.T("Save game")), SlotRows(true), () => UiText.T("[Enter] Choose   [Esc] Back")); break;
            case PageId.Load: list.Show(() => MenuKit.Title(UiText.T("Load game")), SlotRows(false), () => UiText.T("[Enter] Choose   [Esc] Back")); break;
            case PageId.Settings: ShowSettingsHub(fresh ? 0 : (int)settingsTab); break;
            case PageId.SettingsPage: ShowSettingsPage(sel); break;
            case PageId.Controls: list.Show(() => MenuKit.Title(UiText.T("Controls")), ControlRows(), ControlsFooter, sel); break;
            case PageId.Campaign: list.Show(() => MenuKit.Title(UiText.T("Campaign")), CampaignRows(), () => UiText.T("[Enter] Replay chapter story   [Esc] Back"), sel); break;
            case PageId.Map: list.Show(() => MenuKit.Title(UiText.T("Choose your map")), MapRows(), () => UiText.T("[Enter] Choose   [Esc] Back"), sel); break;
            case PageId.Start: list.Show(() => MenuKit.Title(UiText.T("How do you want to start?")), StartRows(), () => UiText.T("[Enter] Choose   [Esc] Back"), sel); break;
            case PageId.Economy: list.Show(() => MenuKit.Title(UiText.T("Economy difficulty")), EconomyRows(), () => UiText.T("[Enter] Start farming   [Esc] Back"), fresh ? 1 : sel); break;
            case PageId.Credits: list.Show(() => MenuKit.Title(UiText.T("Credits")), CreditRows(), () => UiText.T("[Esc] Back"), sel); break;
            case PageId.Achievements: list.Show(() => MenuKit.Title(UiText.T("Achievements") + "  " + AchievementManager.UnlockedCount + "/" + AchievementManager.All.Length), AchievementRows(), () => UiText.T("[Esc] Back"), sel); break;
            case PageId.ConfirmNew:
                list.Show(() => MenuKit.Title(UiText.T("Start a new game?")), new List<MenuRow>
                {
                    MenuRow.Button(() => UiText.T("No, go back"), Back),
                    MenuRow.Button(() => UiText.T("Yes, clear the current save (slots are kept)"), () => Go(PageId.Map)),
                }, () => UiText.T("[Enter] Choose   [Esc] Back"));
                break;
        }
    }

    private List<MenuRow> CampaignRows()
    {
        var rows = new List<MenuRow>();
        var cm = CampaignManager.Instance;
        if (cm == null) return rows;
        string[] lines = cm.Overview().TrimEnd('\n').Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            int ch = i;
            string line = lines[i];
            rows.Add(new MenuRow
            {
                label = () => line,
                confirm = () => { Close(); cm.Show(cm.ChapterCard(ch, true, 0), 30f); },
                enabled = () => ch <= cm.CurrentChapter || (MissionManager.Instance != null && MissionManager.Instance.AllDone),
            });
        }
        return rows;
    }

    private static bool HasSave() => SaveData.HasKey("mazraa.introSeen") || SaveData.HasKey("mazraa.money") || SaveData.HasKey("mazraa.mission");

    // ------------------------------------------------------------------ pages

    private List<MenuRow> RootRows()
    {
        var rows = new List<MenuRow>();
        if (mode == Mode.Main)
        {
            rows.Add(new MenuRow { label = () => UiText.T("Continue"), confirm = Close, enabled = HasSave });
            rows.Add(MenuRow.Button(() => UiText.T("New game"), () => { if (HasSave()) Go(PageId.ConfirmNew); else Go(PageId.Map); }));
            for (int i = 1; i <= SaveSlots.Count; i++)
            {
                int slot = i;
                rows.Add(new MenuRow { label = () => Loc.Display(UiText.Raw("Load slot") + " " + slot + ": " + SaveSlots.Describe(slot)), confirm = () => LoadSlot(slot), enabled = () => SaveSlots.HasSlot(slot) });
            }
        }
        else
        {
            rows.Add(MenuRow.Button(() => UiText.T("Resume"), Close));
            rows.Add(MenuRow.Button(() => UiText.T("Map"), () => { Close(); if (Minimap.Instance != null) Minimap.Instance.SetFull(true); }));
            rows.Add(MenuRow.Button(() => UiText.T("Farm overview"), () => { Close(); if (FarmInfoScreen.Instance != null) FarmInfoScreen.Instance.Show(FarmInfoScreen.Page.Stats); }));
            rows.Add(MenuRow.Button(() => UiText.T("Inventory"), () => { Close(); if (FarmInfoScreen.Instance != null) FarmInfoScreen.Instance.Show(FarmInfoScreen.Page.Inventory); }));
            rows.Add(MenuRow.Button(() => UiText.T("Fields (precision farming)"), () => { Close(); if (FarmInfoScreen.Instance != null) FarmInfoScreen.Instance.Show(FarmInfoScreen.Page.Fields); }));
            // farm life (FarmLife/*.cs): journal (level, stand orders, friends, festivals) and photo mode
            rows.Add(MenuRow.Button(() => UiText.T("Farm life journal"), () => { Close(); if (FarmLifeScreen.Instance != null) FarmLifeScreen.Instance.Show(FarmLifeScreen.Tab.Overview); }));
            rows.Add(MenuRow.Button(() => UiText.T("Photo mode"), () => { Close(); if (PhotoMode.Instance != null) PhotoMode.Instance.Enter(); }));
            if (CampaignManager.Instance != null && CampaignManager.Instance.Chapters.Length > 0)
                rows.Add(MenuRow.Button(() => UiText.T("Campaign"), () => Go(PageId.Campaign)));
            rows.Add(MenuRow.Button(() => UiText.T("Save game"), () => Go(PageId.Save)));
            rows.Add(MenuRow.Button(() => UiText.T("Load game"), () => Go(PageId.Load)));
        }
        rows.Add(MenuRow.Button(() => UiText.T("Controls tutorial"), StartControls));
        rows.Add(MenuRow.Button(() => UiText.T("Tutorial"), StartTutorial));
        rows.Add(MenuRow.Button(() => UiText.T("Settings"), () => Go(PageId.Settings)));
        rows.Add(MenuRow.Button(() => UiText.T("Achievements"), () => Go(PageId.Achievements)));
        if (mode == Mode.Main) rows.Add(MenuRow.Button(() => UiText.T("Credits"), () => Go(PageId.Credits)));
        if (mode == Mode.Pause)
        {
            rows.Add(MenuRow.Button(() => UiText.T("New game"), () => Go(PageId.ConfirmNew)));
            rows.Add(MenuRow.Button(() => UiText.T("Main menu"), () => { mode = Mode.Main; ApplyLayout(); history.Clear(); ShowPage(PageId.Root); }));
        }
        rows.Add(MenuRow.Button(() => UiText.T("Quit"), Quit));
        return rows;
    }

    // ------------------------------------------------------------------ new career: map -> start -> economy

    private List<MenuRow> MapRows()
    {
        var rows = new List<MenuRow>();
        foreach (var r in Regions.All)
        {
            var region = r;
            rows.Add(new MenuRow
            {
                label = () => Loc.Display(Regions.Name(region) + " - " + Regions.Country(region)) + "\n<size=70%><color=#FFFFFFAA>" + Loc.Display(Regions.Desc(region)) + "</color></size>",
                value = () => region.comingSoon ? UiText.T("Coming soon") : region.dlc && !Dlc.Owns(region.id) ? UiText.T("DLC - not owned") : region.dlc ? UiText.T("DLC") : "",
                confirm = () => { if (!Regions.IsPlayable(region)) return; pendingRegion = region.id; Go(PageId.Start); },
                enabled = () => Regions.IsPlayable(region),
            });
        }
        rows.Add(MenuRow.Button(() => UiText.T("Back"), Back));
        return rows;
    }

    private List<MenuRow> StartRows()
    {
        var rows = new List<MenuRow>();
        foreach (Career.Start st in Enum.GetValues(typeof(Career.Start)))
        {
            var start = st;
            rows.Add(new MenuRow
            {
                label = () => UiText.T(Career.StartName(start)) + "\n<size=70%><color=#FFFFFFAA>" + UiText.T(Career.StartDesc(start)) + "</color></size>",
                value = () => Career.StartMoney(start).ToString("N0") + " " + UiText.T("SAR"),
                confirm = () => { pendingStart = start; Go(PageId.Economy); },
            });
        }
        rows.Add(MenuRow.Button(() => UiText.T("Back"), Back));
        return rows;
    }

    private List<MenuRow> EconomyRows()
    {
        var rows = new List<MenuRow>();
        foreach (Career.Economy ec in Enum.GetValues(typeof(Career.Economy)))
        {
            var econ = ec;
            rows.Add(new MenuRow
            {
                label = () => UiText.T(Career.EconomyName(econ)),
                value = () => UiText.T(Career.EconomyDesc(econ)),
                confirm = () => StartCareer(pendingRegion, pendingStart, econ),
            });
        }
        rows.Add(MenuRow.Button(() => UiText.T("Back"), Back));
        return rows;
    }

    /// <summary>Start a new farm with the chosen map, start and economy (also used by tests).</summary>
    public void StartCareer(string region, Career.Start start, Career.Economy economy)
    {
        if (GameHUD.Instance != null) GameHUD.Instance.StartNewGame();
        Regions.Select(region);
        Career.Apply(start, economy);
        Close();
        LoadingScreen.Show();
    }

    private static readonly string[] Credits =
    {
        "<b>MAZRAA</b>  -  Almadarek Games",
        "Game design, programming and art direction: Almadarek Games",
        "Music: original Mazraa theme (made for the game)",
        "Vehicles, crops and characters: Kenney (kenney.nl) - CC0",
        "Farm animals: Quaternius - CC0",
        "Chicken: CDmir (opengameart.org) - CC0",
        "Arabic font: Tajawal by Boutros Fonts - SIL Open Font License",
        "Crop growth stages, skybox, well and first-person controller: Unity Asset Store packages",
        "Made with Unity",
        "Thank you for playing!",
    };

    private List<MenuRow> CreditRows()
    {
        var rows = new List<MenuRow>();
        foreach (var c in Credits) { string line = c; rows.Add(MenuRow.Button(() => UiText.T(line), () => { })); }
        rows.Add(MenuRow.Button(() => UiText.T("Back"), Back));
        return rows;
    }

    private List<MenuRow> AchievementRows()
    {
        var rows = new List<MenuRow>();
        foreach (var a in AchievementManager.All) { var def = a; rows.Add(MenuRow.Button(() => AchievementManager.Line(def), () => { })); }
        rows.Add(MenuRow.Button(() => UiText.T("Back"), Back));
        return rows;
    }

    private List<MenuRow> SlotRows(bool saving)
    {
        var rows = new List<MenuRow>();
        for (int i = 1; i <= SaveSlots.Count; i++)
        {
            int slot = i;
            if (saving)
                rows.Add(MenuRow.Button(() => Loc.Display(UiText.Raw("Save to slot") + " " + slot + ": " + SaveSlots.Describe(slot) + (SaveSlots.ActiveSlot == slot ? "  *" : "")), () => SaveSlot(slot)));
            else
                rows.Add(new MenuRow { label = () => Loc.Display(UiText.Raw("Load slot") + " " + slot + ": " + SaveSlots.Describe(slot)), confirm = () => LoadSlot(slot), enabled = () => SaveSlots.HasSlot(slot) });
        }
        rows.Add(MenuRow.Button(() => UiText.T("Back"), Back));
        return rows;
    }

    private static string Pct(float v) => Mathf.RoundToInt(v * 100f) + "%";
    private static string Mult(float v) => v.ToString("0.0") + "x";
    private static string OnOff(bool v) => UiText.T(v ? "On" : "Off");

    private static float Step(float v, float d, float min, float max) => Mathf.Clamp(Mathf.Round((v + d) * 10f) / 10f, min, max);

    private List<MenuRow> ControlRows()
    {
        var rows = new List<MenuRow>();
        for (int i = 0; i < GameInput.Count; i++)
        {
            var a = (GameAction)i;
            rows.Add(new MenuRow
            {
                label = () => UiText.T(GameInput.Label(a)),
                value = () => waitingForKey && rebinding == a ? UiText.T("Press a key...") : GameInput.KeyName(a) + PadSuffix(a),
                confirm = () => Rebind(a),
            });
        }
        rows.Add(MenuRow.Button(() => UiText.T("Reset to defaults"), () => { GameInput.ResetBindings(); list.Refresh(); }));
        rows.Add(MenuRow.Button(() => UiText.T("Back"), Back));
        return rows;
    }

    private static string PadSuffix(GameAction a)
    {
        string pad = GameInput.GamepadPath(a);
        if (string.IsNullOrEmpty(pad)) return "";
        string label = Glyphs.PadFor(a, GameInput.IsPlayStation);
        return "  /  " + label;
    }

    private string ControlsFooter() => waitingForKey ? UiText.T("Press the new key ([Esc] cancels)") : UiText.T("[Enter] Change key   [Esc] Back");

    private GameAction rebinding;

    private void Rebind(GameAction a)
    {
        if (waitingForKey) return;
        waitingForKey = true;
        rebinding = a;
        list.Refresh();
        GameInput.StartRebind(a, ok =>
        {
            waitingForKey = false;
            rebindEndFrame = Time.frameCount;
            if (list != null) list.Refresh();
        });
    }

    // ------------------------------------------------------------------ actions

    private void SaveSlot(int slot)
    {
        bool ok = SaveSlots.Save(slot);
        if (UIManager.Instance != null) UIManager.Instance.ShowAlertMessage(ok ? Loc.Display(UiText.Raw("Saved to slot") + " " + slot) : UiText.T("Could not save"));
        list.Refresh();
    }

    private void LoadSlot(int slot)
    {
        if (!SaveSlots.HasSlot(slot)) return;
        Close();
        SaveSlots.Load(slot);
    }

    private void StartControls()
    {
        Close();
        if (ControlsTutorial.Instance != null) ControlsTutorial.Instance.Begin();
    }

    private void StartTutorial()
    {
        Close();
        if (TutorialOverlay.Instance != null) TutorialOverlay.Instance.Begin();
    }

    private static void Quit()
    {
        SaveData.Flush(true);
        Application.Quit();
    }
}
