using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Farm life journal: Y (or D-pad up) opens it, also the pause menu and the roadside stand / festival stand.
/// Tabs (LB / RB or Left / Right): Overview (level, rewards to collect, daily gift, beauty, photo mode),
/// Roadside stand (serve or send away visitors), Friends (hearts, birthdays, known favourite gifts),
/// Calendar (today's festival and contest, upcoming festivals and birthdays), Crop mastery, Level unlocks.
/// Up / Down select, Enter / A choose, Esc / B or Y close. The game keeps running underneath.
/// </summary>
[MazraaSystem(81)]
public class FarmLifeScreen : MonoBehaviour
{
    public enum Tab { Overview, Orders, Friends, Calendar, Mastery, Unlocks }
    private static readonly string[] TabKeys = { "Overview", "Roadside stand", "Friends", "Calendar", "Crop mastery", "Level unlocks" };

    public static FarmLifeScreen Instance { get; private set; }
    public bool IsOpen { get; private set; }
    public Tab Current { get; private set; }
    public MenuList List => list;

    private Canvas canvas;
    private MenuList list;
    private int openedFrame;
    private float refresh;

    private void Awake() => Instance = this;

    private void Start()
    {
        canvas = MenuKit.Canvas("Farm Life Journal UI", transform, 64);
        MenuKit.Backdrop(canvas.transform, 0.5f);
        list = new MenuList(canvas.transform, 1100);
        canvas.gameObject.SetActive(false);
        Loc.OnLanguageChanged += OnLanguage;
    }

    private void OnDestroy()
    {
        Loc.OnLanguageChanged -= OnLanguage;
        if (Instance == this) Instance = null;
    }

    private void OnLanguage() { if (IsOpen) Rebuild(); }

    private static bool OpenPressed()
    {
        var kb = Keyboard.current; var pad = Gamepad.current;
        return (kb != null && kb.yKey != null && kb.yKey.wasPressedThisFrame) || (pad != null && pad.dpad.up.wasPressedThisFrame);
    }

    private void Update()
    {
        if (canvas == null) return;
        if (!IsOpen)
        {
            if (!NetSession.Typing && CozyKeys.Free && !GameMenu.IsOpen && (PhotoMode.Instance == null || !PhotoMode.Instance.Active) && OpenPressed()) Show(Tab.Overview);
            return;
        }
        if (Time.frameCount == openedFrame) return;
        var kb = Keyboard.current;
        if (MenuNav.BackOrTab() || (kb != null && kb.yKey != null && kb.yKey.wasPressedThisFrame)) { Close(); return; }
        int h = MenuNav.Horizontal();
        if (MenuNav.PageNext() || h > 0) { SetTab((Tab)(((int)Current + 1) % TabKeys.Length)); return; }
        if (MenuNav.PagePrev() || h < 0) { SetTab((Tab)(((int)Current + TabKeys.Length - 1) % TabKeys.Length)); return; }
        if (list.HandleInput()) return;
        refresh -= Time.unscaledDeltaTime;
        if (refresh <= 0f) { refresh = 0.5f; list.Refresh(); }
    }

    public void Show(Tab tab)
    {
        if (canvas == null) return;
        if (!IsOpen)
        {
            canvas.gameObject.SetActive(true);
            if (UIManager.Instance != null) UIManager.Instance.SetExternalPanel(true);
        }
        IsOpen = true;
        openedFrame = Time.frameCount;
        Current = tab;
        Rebuild(0);
    }

    public void SetTab(Tab tab) { Current = tab; Rebuild(0); }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        canvas.gameObject.SetActive(false);
        if (UIManager.Instance != null) UIManager.Instance.SetExternalPanel(false);
    }

    private void Rebuild(int select = -1)
    {
        int sel = select >= 0 ? select : list.Selected;
        list.Show(TitleText, Rows(Current), () => Glyphs.Apply(Loc.T("[LB] [RB] or Left / Right: tabs   [Enter] Choose   [Esc] or [Y] close")), sel);
    }

    private string TitleText()
    {
        var parts = new List<string>();
        for (int i = 0; i < TabKeys.Length; i++)
        {
            string n = Loc.Raw(TabKeys[i]);
            parts.Add(i == (int)Current ? "[" + n + "]" : n);
        }
        return MenuKit.Title(Loc.T("Farm life journal")) + "\n<size=60%>" + Loc.Display(string.Join("   ", parts.ToArray())) + "</size>";
    }

    private static MenuRow Info(Func<string> text) => new MenuRow { label = () => Loc.Display(text()) };

    /// <summary>The rows of a tab (also used by tests).</summary>
    public List<MenuRow> Rows(Tab tab)
    {
        var rows = new List<MenuRow>();
        switch (tab)
        {
            case Tab.Overview: OverviewRows(rows); break;
            case Tab.Orders: OrderRows(rows); break;
            case Tab.Friends:
                if (Friendship.Instance != null) { var lines = Friendship.Instance.Lines(); for (int i = 0; i < lines.Count; i++) { int k = i; rows.Add(Info(() => Friendship.Instance.Lines()[k])); } }
                rows.Add(Info(() => Loc.Raw("Talk to villagers every day and give them gifts from the farm store (E on a villager).")));
                break;
            case Tab.Calendar: CalendarRows(rows); break;
            case Tab.Mastery:
                if (CropMastery.Instance != null) foreach (var l in CropMastery.Instance.Lines()) { string s = l; rows.Add(Info(() => s)); }
                if (rows.Count == 0) rows.Add(Info(() => Loc.Raw("Harvest crops to earn mastery stars.")));
                break;
            case Tab.Unlocks:
                foreach (var r in FarmLife.Data.levels)
                {
                    int lv = r.level;
                    var names = new List<string>();
                    foreach (var u in FarmLevel.UnlocksAt(lv)) names.Add(FarmLevel.UnlockName(u));
                    rows.Add(Info(() => (FarmLevel.Instance != null && FarmLevel.Instance.Level >= lv ? "(" + Loc.Raw("done") + ") " : "") + Loc.Raw("Level") + " " + lv + ": "
                        + (names.Count > 0 ? string.Join(", ", names.ToArray()) : FarmLife.Describe(r))));
                }
                break;
        }
        rows.Add(MenuRow.Button(() => Loc.T("Close"), Close));
        return rows;
    }

    private void OverviewRows(List<MenuRow> rows)
    {
        var lvl = FarmLevel.Instance;
        rows.Add(Info(() => lvl == null ? "" : Loc.Raw("Farm level") + " " + lvl.Level + "   " + Mathf.FloorToInt(lvl.Xp) + " / " + Mathf.FloorToInt(lvl.NextLevelXp) + " " + Loc.Raw("XP")
            + "   [" + new string('|', Mathf.RoundToInt(lvl.Progress * 20f)) + new string('.', 20 - Mathf.RoundToInt(lvl.Progress * 20f)) + "]"));
        rows.Add(new MenuRow
        {
            label = () => Loc.Display(Loc.Raw("Collect level rewards") + (lvl != null && lvl.Pending.Count > 0 ? " (" + lvl.Pending.Count + ")" : "")),
            confirm = () => { if (lvl != null) lvl.ClaimPending(); Rebuild(); },
            enabled = () => lvl != null && lvl.Pending.Count > 0,
        });
        rows.Add(new MenuRow
        {
            label = () => Loc.Display(Loc.Raw("Collect daily gift") + " - " + Loc.Raw("streak") + " " + DailyGift.Streak + " " + Loc.Raw("days") + ": " + FarmLife.Describe(DailyGift.Reward(DailyGift.Step))),
            confirm = () => { if (DailyGift.Instance != null) DailyGift.Instance.Claim(); Rebuild(); },
            enabled = () => DailyGift.Ready,
        });
        rows.Add(Info(() => FarmBeauty.Instance != null ? FarmBeauty.Instance.Summary().Split('\n')[0] + "  - " + FarmBeauty.Instance.Summary().Split('\n')[2].Trim() : ""));
        rows.Add(Info(() => Festivals.Instance != null && Festivals.Instance.Today != null ? Loc.Raw("Festival today") + ": " + Festivals.Name(Festivals.Instance.Today) : Loc.Raw("No festival today")));
        rows.Add(Info(() => RoadsideStand.Instance != null ? Loc.Raw("Roadside stand") + ": " + RoadsideStand.Instance.ActiveCount + " " + Loc.Raw("waiting") + "   " + Loc.Raw("served") + ": " + RoadsideStand.Instance.Filled : ""));
        rows.Add(MenuRow.Button(() => Loc.T("Photo mode"), () => { Close(); if (PhotoMode.Instance != null) PhotoMode.Instance.Enter(); }));
    }

    private void OrderRows(List<MenuRow> rows)
    {
        var st = RoadsideStand.Instance;
        if (st == null) return;
        for (int i = 0; i < RoadsideStand.SlotCount; i++)
        {
            int slot = i;
            if (!st.Slots[slot].active)
            {
                rows.Add(Info(() => Loc.Raw("Next visitor in about") + " " + Mathf.CeilToInt(st.HoursUntil(slot)) + " " + Loc.Raw("hours")));
                continue;
            }
            rows.Add(new MenuRow
            {
                label = () => { var o = st.Slots[slot]; return Loc.Display(RoadsideStand.VisitorName(o) + " - " + RoadsideStand.TemplateName(o) + ": " + RoadsideStand.Describe(o)); },
                value = () => { var o = st.Slots[slot]; return o.active ? "+" + RoadsideStand.Pay(o) + " " + Loc.T("SAR") : ""; },
                confirm = () => { st.Fulfil(slot); Rebuild(); },
                enabled = () => RoadsideStand.CanFill(st.Slots[slot]),
            });
            rows.Add(new MenuRow
            {
                label = () => Loc.Display("      " + Loc.Raw("Send away") + ": " + RoadsideStand.VisitorName(st.Slots[slot])),
                confirm = () => { st.Skip(slot); Rebuild(); },
                enabled = () => st.Slots[slot].active,
            });
        }
        rows.Add(Info(() => Loc.Raw("Goods come from the farm store. Grey orders need more goods.")));
    }

    private void CalendarRows(List<MenuRow> rows)
    {
        var fest = Festivals.Instance;
        if (fest != null && fest.Today != null)
        {
            rows.Add(Info(() => Loc.Raw("Today") + ": " + Festivals.Name(fest.Today) + " - " + FarmLife.Pick(fest.Today.blurbEn, fest.Today.blurbAr)));
            if (fest.Today.HasCompetition)
                rows.Add(new MenuRow
                {
                    label = () => Loc.Display(Loc.Raw("Enter the contest") + ": " + Festivals.ContestName(fest.Today)),
                    value = () => { string b = fest.BlockReason(); return b != null ? Loc.Display(b) : ""; },
                    confirm = () => { fest.Enter(); Rebuild(); },
                    enabled = () => fest.Today != null && fest.BlockReason() == null,
                });
            if (!string.IsNullOrEmpty(fest.LastResult)) rows.Add(Info(() => fest.LastResult.Replace("\n", "  ")));
        }
        foreach (var l in Festivals.Upcoming(FarmLife.DaysPerSeason * 2)) { string s = l; rows.Add(Info(() => s)); }
        if (fest != null) rows.Add(Info(() => Loc.Raw("Ribbons won") + ": " + fest.Ribbons + "   " + Loc.Raw("Wins") + ": " + fest.Wins));
    }
}
