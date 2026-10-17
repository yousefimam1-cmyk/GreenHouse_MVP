// Front end (GameMenuFrontEnd.cs, MainMenuScene.cs) and play modes (GameModes.cs): title screen -> main menu, Career
// save-slot cards, the new-farm wizard on an empty slot (campaign / free play / challenge), load and delete with a
// confirmation, the Multiplayer screen (greyed without Netcode, digit-picker join with a backend), gamepad navigation
// through every screen and Arabic. Runs in its own world at the end of Program.Main.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;

static class FrontEndTests
{
    static Keyboard kb;
    static Gamepad pad;
    static GameMenu Menu => GameMenu.Instance;
    static MainMenuScene Scene => MainMenuScene.Instance;

    static void Key(KeyControl k) { k.wasPressedThisFrame = true; k.isPressed = true; FakeEngine.Tick(0.02f); k.wasPressedThisFrame = false; k.isPressed = false; FakeEngine.Tick(0.02f); }
    static void Pad(ButtonControl b) { b.wasPressedThisFrame = true; b.isPressed = true; FakeEngine.Tick(0.02f); b.wasPressedThisFrame = false; b.isPressed = false; FakeEngine.Tick(0.02f); }
    static List<string> Labels() => Menu.List.Rows.Select(r => r.label()).ToList();
    static string Sel() => Menu.List.Rows[Menu.List.Selected].label();
    static bool HasArabic(string s) => System.Text.RegularExpressions.Regex.IsMatch(s ?? "", "[؀-ۿﭐ-﷿ﹰ-﻿]");

    /// <summary>D-pad down until the selected row's label contains the text (gamepad navigation). True when found.</summary>
    static bool PadTo(string text)
    {
        for (int i = 0; i < 30; i++)
        {
            if (Sel().Contains(text)) return true;
            Pad(pad.dpad.down);
        }
        return Sel().Contains(text);
    }

    /// <summary>Arrow keys down until the selected row's label contains the text (keyboard navigation).</summary>
    static bool KeyTo(string text)
    {
        for (int i = 0; i < 30; i++)
        {
            if (Sel().Contains(text)) return true;
            Key(kb.downArrowKey);
        }
        return Sel().Contains(text);
    }

    static void ToMain()
    {
        if (!GameMenu.IsOpen) Menu.Open(GameMenu.Mode.Main);
        if (Menu.CurrentMode != GameMenu.Mode.Main) { Menu.Close(); Menu.Open(GameMenu.Mode.Main); }
        FakeEngine.Tick(0.02f); FakeEngine.Tick(0.02f);
    }

    public static void Run()
    {
        Console.WriteLine("Front end: title, main menu, Career slots, new-farm wizard, play modes, multiplayer");
        Loc.SetLanguage(Language.English);
        for (int i = 1; i <= SaveSlots.Count; i++) SaveSlots.Delete(i);
        Program.BuildWorld();
        GameMenu.ShowOnStart = true;
        kb = new Keyboard();
        foreach (var f in typeof(Keyboard).GetFields()) if (f.FieldType == typeof(KeyControl)) f.SetValue(kb, new KeyControl());
        Keyboard.current = kb;
        pad = new Gamepad { name = "XInputControllerWindows", displayName = "Xbox Controller" };
        Gamepad.current = pad;
        Mouse.current = null;
        GameInput.ForceDevice(true, false);
        int e0 = FakeEngine.Errors.Count;
        FakeEngine.Run(1f);

        // ---- title screen
        T.Check("Front end: the game opens on the title screen (paused, menu column hidden)",
            Menu.Front == GameMenu.FrontScreen.Title && Time.timeScale == 0f && !Menu.List.Panel.gameObject.activeSelf, Menu.PageName);
        FakeEngine.Run(0.5f);
        T.Check("Title: big centred logo, 'press A' prompt, studio + version at the bottom",
            Scene.LogoCentered && Scene.PromptText.Contains("to start") && Scene.VersionText.Contains("Almadarek Games") && Scene.VersionText.Contains("v" + Application.version),
            Scene.PromptText + " | " + Scene.VersionText);
        for (int i = 0; i < 400 && LoadingScreen.Visible; i++) FakeEngine.Tick(0.1f);
        T.Check("Title: the loading-screen theme keeps playing under the title", !LoadingScreen.Visible && LoadingScreen.MusicPlaying && Menu.Front == GameMenu.FrontScreen.Title);
        GameInput.ForceDevice(false, false);
        FakeEngine.Run(0.3f);
        T.Check("Title: keyboard prompt says press any key", Scene.PromptText == "Press any key to start", Scene.PromptText);
        Key(kb.anyKey);
        T.Check("Title -> main menu on any key", Menu.Front == GameMenu.FrontScreen.Main && Menu.List.Panel.gameObject.activeSelf && !Scene.LogoCentered);
        var labels = Labels();
        bool cont = labels[0].StartsWith("Continue");
        T.Check("Main menu: [Continue], Career, Multiplayer, Achievements, Settings, Credits, Quit",
            labels.Skip(cont ? 1 : 0).SequenceEqual(new[] { "Career", "Multiplayer", "Achievements", "Settings", "Credits", "Quit" }), string.Join(",", labels));
        FakeEngine.Run(0.3f);
        string news1 = Scene.NewsText;
        T.Check("Main menu: news panel with a tip and the last-played farm", news1.Contains("News and tips") && news1.Contains("Tip") && news1.Contains("Last played"), news1);
        T.Check("Main menu: Egypt Nile Delta DLC card shows ownership", Scene.DlcText.Contains("Egypt: Nile Delta DLC") && Scene.DlcText.Contains(Dlc.Owns("nile_delta") ? "Owned" : "Not owned"), Scene.DlcText);
        FakeEngine.Run(MainMenuScene.TipSeconds + 0.5f);
        T.Check("Main menu: the tip rotates", Scene.NewsText != news1 && Scene.NewsText.Contains("Tip"));
        Pad(pad.buttonEast);
        T.Check("B on the main menu goes back to the title", Menu.Front == GameMenu.FrontScreen.Title);
        Pad(pad.buttonSouth);
        T.Check("A on the title opens the main menu", Menu.Front == GameMenu.FrontScreen.Main);

        // ---- gamepad through the simple pages (Achievements, Settings, Credits): A opens, B returns to the same row
        bool pages = true; string bad = "";
        foreach (var name in new[] { "Achievements", "Settings", "Credits" })
        {
            pages &= PadTo(name);
            Pad(pad.buttonSouth);
            bool opened = Menu.PageName == name || (name == "Settings" && Menu.OnSettingsHub);
            Pad(pad.buttonEast);
            bool back = Menu.Front == GameMenu.FrontScreen.Main && Sel() == name;
            if (!opened || !back) { pages = false; bad += name + " "; }
        }
        T.Check("Gamepad: Achievements, Settings and Credits open with A and B returns to the same row", pages, bad);

        // ---- Career: three slot cards
        T.Check("Gamepad: D-pad to Career", PadTo("Career"));
        Pad(pad.buttonSouth);
        labels = Labels();
        T.Check("Career: 3 slot cards (all empty) + Back", Menu.Front == GameMenu.FrontScreen.Career && labels.Count == 4 && labels.Take(3).All(l => l.Contains("Empty slot")) && labels[3] == "Back",
            string.Join(" | ", labels));
        FakeEngine.Run(0.3f);
        T.Check("Career: the right panel explains the selected empty slot", Scene.InfoText.Contains("Farm 1") && Scene.InfoText.Contains("Empty slot"), Scene.InfoText);

        // ---- wizard on slot 1 with the gamepad: Free play, Nile Delta, unlimited money, Normal, second name
        Pad(pad.buttonSouth);
        T.Check("Empty slot starts the new-farm wizard: Game mode (Campaign / Free play / Challenge)",
            Menu.PageName == "GameModePick" && Menu.Pending.slot == 1 && Labels().Count == 4 && Labels()[0].StartsWith("Campaign") && Labels()[1].StartsWith("Free play") && Labels()[2].StartsWith("Challenge"),
            string.Join(" | ", Labels()));
        PadTo("Free play"); Pad(pad.buttonSouth);
        T.Check("Wizard: map choice (Najd, Egypt DLC, coming-soon maps locked)", Menu.PageName == "Map" && Labels().Count == Regions.All.Length + 1 && !Menu.List.Rows[2].IsEnabled);
        PadTo("Nile"); Pad(pad.buttonSouth);
        T.Check("Wizard (free play): start money choice incl. Unlimited money, 100,000 preselected",
            Menu.PageName == "FreeMoney" && Labels().Any(l => l == "Unlimited money") && Sel().Contains("100,000"), Menu.PageName + " " + Sel());
        PadTo("Unlimited"); Pad(pad.buttonSouth);
        T.Check("Wizard: economy difficulty (Normal preselected)", Menu.PageName == "Economy" && Menu.List.Selected == 1);
        Pad(pad.buttonSouth);
        T.Check("Wizard: farm name page with the map's default name", Menu.PageName == "FarmName" && Menu.List.Rows[0].value() == "Nile Breeze Farm", Menu.List.Rows[0].value());
        Pad(pad.dpad.right);
        T.Check("Wizard: left / right picks another generated name", Menu.List.Rows[0].value() == "Barakah Farm", Menu.List.Rows[0].value());
        FakeEngine.Run(0.3f);
        T.Check("Wizard: the right panel sums up the new farm", Scene.InfoText.Contains("Free play") && Scene.InfoText.Contains("Nile Delta") && Scene.InfoText.Contains("Unlimited money") && Scene.InfoText.Contains("Barakah Farm"), Scene.InfoText);
        Pad(pad.buttonSouth);   // Enter on the name keeps it and starts the farm
        FakeEngine.Run(0.5f);
        var eco = EconomyManager.Instance; var mm = MissionManager.Instance; var camp = CampaignManager.Instance;
        T.Check("Free play farm started on the Nile Delta in slot 1", !GameMenu.IsOpen && GameModes.Current == GameModes.Mode.FreePlay && Regions.CurrentId == "nile_delta"
            && SaveSlots.HasSlot(1) && SaveSlots.ActiveSlot == 1 && GameModes.CurrentFarmName == "Barakah Farm", GameModes.Current + " " + Regions.CurrentId + " " + SaveSlots.ActiveSlot);
        T.Check("Free play: unlimited money", GameModes.UnlimitedMoney && eco.Money == GameModes.UnlimitedAmount, eco.Money + "");
        eco.Spend(5000);
        FakeEngine.Tick(0.02f);
        T.Check("Free play: spending never runs the money down", eco.Money == GameModes.UnlimitedAmount, eco.Money + "");
        T.Check("Free play: no mission chain, no story chapters (and not 'all done')", mm.Missions.Length == 0 && mm.Current == null && !mm.AllDone && camp.Chapters.Length == 0);
        int topLevel = FarmLife.Data.decor.Max(d => d.minLevel);
        var topDecor = FarmLife.Data.decor.First(d => d.minLevel == topLevel);
        T.Check("Free play: everything unlocked (top farm-level decor, all farmland)", FarmLevel.Instance.Level < topLevel && FarmLevel.IsUnlocked(topDecor.id) && LandOffice.Instance.Owned == LandOffice.Parcels.Length,
            topDecor.id + " L" + topLevel);
        FakeEngine.Run(3f);
        T.Check("Free play: no story popups (intro card, chapter cards)", GameObject.Find("Story Intro") == null && !camp.CardOpen && SaveData.GetInt("mazraa.introSeen", 0) == 1);
        T.Check("Free play: HUD says free play", HudLines.Status.Select(f => f()).Any(s => s != null && s.Contains("unlimited money")));
        FakeEngine.Run(65f, 0.5f);
        T.Check("Play time counts while farming", GameModes.PlaySeconds >= 60f, GameModes.PlaySeconds + "");
        SaveSlots.Save(1);
        SaveSlots.Summary s1;
        T.Check("Slot info stores map, mode, farm name and play time", SaveSlots.Info(1, out s1) && s1.region == "nile_delta" && s1.mode == GameModes.Mode.FreePlay && s1.farmEn == "Barakah Farm"
            && s1.farmAr == "مزرعة البركة" && s1.playSeconds >= 60f && s1.money == GameModes.UnlimitedAmount, s1.region + " " + s1.mode + " " + s1.farmEn + " " + s1.playSeconds);

        // ---- campaign on slot 2 with the keyboard: Najd, Farm manager, Hard, default name
        ToMain();
        T.Check("Main menu: Continue shows the farm name and day", Labels()[0].StartsWith("Continue") && Labels()[0].Contains("Barakah Farm") && Labels()[0].Contains("Day"), Labels()[0]);
        KeyTo("Career"); Key(kb.enterKey);
        Key(kb.downArrowKey); Key(kb.enterKey);   // slot 2
        T.Check("Keyboard: slot 2 opens the wizard", Menu.PageName == "GameModePick" && Menu.Pending.slot == 2);
        Key(kb.enterKey);                           // Campaign
        KeyTo("Najd"); Key(kb.enterKey);
        T.Check("Wizard (campaign): start choice", Menu.PageName == "Start" && Labels().Count == 4);
        KeyTo("Farm manager"); Key(kb.enterKey);
        Key(kb.downArrowKey); Key(kb.enterKey);     // Normal -> Hard
        T.Check("Wizard (campaign): name page", Menu.PageName == "FarmName" && Menu.List.Rows[0].value() == "Palm Oasis Farm");
        KeyTo("Start farming"); Key(kb.enterKey);
        FakeEngine.Run(0.5f);
        T.Check("Campaign farm started with the chosen map / start / economy / name in slot 2",
            !GameMenu.IsOpen && GameModes.Current == GameModes.Mode.Campaign && Regions.CurrentId == "najd" && eco.Money == 25000 && Career.CurrentEconomy == Career.Economy.Hard
            && GameModes.CurrentFarmName == "Palm Oasis Farm" && SaveSlots.ActiveSlot == 2 && SaveSlots.HasSlot(2), eco.Money + " " + Career.CurrentEconomy + " " + GameModes.CurrentFarmName);
        T.Check("Campaign: story chapters and the mission chain are on", mm.Missions.Length == 50 && mm.Current != null && camp.Chapters.Length == 8 && GameModes.HasStory && !FarmLevel.IsUnlocked(topDecor.id));

        // ---- challenge on slot 3 (mouse / direct choose): Desert start
        ToMain();
        Menu.List.Choose(Labels().IndexOf("Career"));
        Menu.List.Choose(2);
        Menu.List.Choose(2);   // Challenge
        Menu.List.Choose(0);   // Najd
        T.Check("Wizard (challenge): challenge list (Desert start: 1,000,000 within 3 years)", Menu.PageName == "ChallengePick" && Labels()[0].Contains("Desert start") && Labels()[0].Contains("1,000,000") && Labels()[0].Contains("3"),
            string.Join(" | ", Labels()));
        Menu.List.Choose(0);
        T.Check("Wizard (challenge): straight to the name", Menu.PageName == "FarmName");
        Menu.List.Choose(1);   // Start farming
        FakeEngine.Run(0.5f);
        var ch = GameModes.CurrentChallenge;
        T.Check("Challenge: from scratch, hard economy, no story", GameModes.Current == GameModes.Mode.Challenge && ch != null && ch.id == "desert_start" && eco.Money == 150
            && Career.CurrentEconomy == Career.Economy.Hard && mm.Missions.Length == 0 && camp.Chapters.Length == 0 && SaveSlots.ActiveSlot == 3, eco.Money + " " + Career.CurrentEconomy);
        int days = GameModes.DaysPerYear * 3;
        T.Check("Challenge: deadline is 3 in-game years", GameModes.DeadlineDay == WorldClock.Instance.Day + days - 1, GameModes.DeadlineDay + " " + days);
        T.Check("Challenge: HUD shows goal progress and days left", HudLines.Status.Select(f => f()).Any(s => s != null && s.Contains("Desert start") && s.Contains("1,000,000") && s.Contains("days left")));
        string over = MainMenuScene.OverviewText();
        T.Check("Challenge: pause overview shows the goal", over.Contains("Desert start") && over.Contains("Goal"), over);
        eco.AddMoney(1000000);
        FakeEngine.Tick(0.02f);
        T.Check("Challenge won: result card", GameModes.Result == 1 && GameModeDirector.Instance.LastResultCard.Contains("Challenge complete!") && camp.CardOpen, GameModeDirector.Instance.LastResultCard);
        SaveSlots.Save(3);
        // a lost challenge: Market master runs out of time
        Menu.StartCareer(new NewFarm { mode = GameModes.Mode.Challenge, challenge = "quick_profit", region = "najd" });
        FakeEngine.Run(0.5f);
        T.Check("Challenge: Market master starts as farm manager", GameModes.CurrentChallenge.id == "quick_profit" && eco.Money == 25000 && GameModes.Result == 0);
        int guard = 0;
        while (GameModes.Result == 0 && guard++ < 200) WorldClock.Instance.NextDay();
        FakeEngine.Run(0.2f);
        T.Check("Challenge lost after the deadline: result card", GameModes.Result == 2 && WorldClock.Instance.Day == GameModes.DeadlineDay + 1 && GameModeDirector.Instance.LastResultCard.Contains("Challenge failed"),
            GameModes.Result + " day " + WorldClock.Instance.Day + " deadline " + GameModes.DeadlineDay);
        Menu.StartCareer("najd", Career.Start.NewFarmer, Career.Economy.Normal);
        FakeEngine.Run(0.3f);
        T.Check("Pause-menu style New game is a campaign again", GameModes.Current == GameModes.Mode.Campaign && GameModes.CurrentChallenge == null && mm.Missions.Length == 50 && camp.Chapters.Length == 8);

        // ---- Career cards with map / money / day / mode
        ToMain();
        Menu.List.Choose(Labels().IndexOf("Career"));
        labels = Labels();
        T.Check("Career: card 1 = Nile Delta free play farm with money and day", labels[0].Contains("Barakah Farm") && labels[0].Contains("Nile Delta") && labels[0].Contains("Free play")
            && labels[0].Contains(GameModes.UnlimitedAmount.ToString("N0")) && labels[0].Contains("Day ") && labels[0].Contains("EGP") && labels[0].Contains("1 min"), labels[0]);
        T.Check("Career: card 2 = Najd campaign with 25,000 SAR", labels[1].Contains("Palm Oasis Farm") && labels[1].Contains("Najd Oasis") && labels[1].Contains("Campaign") && labels[1].Contains("25,000 SAR") && labels[1].Contains("Day 1"), labels[1]);
        T.Check("Career: card 3 = challenge", labels[2].Contains("Challenge") && labels[2].Contains("Last saved"), labels[2]);
        T.Check("Career: cards carry the map's flag colour", Menu.List.Rows[0].accent().Equals(GameMenu.FlagColor("nile_delta")) && Menu.List.Rows[1].accent().Equals(GameMenu.FlagColor("najd")));
        Pad(pad.dpad.down);
        FakeEngine.Run(0.3f);
        T.Check("Career: right panel shows the selected farm in detail", Scene.InfoText.Contains("Palm Oasis Farm") && Scene.InfoText.Contains("Play time") && Scene.InfoText.Contains("Campaign"), Scene.InfoText);

        // ---- delete with confirmation (slot 3)
        Pad(pad.dpad.down); Pad(pad.buttonSouth);
        T.Check("Used slot: Load / Delete / Back", Menu.PageName == "SlotActions" && Menu.SelectedSlot == 3 && Labels().SequenceEqual(new[] { "Load", "Delete", "Back" }), string.Join(",", Labels()));
        PadTo("Delete"); Pad(pad.buttonSouth);
        T.Check("Delete asks first (No is preselected)", Menu.PageName == "ConfirmDelete" && Sel() == "No, keep it");
        Pad(pad.buttonSouth);
        T.Check("No keeps the farm", Menu.PageName == "SlotActions" && SaveSlots.HasSlot(3));
        PadTo("Delete"); Pad(pad.buttonSouth); Pad(pad.dpad.down); Pad(pad.buttonSouth);
        T.Check("Yes deletes the farm and returns to Career", !SaveSlots.HasSlot(3) && Menu.PageName == "Career" && Labels()[2].Contains("Empty slot") && SaveSlots.ActiveSlot != 3, Menu.PageName);

        // ---- load a used slot (slot 2)
        SaveSlots.Summary s2; SaveSlots.Info(2, out s2);   // (the fake engine clears PlayerPrefs with the scene below)
        Menu.List.Choose(1);
        int loads = UnityEngine.SceneManagement.SceneManager.LoadCount;
        Menu.List.Choose(Labels().IndexOf("Load"));
        T.Check("Load reloads the scene with the slot", UnityEngine.SceneManagement.SceneManager.LoadCount == loads + 1 && !GameMenu.IsOpen);
        Program.BuildWorld();
        FakeEngine.Run(2f);
        T.Check("Loaded farm: name, map, mode and money from slot 2", GameModes.CurrentFarmName == "Palm Oasis Farm" && Regions.CurrentId == "najd" && GameModes.Current == GameModes.Mode.Campaign
            && EconomyManager.Instance.Money == s2.money && !GameMenu.IsOpen, GameModes.CurrentFarmName + " " + EconomyManager.Instance.Money + " vs " + s2.money);
        Keyboard.current = kb; Gamepad.current = pad;

        // ---- multiplayer: greyed without Netcode
        NetSession.Register(null);
        ToMain();
        PadTo("Multiplayer"); Pad(pad.buttonSouth);
        labels = Labels();
        var rows = Menu.List.Rows;
        T.Check("Multiplayer without Netcode: 'coming in a future update', Host and Join greyed",
            Menu.Front == GameMenu.FrontScreen.Multiplayer && labels[0].Contains("Multiplayer is coming in a future update") && !rows[0].IsEnabled
            && labels[1] == "Host a game" && !rows[1].IsEnabled && labels[2] == "Join a game" && !rows[2].IsEnabled && Sel() == "Multiplayer settings", string.Join(" | ", labels));
        FakeEngine.Run(0.3f);
        T.Check("Multiplayer without Netcode: no developer messages", !labels.Any(l => l.Contains("Netcode") || l.Contains("package")) && !Scene.InfoText.Contains("Netcode")
            && Scene.InfoText.Contains("coming in a future update"), Scene.InfoText);
        var chatBefore = ChatSafety.Chat;
        Pad(pad.buttonSouth);
        Pad(pad.dpad.right);
        T.Check("Multiplayer settings: D-pad changes the chat mode", Menu.PageName == "MpSettings" && ChatSafety.Chat != chatBefore);
        Pad(pad.dpad.left);
        Pad(pad.buttonEast);
        T.Check("B back to the Multiplayer screen", Menu.PageName == "Multiplayer" && ChatSafety.Chat == chatBefore);
        Pad(pad.buttonEast);

        // ---- multiplayer with a backend: host / join with the digit picker
        var net = new TestBackend();
        NetSession.Register(net);
        PadTo("Multiplayer"); Pad(pad.buttonSouth);
        T.Check("Multiplayer with a backend: Host and Join enabled, no 'coming soon'", Menu.List.Rows[0].label() == "Host a game" && Menu.List.Rows[0].IsEnabled && Menu.List.Rows[1].IsEnabled
            && !Labels().Any(l => l.Contains("future update")));
        PadTo("Join a game"); Pad(pad.buttonSouth);
        T.Check("Join: four address parts + Connect", Menu.PageName == "MpJoin" && Labels().Count == 6 && Labels()[4].StartsWith("Connect"));
        string addr0 = Menu.JoinAddress;
        Pad(pad.dpad.right);            // part 1 + 1
        Pad(pad.rightShoulder);         // part 1 + 10
        Pad(pad.dpad.down); Pad(pad.leftShoulder);   // part 2 - 10
        var p0 = addr0.Split('.').Select(int.Parse).ToArray(); var p1 = Menu.JoinAddress.Split('.').Select(int.Parse).ToArray();
        T.Check("Join: D-pad +-1 and LB / RB +-10 pick the address digits (no typing)", p1[0] == (p0[0] + 11) % 256 && p1[1] == ((p0[1] - 10) % 256 + 256) % 256, addr0 + " -> " + Menu.JoinAddress);
        string want = Menu.JoinAddress;
        PadTo("Connect"); Pad(pad.buttonSouth);
        T.Check("Join connects to the picked address and closes the menu", !GameMenu.IsOpen && MultiplayerManager.Instance.LastAddress == want, MultiplayerManager.Instance.LastAddress + " vs " + want);
        MultiplayerManager.Instance.Leave();
        Keyboard.current = null;   // gamepad only while hosting (the fake keyboard has no emote-key indexer)
        ToMain();
        PadTo("Multiplayer"); Pad(pad.buttonSouth); Pad(pad.buttonSouth);   // Host a game
        T.Check("Host a game hosts this farm and resumes", NetSession.IsHost && !GameMenu.IsOpen);
        MultiplayerManager.Instance.Leave();
        NetSession.Register(null);
        Keyboard.current = kb;

        // ---- pause menu: unchanged, and Main menu returns to the front end
        Key(kb.pKey);
        labels = Labels();
        T.Check("Pause menu keeps its list (Resume, Map, Farm overview, Save, Load, Settings, Main menu, Quit)",
            Menu.CurrentMode == GameMenu.Mode.Pause && labels[0] == "Resume" && new[] { "Map", "Farm overview", "Save game", "Load game", "Settings", "Main menu", "Quit" }.All(labels.Contains), string.Join(",", labels));
        Menu.List.Choose(labels.IndexOf("Main menu"));
        T.Check("Pause > Main menu opens the new front end", Menu.CurrentMode == GameMenu.Mode.Main && Menu.Front == GameMenu.FrontScreen.Main && Labels().Contains("Career") && Labels().Contains("Multiplayer"));

        // ---- Arabic
        Loc.SetLanguage(Language.Arabic);
        Menu.List.Choose(Labels().FindIndex(l => l == Loc.T("Career")));
        labels = Labels();
        T.Check("Arabic: Career cards in Arabic (farm name, map, mode)", Menu.PageName == "Career" && HasArabic(labels[0]) && HasArabic(labels[1]) && HasArabic(labels[2]) && GameModes.FarmNameOf(9) == Loc.Display("مزرعة البركة"), labels[0]);
        Menu.List.Choose(2);
        T.Check("Arabic: wizard pages in Arabic", Menu.PageName == "GameModePick" && Labels().All(HasArabic), string.Join(" | ", Labels()));
        Menu.Back(); Menu.Back(); Menu.Back();
        T.Check("Arabic: back to the title, prompt in Arabic", Menu.Front == GameMenu.FrontScreen.Title);
        FakeEngine.Run(0.3f);
        T.Check("Arabic: title prompt and news in Arabic", HasArabic(Scene.PromptText), Scene.PromptText);
        var keys = new List<string> { "Press any key to start", "Press [Enter] to start", "Unlimited money", "Game mode", "Map", "Start money", "Start", "Economy", "Farm name", "Challenge" };
        foreach (GameModes.Mode m in Enum.GetValues(typeof(GameModes.Mode))) { keys.Add(GameModes.ModeName(m)); keys.Add(GameModes.ModeDesc(m)); }
        foreach (var c in GameModes.Challenges) { keys.Add(c.name); keys.Add(c.desc); }
        foreach (var m in new[] { ChatSafety.ChatMode.Everyone, ChatSafety.ChatMode.FriendsOnly, ChatSafety.ChatMode.Off }) keys.Add(ChatSafety.ChatModeLabel(m));
        foreach (var m in new[] { ChatSafety.JoinMode.Anyone, ChatSafety.JoinMode.FriendsOnly }) keys.Add(ChatSafety.JoinModeLabel(m));
        var missing = keys.Distinct().Where(k => !Loc.HasTranslation(k)).ToList();
        T.Check("Arabic: every front-end and play-mode text has a translation (Loc/98_frontend.json)", missing.Count == 0 && GameModes.FarmNames.All(n => HasArabic(n[1])), string.Join(" | ", missing));
        Loc.SetLanguage(Language.English);
        Menu.Close();

        T.Check("Front end: no errors", FakeEngine.Errors.Count == e0, string.Join(" | ", FakeEngine.Errors.Skip(e0).Take(3)));
        GameMenu.ShowOnStart = false;
        Keyboard.current = null; Gamepad.current = null;
        GameInput.ForceDevice(false, false);
    }
}
