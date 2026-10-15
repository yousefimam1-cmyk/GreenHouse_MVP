using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Main menu (shown at start), pause menu (P / Start), save slots, settings and key rebinding, all built in code.
/// Keyboard, mouse and gamepad: Up/Down select, Left/Right change a setting, Enter / A choose, Esc / B back.
/// The game is paused (timeScale 0) while the menu is open. Autosaves every in-game day (SaveSlots).
/// </summary>
[MazraaSystem(60)]
public class GameMenu : MonoBehaviour
{
    public enum Mode { Closed, Main, Pause }
    private enum PageId { Root, Save, Load, Settings, Controls, ConfirmNew }

    /// <summary>Show the main menu when the game starts (headless tests turn this off).</summary>
    public static bool ShowOnStart = true;
    public static GameMenu Instance { get; private set; }
    public static bool IsOpen => Instance != null && Instance.mode != Mode.Closed;

    public Mode CurrentMode => mode;
    public MenuList List => list;

    private Mode mode = Mode.Closed;
    private PageId page = PageId.Root;
    private readonly Stack<PageId> history = new Stack<PageId>();
    private Canvas canvas;
    private MenuList list;
    private int openedFrame, rebindEndFrame = -10;
    private float timeScaleBefore = 1f;
    private bool waitingForKey;

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
        MenuKit.Backdrop(canvas.transform, 0.7f);
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
    private void OnDay(int day) { CancelInvoke(nameof(Autosave)); Invoke(nameof(Autosave), 0.5f); }

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
        openedFrame = Time.frameCount;
        history.Clear();
        ShowPage(PageId.Root);
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
            case PageId.Settings: list.Show(() => MenuKit.Title(UiText.T("Settings")), SettingsRows(), () => UiText.T("[<- ->] Change   [Esc] Back"), sel); break;
            case PageId.Controls: list.Show(() => MenuKit.Title(UiText.T("Controls")), ControlRows(), ControlsFooter, sel); break;
            case PageId.ConfirmNew:
                list.Show(() => MenuKit.Title(UiText.T("Start a new game?")), new List<MenuRow>
                {
                    MenuRow.Button(() => UiText.T("No, go back"), Back),
                    MenuRow.Button(() => UiText.T("Yes, clear the current save (slots are kept)"), NewGame),
                }, () => UiText.T("[Enter] Choose   [Esc] Back"));
                break;
        }
    }

    private static bool HasSave() => SaveData.HasKey("mazraa.introSeen") || SaveData.HasKey("mazraa.money") || SaveData.HasKey("mazraa.mission");

    // ------------------------------------------------------------------ pages

    private List<MenuRow> RootRows()
    {
        var rows = new List<MenuRow>();
        if (mode == Mode.Main)
        {
            rows.Add(new MenuRow { label = () => UiText.T("Continue"), confirm = Close, enabled = HasSave });
            rows.Add(MenuRow.Button(() => UiText.T("New game"), () => { if (HasSave()) Go(PageId.ConfirmNew); else NewGame(); }));
            for (int i = 1; i <= SaveSlots.Count; i++)
            {
                int slot = i;
                rows.Add(new MenuRow { label = () => Loc.Display(UiText.Raw("Load slot") + " " + slot + ": " + SaveSlots.Describe(slot)), confirm = () => LoadSlot(slot), enabled = () => SaveSlots.HasSlot(slot) });
            }
        }
        else
        {
            rows.Add(MenuRow.Button(() => UiText.T("Resume"), Close));
            rows.Add(MenuRow.Button(() => UiText.T("Save game"), () => Go(PageId.Save)));
            rows.Add(MenuRow.Button(() => UiText.T("Load game"), () => Go(PageId.Load)));
        }
        rows.Add(MenuRow.Button(() => UiText.T("Tutorial"), StartTutorial));
        rows.Add(MenuRow.Button(() => UiText.T("Settings"), () => Go(PageId.Settings)));
        if (mode == Mode.Pause)
        {
            rows.Add(MenuRow.Button(() => UiText.T("New game"), () => Go(PageId.ConfirmNew)));
            rows.Add(MenuRow.Button(() => UiText.T("Main menu"), () => { mode = Mode.Main; history.Clear(); ShowPage(PageId.Root); }));
        }
        rows.Add(MenuRow.Button(() => UiText.T("Quit"), Quit));
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

    private List<MenuRow> SettingsRows()
    {
        return new List<MenuRow>
        {
            MenuRow.Setting(() => UiText.T("Master volume"), () => Pct(GameSettings.MasterVolume), d => GameSettings.MasterVolume = Step(GameSettings.MasterVolume, d * 0.1f, 0f, 1f)),
            MenuRow.Setting(() => UiText.T("Music volume"), () => Pct(GameSettings.MusicVolume), d => GameSettings.MusicVolume = Step(GameSettings.MusicVolume, d * 0.1f, 0f, 1f)),
            MenuRow.Setting(() => UiText.T("Sound effects volume"), () => Pct(GameSettings.SfxVolume), d => GameSettings.SfxVolume = Step(GameSettings.SfxVolume, d * 0.1f, 0f, 1f)),
            MenuRow.Setting(() => UiText.T("Graphics"), () => UiText.T(GameSettings.PresetName(GameSettings.GraphicsPreset)), d => GameSettings.GraphicsPreset = (GameSettings.GraphicsPreset + d + 3) % 3),
            MenuRow.Setting(() => UiText.T("Mouse sensitivity"), () => Mult(GameSettings.MouseSensitivity), d => GameSettings.MouseSensitivity = Step(GameSettings.MouseSensitivity, d * 0.1f, 0.1f, 3f)),
            MenuRow.Setting(() => UiText.T("Stick sensitivity"), () => Mult(GameSettings.StickSensitivity), d => GameSettings.StickSensitivity = Step(GameSettings.StickSensitivity, d * 0.1f, 0.1f, 3f)),
            MenuRow.Setting(() => UiText.T("Invert Y axis"), () => OnOff(GameSettings.InvertY), d => GameSettings.InvertY = !GameSettings.InvertY),
            MenuRow.Setting(() => UiText.T("Language"), () => Loc.IsArabic ? Loc.Display("العربية") : "English", d => Loc.Toggle()),
            MenuRow.Setting(() => UiText.T("Camera"), () => UiText.T(GameSettings.ThirdPerson ? "Third person" : "First person"), d => ThirdPersonView.SetMode(!GameSettings.ThirdPerson)),
            MenuRow.Button(() => UiText.T("Controls (rebind keys)"), () => Go(PageId.Controls)),
            MenuRow.Button(() => UiText.T("Back"), Back),
        };
    }

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
        bool ps = GameInput.IsPlayStation;
        string label = a == GameAction.Interact ? Glyphs.PadLabel(Glyphs.Pad.South, ps) : a == GameAction.VehicleAction ? Glyphs.PadLabel(Glyphs.Pad.West, ps)
                     : a == GameAction.Implement ? Glyphs.PadLabel(Glyphs.Pad.North, ps) : a == GameAction.Pause ? Glyphs.PadLabel(Glyphs.Pad.Start, ps)
                     : a == GameAction.Map ? Glyphs.PadLabel(Glyphs.Pad.Select, ps) : Glyphs.PadLabel(Glyphs.Pad.RStick, ps);
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

    private void NewGame()
    {
        if (GameHUD.Instance != null) GameHUD.Instance.StartNewGame();
        Close();
    }

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
