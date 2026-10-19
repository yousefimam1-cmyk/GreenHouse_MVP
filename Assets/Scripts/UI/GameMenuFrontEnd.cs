using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Choices of a new farm (Career wizard on an empty slot, or the pause menu's New game).</summary>
public class NewFarm
{
    public GameModes.Mode mode = GameModes.Mode.Campaign;
    public string region = Regions.DefaultId;
    public Career.Start start = Career.Start.NewFarmer;
    public Career.Economy economy = Career.Economy.Normal;
    public int freeMoney = GameModes.DefaultFreeMoney;   // index into GameModes.FreeMoney (free play)
    public string challenge = GameModes.Challenges[0].id;
    public int nameIndex;                                // GameModes.FarmNames
    public int slot;                                     // Career slot 1-3 (0 = pause-menu New game, no slot)
}

/// <summary>
/// The front end, laid out like the menus of big farming sims but in Mazraa's own style (MainMenuScene draws the
/// logo, news / tips, slot details and the wizard summary on the right):
///  Title screen ("Press any key / A to start") -> Main menu: Continue (when a farm exists), Career, Multiplayer,
///  Achievements, Settings, Credits, Quit.
///  Career: the 3 save slots as cards (map + flag colour, farm name, mode, day, money, play time, last saved). An empty
///  slot starts the new-farm wizard: Game mode -> Map -> Start (campaign) / Money (free play) / Challenge -> Economy ->
///  Farm name -> Start farming. A used slot offers Load / Delete (with a confirmation).
///  Multiplayer: Host an online game (Unity Relay join code; options: players 2-4, who can join, chat, name) / Join
///  with a code (character picker, no typing) / Browse public games / LAN host / LAN join (digit picker for the IP
///  address) / Players (friend, block, report; host: remove, ban for the session) / Settings. Without the Netcode
///  backend the page says multiplayer is coming in a future update and everything but the settings is greyed out.
/// Everything works with keyboard, mouse and gamepad: Up / Down, Left / Right, Enter / A, Esc / B, LB / RB.
/// </summary>
public partial class GameMenu
{
    public enum FrontScreen { None, Title, Main, Career, NewFarm, Multiplayer, Other }

    /// <summary>Which part of the front end is shown (MainMenuScene lays out the right side from it).</summary>
    public FrontScreen Front
    {
        get
        {
            if (mode != Mode.Main) return FrontScreen.None;
            switch (page)
            {
                case PageId.Title: return FrontScreen.Title;
                case PageId.Root: return FrontScreen.Main;
                case PageId.Career: case PageId.SlotActions: case PageId.ConfirmDelete: return FrontScreen.Career;
                case PageId.GameModePick: case PageId.Map: case PageId.Start: case PageId.FreeMoney: case PageId.ChallengePick:
                case PageId.Economy: case PageId.FarmName: return pending.slot > 0 ? FrontScreen.NewFarm : FrontScreen.Other;
                case PageId.Multiplayer: case PageId.MpJoin: case PageId.MpSettings: case PageId.MpHost: case PageId.MpCode:
                case PageId.MpBrowse: case PageId.MpPlayers: case PageId.MpPlayer: return FrontScreen.Multiplayer;
                default: return FrontScreen.Other;
            }
        }
    }

    /// <summary>Name of the page shown (tests, debugging).</summary>
    public string PageName => page.ToString();
    /// <summary>Choices of the new farm being set up.</summary>
    public NewFarm Pending => pending;
    /// <summary>Career slot whose actions (Load / Delete) are shown.</summary>
    public int SelectedSlot => actionSlot;

    private int actionSlot;
    private readonly int[] ip = { 127, 0, 0, 1 };

    /// <summary>Open the front end on the title screen (game start).</summary>
    public void OpenTitle()
    {
        Open(Mode.Main);
        history.Clear();
        ShowPage(PageId.Title);
    }

    /// <summary>Title screen: any key, A / Start or a click goes on to the main menu.</summary>
    private static bool TitleInput()
    {
        var kb = Keyboard.current;
        var anyKey = kb != null ? kb.anyKey : null;
        var pad = Gamepad.current;
        var mouse = Mouse.current;
        return MenuNav.Confirm()
            || (anyKey != null && anyKey.wasPressedThisFrame)
            || (pad != null && (pad.startButton.wasPressedThisFrame || pad.buttonSouth.wasPressedThisFrame))
            || (mouse != null && mouse.leftButton.wasPressedThisFrame);
    }

    private static string Footer() => UiText.T("[Enter] Choose   [Esc] Back");

    /// <summary>Front-end pages. Returns false for the pages GameMenu.ShowPage builds itself.</summary>
    private bool ShowFrontEndPage(PageId p, int sel, bool fresh)
    {
        switch (p)
        {
            case PageId.Title: list.Show(() => "", new List<MenuRow>(), () => ""); return true;
            case PageId.Career: list.Show(() => MenuKit.Title(UiText.T("Career")), CareerRows(), Footer, sel); return true;
            case PageId.SlotActions: list.Show(() => MenuKit.Title(SlotTitle(actionSlot)), SlotActionRows(), Footer, sel); return true;
            case PageId.ConfirmDelete:
                list.Show(() => MenuKit.Title(UiText.T("Delete this farm?")), new List<MenuRow>
                {
                    MenuRow.Button(() => UiText.T("No, keep it"), Back),
                    MenuRow.Button(() => UiText.T("Yes, delete it for good"), DeleteSlot),
                }, () => UiText.T("[Enter] Choose   [Esc] Back"));
                return true;
            case PageId.GameModePick: list.Show(() => MenuKit.Title(UiText.T("Game mode")), ModeRows(), Footer, sel); return true;
            case PageId.FreeMoney: list.Show(() => MenuKit.Title(UiText.T("Start money")), FreeMoneyRows(), Footer, fresh ? GameModes.DefaultFreeMoney : sel); return true;
            case PageId.ChallengePick: list.Show(() => MenuKit.Title(UiText.T("Choose a challenge")), ChallengeRows(), Footer, sel); return true;
            case PageId.FarmName: list.Show(() => MenuKit.Title(UiText.T("Name your farm")), FarmNameRows(), () => UiText.T("[<- ->] Other name   [Enter] Start farming   [Esc] Back"), sel); return true;
            case PageId.Multiplayer: list.Show(() => MenuKit.Title(UiText.T("Multiplayer")), MultiplayerRows(), MultiplayerFooter, sel); return true;
            case PageId.MpJoin: list.Show(() => MenuKit.Title(UiText.T("Join a game")), JoinRows(), () => UiText.T("[<- ->] Change   LB / RB +-10   [Enter] Choose   [Esc] Back"), sel); return true;
            case PageId.MpSettings: list.Show(() => MenuKit.Title(UiText.T("Multiplayer settings")), MpSettingRows(), () => UiText.T("[<- ->] Change   [Esc] Back"), sel); return true;
            case PageId.MpHost: list.Show(() => MenuKit.Title(UiText.T("Host an online game")), HostOptionRows(), () => UiText.T("[<- ->] Change   [Enter] Choose   [Esc] Back"), sel); return true;
            case PageId.MpCode: list.Show(() => MenuKit.Title(UiText.T("Join with a code")), CodeRows(), () => UiText.T("[<- ->] Change   LB / RB +-10   [Enter] Choose   [Esc] Back"), sel); return true;
            case PageId.MpBrowse: list.Show(() => MenuKit.Title(UiText.T("Public games")), BrowseRows(), MultiplayerFooter, sel); return true;
            case PageId.MpPlayers: list.Show(() => MenuKit.Title(UiText.T("Players")), PlayerRows(), MultiplayerFooter, sel); return true;
            case PageId.MpPlayer: list.Show(() => MenuKit.Title(Mp.Name(MpPlayerName(mpPlayer))), PlayerActionRows(), Footer, sel); return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ main menu

    private List<MenuRow> MainRows()
    {
        var rows = new List<MenuRow>();
        // Quick continue (like FS): the farm that is loaded right now, with its name and day.
        if (HasSave()) rows.Add(MenuRow.Button(ContinueLabel, Close).WithIcon("play"));
        rows.Add(MenuRow.Button(() => UiText.T("Career"), () => Go(PageId.Career)).WithIcon("poi_farm"));
        rows.Add(MenuRow.Button(() => UiText.T("Multiplayer"), () => Go(PageId.Multiplayer)).WithIcon("friends"));
        rows.Add(MenuRow.Button(() => UiText.T("Achievements"), () => Go(PageId.Achievements)).WithIcon("trophy"));
        rows.Add(MenuRow.Button(() => UiText.T("Settings"), () => Go(PageId.Settings)).WithIcon("settings"));
        rows.Add(MenuRow.Button(() => UiText.T("Credits"), () => Go(PageId.Credits)).WithIcon("star"));
        rows.Add(MenuRow.Button(() => UiText.T("Quit"), Quit).WithIcon("quit"));
        return rows;
    }

    private static string ContinueLabel()
    {
        int day = WorldClock.Instance != null ? WorldClock.Instance.Day : SaveData.GetInt("mazraa.day", 1);
        return UiText.T("Continue") + "\n<size=70%><color=#F5F2EB>" + Loc.Display(GameModes.CurrentFarmNameRaw + "  -  " + UiText.Raw("Day") + " " + day) + "</color></size>";
    }

    // ------------------------------------------------------------------ Career: save slot cards

    /// <summary>Flag colour of a map (accent bar of its slot card).</summary>
    public static Color FlagColor(string region)
    {
        switch (region)
        {
            case "najd": return new Color(0f, 0.42f, 0.21f);          // green
            case "nile_delta": return new Color(0.81f, 0.07f, 0.15f); // red
            case "jordan_valley": return new Color(0f, 0.48f, 0.24f);
            case "souss": return new Color(0.76f, 0.15f, 0.18f);
            case "mesopotamia": return new Color(0.16f, 0.16f, 0.16f);
            case "batinah": return new Color(0.86f, 0.09f, 0.11f);
            default: return ControlsTutorial.Gold;
        }
    }

    private static string Currency(RegionDef r, bool arabic)
    {
        if (r == null || string.IsNullOrEmpty(r.currency)) return arabic ? "ريال" : "SAR";
        return arabic && !string.IsNullOrEmpty(r.currencyAr) ? r.currencyAr : r.currency;
    }

    /// <summary>Text of a Career slot card: farm name, map, mode / day, money, play time, last saved (or "Empty slot").</summary>
    public static string SlotCard(int slot)
    {
        SaveSlots.Summary s;
        string head = UiText.Raw("Farm") + " " + slot;
        if (!SaveSlots.Info(slot, out s))
            return "<b>" + Loc.Display(head) + "</b>\n<size=75%><color=#FFFFFFAA>" + UiText.T("Empty slot - start a new farm") + "</color></size>";
        var map = s.Map;
        bool ar = Loc.IsArabic;
        string line1 = head + ":  " + s.FarmNameRaw;
        string line2 = Regions.Name(map) + " (" + Regions.Country(map) + ")  -  " + UiText.Raw(GameModes.ModeName(s.mode));
        string line3 = UiText.Raw("Day") + " " + s.day + "  -  " + s.money.ToString("N0") + " " + Currency(map, ar) + "  -  " + GameModes.PlayTimeRaw(s.playSeconds);
        string line4 = string.IsNullOrEmpty(s.saved) ? "" : UiText.Raw("Last saved") + " " + s.saved;
        return "<b>" + Loc.Display(line1) + "</b>" + (SaveSlots.ActiveSlot == slot ? "   <size=70%><color=#F2A93B>" + UiText.T("Playing") + "</color></size>" : "")
             + "\n<size=75%><color=#F5F2EB>" + Loc.Display(line2) + "</color>\n<color=#FFFFFFCC>" + Loc.Display(line3)
             + (line4 == "" ? "" : "   " + Loc.Display(line4)) + "</color></size>";
    }

    private static string SlotTitle(int slot)
    {
        SaveSlots.Summary s;
        return Loc.Display(UiText.Raw("Farm") + " " + slot + (SaveSlots.Info(slot, out s) ? ":  " + s.FarmNameRaw : ""));
    }

    private List<MenuRow> CareerRows()
    {
        var rows = new List<MenuRow>();
        for (int i = 1; i <= SaveSlots.Count; i++)
        {
            int slot = i;
            rows.Add(new MenuRow
            {
                label = () => SlotCard(slot),
                confirm = () => ChooseSlot(slot),
                accent = () => { SaveSlots.Summary s; return SaveSlots.Info(slot, out s) ? FlagColor(s.region) : new Color(1f, 1f, 1f, 0.15f); },
                height = 118f,
            });
        }
        rows.Add(MenuRow.Button(() => UiText.T("Back"), Back));
        return rows;
    }

    /// <summary>Career: a used slot opens Load / Delete, an empty slot starts the new-farm wizard.</summary>
    public void ChooseSlot(int slot)
    {
        actionSlot = slot;
        if (SaveSlots.HasSlot(slot)) { Go(PageId.SlotActions); return; }
        pending = new NewFarm { slot = slot };
        Go(PageId.GameModePick);
    }

    private List<MenuRow> SlotActionRows()
    {
        return new List<MenuRow>
        {
            MenuRow.Button(() => UiText.T("Load"), () => LoadSlot(actionSlot)),
            MenuRow.Button(() => UiText.T("Delete"), () => Go(PageId.ConfirmDelete)),
            MenuRow.Button(() => UiText.T("Back"), Back),
        };
    }

    private void DeleteSlot()
    {
        bool ok = SaveSlots.Delete(actionSlot);
        if (UIManager.Instance != null) UIManager.Instance.ShowAlertMessage(ok ? Loc.Display(UiText.Raw("Farm deleted") + " (" + actionSlot + ")") : UiText.T("Could not delete"));
        BackTo(PageId.Career);
    }

    // ------------------------------------------------------------------ new-farm wizard

    private static string TwoLines(string title, string desc) => title + "\n<size=70%><color=#FFFFFFAA>" + desc + "</color></size>";

    private List<MenuRow> ModeRows()
    {
        var rows = new List<MenuRow>();
        foreach (GameModes.Mode m in Enum.GetValues(typeof(GameModes.Mode)))
        {
            var gm = m;
            rows.Add(MenuRow.Button(() => TwoLines(UiText.T(GameModes.ModeName(gm)), UiText.T(GameModes.ModeDesc(gm))), () => { pending.mode = gm; Go(PageId.Map); }));
        }
        rows.Add(MenuRow.Button(() => UiText.T("Back"), Back));
        return rows;
    }

    /// <summary>Page after the map: the campaign picks a start, free play its money, a challenge is chosen.</summary>
    private PageId NextAfterMap()
    {
        if (pending.slot <= 0) return PageId.Start;   // pause-menu New game (campaign)
        return pending.mode == GameModes.Mode.FreePlay ? PageId.FreeMoney : pending.mode == GameModes.Mode.Challenge ? PageId.ChallengePick : PageId.Start;
    }

    private static string MoneyLabel(int money) => money < 0 ? UiText.T("Unlimited money") : Loc.Display(money.ToString("N0") + " " + Loc.Raw("SAR"));

    private List<MenuRow> FreeMoneyRows()
    {
        var rows = new List<MenuRow>();
        for (int i = 0; i < GameModes.FreeMoney.Length; i++)
        {
            int index = i;
            rows.Add(MenuRow.Button(() => MoneyLabel(GameModes.FreeMoney[index]), () => { pending.freeMoney = index; Go(PageId.Economy); }));
        }
        rows.Add(MenuRow.Button(() => UiText.T("Back"), Back));
        return rows;
    }

    private List<MenuRow> ChallengeRows()
    {
        var rows = new List<MenuRow>();
        foreach (var ch in GameModes.Challenges)
        {
            var c = ch;
            rows.Add(MenuRow.Button(() => TwoLines(UiText.T(c.name), UiText.T(c.desc) + "  " + GameModes.GoalText(c)), () => { pending.challenge = c.id; Go(PageId.FarmName); }));
        }
        rows.Add(MenuRow.Button(() => UiText.T("Back"), Back));
        return rows;
    }

    private List<MenuRow> FarmNameRows()
    {
        return new List<MenuRow>
        {
            // Left / Right picks another generated name; Enter keeps it and starts the farm.
            new MenuRow { label = () => UiText.T("Farm name"), value = () => GameModes.FarmNameOf(pending.nameIndex),
                          adjust = d => pending.nameIndex = ((pending.nameIndex + d) % GameModes.FarmNames.Length + GameModes.FarmNames.Length) % GameModes.FarmNames.Length,
                          confirm = StartPending },
            MenuRow.Button(() => UiText.T("Start farming"), StartPending),
            MenuRow.Button(() => UiText.T("Back"), Back),
        };
    }

    private void StartPending() => StartCareer(pending);

    /// <summary>Summary of the new farm being set up (right side of the wizard).</summary>
    public string WizardSummary()
    {
        var f = pending;
        var sb = new System.Text.StringBuilder(256);
        sb.Append("<b><color=#F5F2EB>").Append(Loc.T("New farm")).Append("</color></b>  <size=80%>(").Append(Loc.Display(Loc.Raw("Farm") + " " + f.slot)).Append(")</size>\n\n");
        Line(sb, "Game mode", Loc.Raw(GameModes.ModeName(f.mode)));
        if (page != PageId.GameModePick)
        {
            Line(sb, "Map", Regions.Name(Regions.Find(f.region)));
            if (f.mode == GameModes.Mode.Challenge)
            {
                var c = GameModes.FindChallenge(f.challenge) ?? GameModes.Challenges[0];
                Line(sb, "Challenge", Loc.Raw(c.name));
                sb.Append("<size=85%>").Append(GameModes.GoalText(c)).Append("</size>\n");
            }
            else if (f.mode == GameModes.Mode.FreePlay) Line(sb, "Start money", GameModes.FreeMoneyAt(f.freeMoney) < 0 ? Loc.Raw("Unlimited money") : GameModes.FreeMoneyAt(f.freeMoney).ToString("N0") + " " + Loc.Raw("SAR"));
            else Line(sb, "Start", Loc.Raw(Career.StartName(f.start)));
            if (f.mode != GameModes.Mode.Challenge && (page == PageId.Economy || page == PageId.FarmName)) Line(sb, "Economy", Loc.Raw(Career.EconomyName(f.economy)));
            if (page == PageId.FarmName) Line(sb, "Farm name", GameModes.FarmNameRawOf(f.nameIndex));
        }
        return sb.ToString();
    }

    private static void Line(System.Text.StringBuilder sb, string label, string value) => sb.Append(Loc.Display(Loc.Raw(label) + ": " + value)).Append('\n');

    // ------------------------------------------------------------------ Multiplayer

    /// <summary>True when co-op can be played in this build (the Netcode backend is compiled in).</summary>
    public static bool MultiplayerAvailable => NetSession.Backend.Available && MultiplayerManager.Instance != null;
    /// <summary>True when internet play (Unity Relay / Lobby) is compiled in too.</summary>
    public static bool OnlineAvailable => MultiplayerAvailable && MultiplayerManager.OnlineAvailable;

    private static bool MpIdle => MultiplayerManager.Instance != null && MultiplayerManager.Instance.Busy == MultiplayerManager.OnlineStep.Idle;
    private static bool CanStart => MpIdle && !NetSession.IsOnline && !NetSession.Backend.Connecting;

    private readonly char[] joinCode = { 'A', 'A', 'A', 'A', 'A', 'A' };
    private ulong mpPlayer;
    private int mpSeenVersion = -1;
    private bool mpWasClient;
    private int mpWatchFrame = -10;
    private const string LastCodeKey = "mazraa.mp.lastCode";

    /// <summary>Join code picked on the "Join with a code" page.</summary>
    public string PickedCode => new string(joinCode);

    private List<MenuRow> MultiplayerRows()
    {
        var rows = new List<MenuRow>();
        var mp = MultiplayerManager.Instance;
        if (!MultiplayerAvailable)
            rows.Add(new MenuRow { label = () => TwoLines(UiText.T("Multiplayer is coming in a future update"), UiText.T("Farm together with up to 3 friends - stay tuned!")), enabled = () => false });
        else if (mp.Busy != MultiplayerManager.OnlineStep.Idle)
        {
            rows.Add(new MenuRow { label = () => TwoLines(UiText.T("Please wait..."), Mp.Tr(mp.Status)), enabled = () => false });
            rows.Add(MenuRow.Button(() => UiText.T("Cancel"), () => { mp.CancelOnline(); }));
        }
        else if (NetSession.IsHost && mp.IsOnlineSession)
            rows.Add(new MenuRow { label = () => TwoLines(UiText.T("Join code") + ":  <size=130%><b>" + JoinCodes.Pretty(mp.JoinCode) + "</b></size>", UiText.T("Friends choose Multiplayer > Join with a code")), enabled = () => false, height = 96f });

        bool online = OnlineAvailable;
        var hostOnline = new MenuRow { label = () => UiText.T("Host an online game"), confirm = () => Go(PageId.MpHost), enabled = () => online && CanStart };
        var joinCodeRow = new MenuRow { label = () => UiText.T("Join with a code"), confirm = OpenCode, enabled = () => online && CanStart };
        var browse = new MenuRow { label = () => UiText.T("Browse public games"), confirm = OpenBrowse, enabled = () => online && CanStart };
        if (MultiplayerAvailable && !online)
            foreach (var r in new[] { hostOnline, joinCodeRow, browse }) r.value = () => UiText.T("Not in this version");
        rows.Add(hostOnline);
        rows.Add(joinCodeRow);
        rows.Add(browse);
        rows.Add(new MenuRow { label = () => UiText.T("Host on local network (LAN)"), confirm = HostGame, enabled = () => MultiplayerAvailable && CanStart });
        rows.Add(new MenuRow { label = () => UiText.T("Join on local network (LAN)"), confirm = OpenJoin, enabled = () => MultiplayerAvailable && CanStart });
        if (MultiplayerAvailable && (NetSession.IsOnline || NetSession.Backend.Connecting))
        {
            if (NetSession.IsOnline) rows.Add(MenuRow.Button(() => UiText.T("Players"), () => Go(PageId.MpPlayers)));
            rows.Add(MenuRow.Button(() => UiText.T(NetSession.IsHost ? "Stop hosting" : NetSession.IsClient ? "Leave the farm" : "Cancel"), () => { MultiplayerManager.Instance.Leave(); list.Refresh(); }));
            if (NetSession.IsOnline) rows.Add(MenuRow.Button(() => UiText.T("Continue farming"), Close));
        }
        rows.Add(MenuRow.Button(() => UiText.T("Multiplayer settings"), () => Go(PageId.MpSettings)));
        rows.Add(MenuRow.Button(() => UiText.T("Back"), Back));
        return rows;
    }

    private static string MultiplayerFooter()
    {
        var mp = MultiplayerManager.Instance;
        string status = MultiplayerAvailable && mp != null && !string.IsNullOrEmpty(mp.Status) ? Mp.Tr(mp.Status) + "\n" : "";
        return status + UiText.T("[Enter] Choose   [Esc] Back");
    }

    /// <summary>Multiplayer pages: rebuild when the co-op state changes (connected, code ready, error...); close the
    /// menu once we have joined a farm.</summary>
    private void MpWatch()
    {
        if (Front != FrontScreen.Multiplayer) return;
        var mp = MultiplayerManager.Instance;
        if (mp == null) return;
        if (Time.frameCount - mpWatchFrame > 1) mpWasClient = NetSession.IsClient;   // menu just opened: no transition yet
        mpWatchFrame = Time.frameCount;
        bool client = NetSession.IsClient;
        if (client && !mpWasClient) { mpWasClient = true; Close(); return; }       // joined a farm: play
        mpWasClient = client;
        if (mp.StateVersion != mpSeenVersion) { mpSeenVersion = mp.StateVersion; ShowPage(page, false, page == PageId.MpBrowse ? 0 : list.Selected); }   // new games list: first farm selected
    }

    // ---- online host options

    private List<MenuRow> HostOptionRows()
    {
        return new List<MenuRow>
        {
            MenuRow.Setting(() => UiText.T("Players"), () => UiText.T("Up to {0}", MultiplayerManager.MaxPlayers),
                            d => MultiplayerManager.MaxPlayers = 2 + (((MultiplayerManager.MaxPlayers - 2 + d) % 3) + 3) % 3),
            MenuRow.Setting(() => UiText.T("Who can join"), () => UiText.T(MultiplayerManager.VisibilityLabel(MultiplayerManager.Visibility)), StepVisibility),
            MenuRow.Setting(() => UiText.T("Chat"), () => UiText.T(ChatSafety.ChatModeLabel(ChatSafety.Chat)), StepChat),
            MenuRow.Setting(() => UiText.T("Your name"), () => Mp.Name(PlayerNames.Current), StepName),
            MenuRow.Button(() => UiText.T("Start hosting"), HostOnlineGame),
            MenuRow.Button(() => UiText.T("Back"), Back),
        };
    }

    private static void StepVisibility(int d) => MultiplayerManager.Visibility = (OnlineVisibility)((((int)MultiplayerManager.Visibility + d) % 3 + 3) % 3);
    private static void StepChat(int d) => ChatSafety.Chat = (ChatSafety.ChatMode)((((int)ChatSafety.Chat + d) % 3 + 3) % 3);
    private static void StepName(int d)
    {
        PlayerNames.Index = PlayerNames.Index + d;
        if (MultiplayerManager.Instance != null) MultiplayerManager.Instance.UseGeneratedName();
    }

    private void HostOnlineGame()
    {
        var mp = MultiplayerManager.Instance;
        if (mp == null || !OnlineAvailable) return;
        mp.UseGeneratedName();
        mp.HostOnline();
        BackTo(PageId.Multiplayer);   // shows "Please wait", then the join code
    }

    // ---- join with a code (character picker)

    private void OpenCode()
    {
        string last = JoinCodes.Normalize(PlayerPrefs.GetString(LastCodeKey, ""));
        for (int i = 0; i < JoinCodes.Length; i++) joinCode[i] = JoinCodes.IsValid(last) ? last[i] : 'A';
        Go(PageId.MpCode);
    }

    private List<MenuRow> CodeRows()
    {
        var rows = new List<MenuRow>();
        for (int i = 0; i < JoinCodes.Length; i++)
        {
            int at = i;
            rows.Add(MenuRow.Setting(() => Loc.Display(UiText.Raw("Character") + " " + (at + 1)), () => "<b>" + joinCode[at] + "</b>", d => joinCode[at] = JoinCodes.Step(joinCode[at], d)));
        }
        rows.Add(MenuRow.Button(() => UiText.T("Join") + "  <b>" + JoinCodes.Pretty(PickedCode) + "</b>", JoinCodeGame));
        rows.Add(MenuRow.Button(() => UiText.T("Back"), Back));
        return rows;
    }

    private void JoinCodeGame()
    {
        var mp = MultiplayerManager.Instance;
        if (mp == null || !OnlineAvailable) return;
        PlayerPrefs.SetString(LastCodeKey, PickedCode); PlayerPrefs.Save();
        mp.UseGeneratedName();
        if (mp.JoinWithCode(PickedCode)) BackTo(PageId.Multiplayer);
        else list.Refresh();
    }

    // ---- public games

    private void OpenBrowse()
    {
        var mp = MultiplayerManager.Instance;
        if (mp == null || !OnlineAvailable) return;
        mp.BrowsePublic();
        Go(PageId.MpBrowse);
    }

    private List<MenuRow> BrowseRows()
    {
        var rows = new List<MenuRow>();
        var mp = MultiplayerManager.Instance;
        if (mp != null && mp.Busy == MultiplayerManager.OnlineStep.Browsing)
            rows.Add(new MenuRow { label = () => UiText.T("Looking for public farms..."), enabled = () => false });
        else if (mp != null)
        {
            foreach (var g in mp.PublicGames)
            {
                var game = g;
                rows.Add(MenuRow.Button(() => TwoLines(Loc.Display(game.farmName), Loc.Display(Regions.Name(Regions.Find(game.map)) + "  -  " + UiText.Raw("Players") + " " + game.players + "/" + game.maxPlayers)), () => JoinPublic(game)));
            }
            if (mp.PublicGames.Count == 0)
                rows.Add(new MenuRow { label = () => UiText.T("No public farms right now - host one, or join a friend with a code"), enabled = () => false });
        }
        rows.Add(new MenuRow { label = () => UiText.T("Refresh"), confirm = () => { if (MultiplayerManager.Instance != null) MultiplayerManager.Instance.BrowsePublic(); }, enabled = () => MpIdle });
        rows.Add(MenuRow.Button(() => UiText.T("Back"), Back));
        return rows;
    }

    private void JoinPublic(LobbyInfo game)
    {
        var mp = MultiplayerManager.Instance;
        if (mp == null || game == null) return;
        mp.UseGeneratedName();
        if (mp.JoinWithCode(game.code)) BackTo(PageId.Multiplayer);
    }

    // ---- players (host tools: kick / ban; everyone: friend, block, report)

    private List<MenuRow> PlayerRows()
    {
        var rows = new List<MenuRow>();
        var mp = MultiplayerManager.Instance;
        if (mp != null)
            foreach (var kv in new List<KeyValuePair<ulong, string>>(mp.Players))
            {
                ulong id = kv.Key; string name = kv.Value;
                bool me = id == NetSession.Backend.LocalId;
                string tag = (id == 0UL ? "  (" + Mp.Tr("host") + ")" : "") + (me ? "  (" + Mp.Tr("you") + ")" : "") + (ChatSafety.IsFriend(name) ? "  *" : "");
                rows.Add(new MenuRow { label = () => Mp.Name(name) + tag, confirm = () => { mpPlayer = id; Go(PageId.MpPlayer); }, enabled = () => !me });
            }
        rows.Add(MenuRow.Button(() => UiText.T("Back"), Back));
        return rows;
    }

    private string MpPlayerName(ulong id)
    {
        var mp = MultiplayerManager.Instance;
        string n;
        return mp != null && mp.Players.TryGetValue(id, out n) ? n : "";
    }

    private List<MenuRow> PlayerActionRows()
    {
        var mp = MultiplayerManager.Instance;
        string name = MpPlayerName(mpPlayer);
        var rows = new List<MenuRow>();
        rows.Add(MenuRow.Button(() => UiText.T(ChatSafety.IsFriend(name) ? "Unfriend" : "Friend"), () => ChatSafety.SetFriend(name, !ChatSafety.IsFriend(name))));
        rows.Add(MenuRow.Button(() => UiText.T(ChatSafety.IsBlocked(name) ? "Unblock" : "Block"), () =>
        {
            if (ChatSafety.IsBlocked(name)) ChatSafety.SetBlocked(name, false);
            else if (mp != null) { mp.Block(mpPlayer, name); if (NetSession.IsHost) BackTo(PageId.MpPlayers); }
        }));
        rows.Add(MenuRow.Button(() => UiText.T("Report"), () =>
        {
            if (mp == null) return;
            Application.OpenURL(mp.Report(mpPlayer, name, "Reported from the multiplayer menu"));
            BackTo(PageId.MpPlayers);
        }));
        if (NetSession.IsHost)
        {
            rows.Add(MenuRow.Button(() => UiText.T("Remove from the farm"), () => { if (mp != null) mp.Kick(mpPlayer); BackTo(PageId.MpPlayers); }));
            rows.Add(MenuRow.Button(() => UiText.T("Ban for this session"), () => { if (mp != null) mp.Ban(mpPlayer); BackTo(PageId.MpPlayers); }));
        }
        rows.Add(MenuRow.Button(() => UiText.T("Back"), Back));
        return rows;
    }

    // ---- LAN

    private void HostGame()
    {
        var mp = MultiplayerManager.Instance;
        if (mp == null || !MultiplayerAvailable) return;
        if (mp.Host()) Close();   // play on: friends join this farm
        else list.Refresh();
    }

    private void OpenJoin()
    {
        var mp = MultiplayerManager.Instance;
        string last = mp != null ? mp.LastAddress : "";
        string[] parts = (last ?? "").Split('.');
        for (int i = 0; i < 4; i++)
        {
            int v;
            ip[i] = parts.Length == 4 && int.TryParse(parts[i], out v) ? Mathf.Clamp(v, 0, 255) : (i == 0 ? 192 : i == 1 ? 168 : i == 2 ? 1 : 2);
        }
        Go(PageId.MpJoin);
    }

    /// <summary>IP address picked on the Join page.</summary>
    public string JoinAddress => ip[0] + "." + ip[1] + "." + ip[2] + "." + ip[3];

    private void StepIp(int part, int d) => ip[part] = ((ip[part] + d) % 256 + 256) % 256;

    private List<MenuRow> JoinRows()
    {
        var rows = new List<MenuRow>();
        for (int i = 0; i < 4; i++)
        {
            int part = i;
            rows.Add(MenuRow.Setting(() => Loc.Display(UiText.Raw("Host address, part") + " " + (part + 1)), () => ip[part].ToString(), d => StepIp(part, d)));
        }
        rows.Add(MenuRow.Button(() => UiText.T("Connect") + "  " + JoinAddress, JoinGame));
        rows.Add(MenuRow.Button(() => UiText.T("Back"), Back));
        return rows;
    }

    /// <summary>LB / RB (Page Up / Down): +-10 on the selected part of the IP address, +-10 characters on the join code.
    /// Returns true when handled.</summary>
    private bool IpPageInput()
    {
        bool ipPage = page == PageId.MpJoin && list.Selected <= 3;
        bool codePage = page == PageId.MpCode && list.Selected < JoinCodes.Length;
        if (!ipPage && !codePage) return false;
        int d = PageInput() * 10;
        if (d == 0) return false;
        if (ipPage) StepIp(list.Selected, d);
        else joinCode[list.Selected] = JoinCodes.Step(joinCode[list.Selected], d);
        list.Refresh();
        return true;
    }

    private void JoinGame()
    {
        var mp = MultiplayerManager.Instance;
        if (mp == null || !MultiplayerAvailable) return;
        if (mp.Join(JoinAddress)) Close();
        else list.Refresh();
    }

    // ---- settings

    private List<MenuRow> MpSettingRows()
    {
        return new List<MenuRow>
        {
            MenuRow.Setting(() => UiText.T("Chat"), () => UiText.T(ChatSafety.ChatModeLabel(ChatSafety.Chat)), StepChat),
            MenuRow.Setting(() => UiText.T("Who can join"), () => UiText.T(MultiplayerManager.VisibilityLabel(MultiplayerManager.Visibility)), StepVisibility),
            MenuRow.Setting(() => UiText.T("Players"), () => UiText.T("Up to {0}", MultiplayerManager.MaxPlayers),
                            d => MultiplayerManager.MaxPlayers = 2 + (((MultiplayerManager.MaxPlayers - 2 + d) % 3) + 3) % 3),
            MenuRow.Setting(() => UiText.T("Your name"), () => Mp.Name(PlayerNames.Current), StepName),
            MenuRow.Button(() => UiText.T("New random name"), () => { PlayerNames.Randomize(); if (MultiplayerManager.Instance != null) MultiplayerManager.Instance.UseGeneratedName(); }),
            MenuRow.Button(() => UiText.T("Back"), Back),
        };
    }
}
