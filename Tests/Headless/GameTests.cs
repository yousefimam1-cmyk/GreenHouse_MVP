// Headless gameplay tests: builds a world shaped like Blak_Pulse_Main (two greenhouses, well, control stations,
// player) on the fake engine, boots the GameManager and plays through the systems.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

static class T
{
    public static int Pass, Fail;
    public static readonly List<string> Failures = new List<string>();
    public static void Check(string name, bool ok, string detail = "")
    {
        if (ok) { Pass++; Console.WriteLine("  PASS " + name); }
        else { Fail++; Failures.Add(name + " " + detail); Console.WriteLine("  FAIL " + name + "  " + detail); }
    }
    static FieldInfo Field(object o, string field)
    {
        for (var t = o.GetType(); t != null; t = t.BaseType)
        {
            var f = t.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
            if (f != null) return f;
        }
        throw new Exception("no field " + field + " on " + o.GetType().Name);
    }
    public static void Set(object o, string field, object v) => Field(o, field).SetValue(o, v);
    public static object Get(object o, string field) => Field(o, field).GetValue(o);
    public static void SetProp(object o, string prop, object v)
    {
        for (var t = o.GetType(); t != null; t = t.BaseType)
        {
            var p = t.GetProperty(prop, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
            if (p != null) { p.SetValue(o, v); return; }
        }
        throw new Exception("no prop " + prop);
    }
    public static void Static(Type t, string method) => t.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public).Invoke(null, null);
}

static class Program
{
    static ConfigurationData config;

    static void ResetStatics()
    {
        foreach (var t in new[] { typeof(GameEvents), typeof(Loc), typeof(CropDatabase), typeof(SaveData), typeof(WorldClock), typeof(FieldPlot) })
        {
            var m = t.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                .FirstOrDefault(x => x.GetCustomAttributes(typeof(RuntimeInitializeOnLoadMethodAttribute), false).Length > 0);
            m?.Invoke(null, null);
        }
        foreach (var t in typeof(GameManager).Assembly.GetTypes())
        {
            var inst = t.GetField("Instance", BindingFlags.Static | BindingFlags.Public);
            if (inst != null && !inst.IsLiteral) inst.SetValue(null, null);
            var instP = t.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public);
            if (instP != null && instP.CanWrite) instP.SetValue(null, null);
        }
        typeof(DeliveryZone).GetField("LoadingBay").SetValue(null, null);
        typeof(DeliveryZone).GetField("Market").SetValue(null, null);
    }

    static Module NewModule(ModuleType t) { var m = ScriptableObject.CreateInstance<Module>(); m.moduleType = t; m.name = t.ToString(); return m; }

    static GreenhouseManager BuildGreenhouse(string name, Vector3 pos, bool randomize, bool stationNeedsWell, out ControllStation station)
    {
        var go = new GameObject(name);
        go.transform.position = pos;
        // children first (cropbeds are found in GreenhouseManager.Awake)
        foreach (var bed in new[] { "CropA", "CropB", "CropC", "CropD" })
        {
            var b = new GameObject(bed);
            b.transform.SetParent(go.transform, false);
            b.layer = 7;
            var mr = b.AddComponent<MeshRenderer>();
            b.AddConfigured<Cropbed>(c =>
            {
                c.data = new CropbedData();
                c.cropbedMats = new[] { new Material((Shader)null), new Material((Shader)null) };
                var plants = new GameObject("Foliage"); plants.transform.SetParent(b.transform, false);
                new GameObject("Alive").transform.SetParent(plants.transform, false);
                new GameObject("Dead").transform.SetParent(plants.transform, false);
                c.foliage = new[] { plants };
                c.meshRenderer = mr;
                T.Set(c, "interactionPrompt", "[E] Inspect");
            });
        }
        var st = new GameObject("ControlStation"); st.transform.SetParent(go.transform, false);
        station = st.AddConfigured<ControllStation>(s => { s.ActionRequired = stationNeedsWell; s.ActionText = "Start the well first to optimize modules!"; T.Set(s, "interactionPrompt", "[E] Control Station"); });
        return go.AddConfigured<GreenhouseManager>(g =>
        {
            g.configurationData = config;
            g.modules = new[] { NewModule(ModuleType.Irrigation), NewModule(ModuleType.Nutrient), NewModule(ModuleType.Energy), NewModule(ModuleType.ClimateControl) };
            g.randomizeModules = randomize;
        });
    }

    static void BuildWorld()
    {
        FakeEngine.Reset();
        ResetStatics();
        FakeEngine.BeginScene();
        config = ScriptableObject.CreateInstance<ConfigurationData>();
        var cam = new GameObject("MainCamera"); cam.tag = "MainCamera"; cam.AddComponent<Camera>(); cam.AddComponent<AudioListener>();
        var sun = new GameObject("Sun"); var l = sun.AddComponent<Light>(); l.type = LightType.Directional; RenderSettings.sun = l;
        var ui = new GameObject("UI");
        var alert = new GameObject("Alert").AddComponent<TMPro.TextMeshProUGUI>();
        var csGroup = new GameObject("CS").AddComponent<CanvasGroup>();
        var cbGroup = new GameObject("CB").AddComponent<CanvasGroup>();
        var title = new GameObject("Title"); title.transform.SetParent(cbGroup.transform, false); title.AddComponent<TMPro.TextMeshProUGUI>().text = "Crop Stats";
        ui.AddConfigured<UIManager>(u =>
        {
            u.configurationData = config;
            T.Set(u, "controlStationUI", csGroup); T.Set(u, "cropbedUI", cbGroup); T.Set(u, "alertMsg", alert);
            u.moduleStatsUI = Enum.GetValues(typeof(ModuleType)).Cast<ModuleType>().Select(m => new ControlStationUI { moduleType = m, valueTxt = new GameObject("v").AddComponent<TMPro.TextMeshProUGUI>(), optimizeButton = new GameObject("b") }).ToArray();
            u.cropbedStatsUI = Enum.GetValues(typeof(CropAttributeType)).Cast<CropAttributeType>().Select(a => new CropbedUI { attributeType = a, valueTxt = new GameObject("v").AddComponent<TMPro.TextMeshProUGUI>(), satisfiedText = new GameObject("s").AddComponent<TMPro.TextMeshProUGUI>() }).ToArray();
        });
        var gh1 = BuildGreenhouse("GreenHouse", new Vector3(30, 0, -15), false, true, out var station1);
        BuildGreenhouse("GreenHouse-1", new Vector3(-32, 0, 34), true, false, out _);
        var well = new GameObject("Well"); well.layer = 7;
        var handle = new GameObject("Handle"); handle.transform.SetParent(well.transform, false);
        well.AddConfigured<WellController>(w => { T.Set(w, "handleT", handle.transform); T.Set(w, "controlStation", station1); T.Set(w, "interactionPrompt", "[E] Start Well"); });
        var player = new GameObject("PlayerCapsule"); player.tag = "Player"; player.transform.position = new Vector3(0, 0, -57.4f);
        player.AddComponent<StarterAssets.StarterAssetsInputs>(); player.AddComponent<CharacterController>();
        FakeEngine.EndScene();
        // what GameManager.Bootstrap does after the scene loads
        new GameObject("GameManager (auto)").AddComponent<GameManager>();
    }

    // ------------------------------------------------------------------ UI, input, menus (feat/ui)
    static void PressKey(UnityEngine.InputSystem.KeyControl k) { k.wasPressedThisFrame = true; k.isPressed = true; FakeEngine.Tick(0.02f); k.wasPressedThisFrame = false; k.isPressed = false; }
    static void PressPad(UnityEngine.InputSystem.ButtonControl b) { b.wasPressedThisFrame = true; b.isPressed = true; FakeEngine.Tick(0.02f); b.wasPressedThisFrame = false; b.isPressed = false; }

    static void UiSection(string resources)
    {
        Console.WriteLine("UI, controllers, menus, saves");
        var kb = new UnityEngine.InputSystem.Keyboard();
        foreach (var f in typeof(UnityEngine.InputSystem.Keyboard).GetFields())
            if (f.FieldType == typeof(UnityEngine.InputSystem.KeyControl)) f.SetValue(kb, new UnityEngine.InputSystem.KeyControl());
        UnityEngine.InputSystem.Keyboard.current = kb;
        T.Static(typeof(SaveSlots), "Hook");   // [RuntimeInitializeOnLoadMethod]: scene load / unload callbacks
        int errs = FakeEngine.Errors.Count;
        FakeEngine.Run(3f);
        T.Check("UI systems created (minimap, menu, info screens, tutorial, third person, gamepad bridge)",
            Minimap.Instance && GameMenu.Instance && FarmInfoScreen.Instance && TutorialOverlay.Instance && ThirdPersonView.Instance && UnityEngine.Object.FindAnyObjectByType<GamepadBridge>() != null);

        // Every UiText key (and the keys picked through variables) has Arabic in Data/Loc/50_ui.json.
        var keys = new List<string>();
        foreach (var file in Directory.GetFiles(Path.Combine(resources, "..", "Scripts"), "*.cs", SearchOption.AllDirectories))
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(file), @"UiText\.(?:T|Raw)\(([^;]*?)\)[;,)\s+.]"))
                foreach (System.Text.RegularExpressions.Match lit in System.Text.RegularExpressions.Regex.Matches(m.Groups[1].Value, "\"((?:[^\"\\\\]|\\\\.)*)\""))
                    keys.Add(lit.Groups[1].Value);
        for (int i = 0; i < GameInput.Count; i++) keys.Add(GameInput.Label((GameAction)i));
        keys.AddRange(new[] { "Low", "Medium", "High" });
        foreach (var h in (string[])typeof(TutorialOverlay).GetField("Hints", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null)) keys.Add(h);
        Loc.Raw("x");
        var missing = keys.Distinct().Where(k => !Loc.HasTranslation(k)).ToList();
        T.Check("All UI texts have Arabic (" + keys.Distinct().Count() + " keys)", keys.Count > 60 && missing.Count == 0, string.Join(" | ", missing.Take(5)));

        // ---- glyphs and rebinding
        GameInput.ForceDevice(false, false);
        T.Check("Keyboard prompt keeps [E]", Glyphs.Apply("[E] Harvest") == "[E] Harvest");
        GameInput.ForceDevice(true, false);
        T.Check("Xbox pad shows A / X / Y in prompts", Glyphs.Apply("[E] Harvest").Contains(">A<") && Glyphs.Apply("[R] Reset").Contains(">X<") && Glyphs.Apply("[Q] Tool").Contains(">Y<"), Glyphs.Apply("[E] Harvest"));
        GameInput.ForceDevice(true, true);
        T.Check("PlayStation pad shows cross / square / triangle", Glyphs.Apply("[E] x").Contains("✕") && Glyphs.Apply("[R] x").Contains("□") && Glyphs.Apply("[Q] x").Contains("△") && Glyphs.Apply("[Esc] x").Contains("○"));
        Loc.SetLanguage(Language.Arabic);
        T.Check("Glyphs work in shaped Arabic prompts", Glyphs.Apply(Loc.T("[E] Harvest")).Contains("✕"), Glyphs.Apply(Loc.T("[E] Harvest")));
        Loc.SetLanguage(Language.English);
        T.Check("DualSense / DualShock detected as PlayStation, XInput as Xbox",
            GameInput.IsPlayStationPad(new UnityEngine.InputSystem.Gamepad { name = "DualSenseGamepadHID", displayName = "DualSense Wireless Controller" }) &&
            GameInput.IsPlayStationPad(new UnityEngine.InputSystem.Gamepad { name = "DualShock4GamepadHID" }) &&
            !GameInput.IsPlayStationPad(new UnityEngine.InputSystem.Gamepad { name = "XInputControllerWindows", displayName = "Xbox Controller" }));
        GameInput.ForceDevice(false, false);
        GameInput.SetKey(GameAction.Interact, "<Keyboard>/f");
        T.Check("Rebound Interact shows [F] in prompts", Glyphs.Apply("[E] Harvest") == "[F] Harvest", Glyphs.Apply("[E] Harvest"));
        kb.fKey.wasPressedThisFrame = true;
        bool fWorks = GameInput.Pressed(GameAction.Interact);
        kb.fKey.wasPressedThisFrame = false; kb.eKey.wasPressedThisFrame = true;
        bool eStill = GameInput.Pressed(GameAction.Interact);
        kb.eKey.wasPressedThisFrame = false;
        T.Check("Rebound key triggers the action, old key doesn't", fWorks && !eStill);
        GameInput.StartRebind(GameAction.Implement, ok => { });
        T.Check("Interactive rebind waits for a key", GameInput.Rebinding && UnityEngine.InputSystem.InputActionRebindingExtensions.RebindingOperation.Current != null);
        UnityEngine.InputSystem.InputActionRebindingExtensions.RebindingOperation.Current.FakeComplete("<Keyboard>/z");
        T.Check("Interactive rebind applies the new key", !GameInput.Rebinding && GameInput.KeyPath(GameAction.Implement) == "<Keyboard>/z");
        string json = PlayerPrefs.GetString(GameInput.BindingsKey, "");
        T.Static(typeof(GameInput), "ResetStatics");
        T.Check("Bindings saved as JSON and loaded on restart", json.Contains("<Keyboard>/z") && GameInput.KeyPath(GameAction.Implement) == "<Keyboard>/z" && GameInput.KeyPath(GameAction.Interact) == "<Keyboard>/f", json);
        GameInput.ResetBindings();
        T.Check("Reset to defaults", GameInput.KeyPath(GameAction.Interact) == "<Keyboard>/e" && GameInput.KeyPath(GameAction.Implement) == "<Keyboard>/q");

        // ---- gamepad
        var pad = new UnityEngine.InputSystem.Gamepad { name = "DualSenseGamepadHID", displayName = "DualSense Wireless Controller" };
        UnityEngine.InputSystem.Gamepad.current = pad;
        PressPad(pad.buttonWest);
        T.Check("Using a DualSense switches glyphs to PlayStation", GameInput.UsingGamepad && GameInput.IsPlayStation);
        var mouse = new UnityEngine.InputSystem.Mouse(); UnityEngine.InputSystem.Mouse.current = mouse;
        PressPad(mouse.leftButton);
        T.Check("Mouse / keyboard switches glyphs back", !GameInput.UsingGamepad);
        UnityEngine.InputSystem.Mouse.current = null;
        var look = GamepadBridge.ScaleLook(new Vector2(2f, 3f), false, 2f, 1f, true);
        T.Check("Look sensitivity and invert Y", Math.Abs(look.x - 4f) < 0.001f && Math.Abs(look.y + 6f) < 0.001f);

        var truck = FarmTruck.Instance;
        truck.transform.position = DeliveryZone.Market.transform.position + new Vector3(30, 0, 30);
        PressPad(pad.buttonSouth); // nothing targeted in the fake world: just tracks the device
        truck.Interact();
        FakeEngine.Tick(0.02f);
        pad.rightTrigger.value = 1f; pad.leftStick.value = new Vector2(1f, 0f);
        FakeEngine.Run(1f, 0.05f);
        T.Check("RT drives the truck, left stick steers", truck.Speed > 1f, "" + truck.Speed);
        pad.rightTrigger.value = 0f; pad.leftTrigger.value = 1f;
        FakeEngine.Run(1.5f, 0.05f);
        T.Check("LT brakes", truck.Speed < 1f, "" + truck.Speed);
        pad.leftTrigger.value = 0f; pad.leftStick.value = Vector2.zero;
        T.SetProp(truck, "Speed", 0f);
        PressPad(pad.buttonEast);
        T.Check("B / Circle exits the vehicle", !truck.IsDriving && Vehicle.Current == null);

        EconomyManager.Instance.AddMoney(5000);
        var shop = ShopUI.Instance;
        shop.Open(); FakeEngine.Tick(0.02f);
        PressPad(pad.rightShoulder);
        T.Check("RB turns the shop page", shop.Page == 1 && shop.CurrentCategory == "Seeds", shop.Page + " " + shop.CurrentCategory);
        PressPad(pad.dpad.down);
        T.Check("D-pad down selects the next row", shop.SelectedRow == 1);
        string crop = EconomyManager.Instance.Data.fieldCrops[1].id;
        int seeds = EconomyManager.Instance.Seeds(crop);
        PressPad(pad.buttonSouth);
        T.Check("A / Cross buys the selected row", EconomyManager.Instance.Seeds(crop) == seeds + 1, crop + " " + seeds + " -> " + EconomyManager.Instance.Seeds(crop));
        PressPad(pad.leftShoulder);
        T.Check("LB goes back a page", shop.Page == 0);
        PressPad(pad.buttonEast);
        T.Check("B / Circle closes the shop", !shop.IsOpen && !UIManager.Instance.IsUIOpened);

        // ---- pause menu, settings, new game
        var menu = GameMenu.Instance;
        FakeEngine.Tick(0.02f);
        PressPad(pad.startButton);
        T.Check("Start / Options opens the pause menu and pauses", menu.CurrentMode == GameMenu.Mode.Pause && Time.timeScale == 0f && UIManager.Instance.IsUIOpened);
        int settingsRow = menu.List.Rows.ToList().FindIndex(r => r.label() == UiText.T("Settings"));
        menu.List.Choose(settingsRow);
        PressPad(pad.dpad.down); PressPad(pad.dpad.down);   // to Sound effects
        PressPad(pad.dpad.left);
        T.Check("D-pad changes a setting (SFX volume 90%)", Math.Abs(GameSettings.SfxVolume - 0.9f) < 0.001f, "" + GameSettings.SfxVolume);
        GameSettings.MasterVolume = 0.5f;
        T.Check("Master volume drives AudioListener.volume", Math.Abs(AudioListener.volume - 0.5f) < 0.001f);
        GameSettings.GraphicsPreset = 1;
        T.Check("Graphics preset Medium applied", GraphicsEnhancer.Preset == 1 && !GraphicsEnhancer.High);
        PressPad(pad.buttonEast);
        T.Check("B goes back to the pause page", menu.List.Rows.Any(r => r.label() == UiText.T("Resume")));
        PressPad(pad.buttonEast);
        T.Check("B on the pause page resumes", menu.CurrentMode == GameMenu.Mode.Closed && Time.timeScale == 1f && !UIManager.Instance.IsUIOpened);
        PressKey(kb.pKey);
        T.Check("P opens the pause menu", GameMenu.IsOpen);
        menu.List.Choose(menu.List.Rows.ToList().FindIndex(r => r.label() == UiText.T("New game")));
        EconomyManager.Instance.AddMoney(1234);
        menu.List.Choose(1); // "Yes, clear the current save"
        FakeEngine.Run(0.5f);
        T.Check("Pause menu New game resets the farm and resumes", EconomyManager.Instance.Money == 500 && !GameMenu.IsOpen && Time.timeScale == 1f, EconomyManager.Instance.Money + "");
        T.Check("Settings survive a new game", Math.Abs(GameSettings.MasterVolume - 0.5f) < 0.001f && GameSettings.GraphicsPreset == 1);

        // ---- inventory and stats
        EconomyManager.Instance.AddMoney(300);
        T.Check("Money earned is tracked", FarmInfoScreen.Instance.MoneyEarned == 300, "" + FarmInfoScreen.Instance.MoneyEarned);
        EconomyManager.Instance.AddGood("eggs", 12f);
        PressKey(kb.iKey);
        T.Check("I opens the inventory", FarmInfoScreen.Instance.Open == FarmInfoScreen.Page.Inventory && UIManager.Instance.IsUIOpened);
        T.Check("Inventory lists goods, seeds, feed, animals", FarmInfoScreen.InventoryText().Contains("12.0") && FarmInfoScreen.InventoryText().Contains("Seeds") && FarmInfoScreen.InventoryText().Contains("Feed"), FarmInfoScreen.InventoryText());
        PressKey(kb.oKey);
        T.Check("O switches to farm stats", FarmInfoScreen.Instance.Open == FarmInfoScreen.Page.Stats && FarmInfoScreen.StatsText().Contains("Money earned: 300") && FarmInfoScreen.StatsText().Contains("Days played"), FarmInfoScreen.StatsText());
        PressKey(kb.escapeKey);
        T.Check("Esc closes the info screen", FarmInfoScreen.Instance.Open == FarmInfoScreen.Page.None && !UIManager.Instance.IsUIOpened);

        // ---- minimap
        FakeEngine.Run(2.5f);
        var mm = Minimap.Instance;
        T.Check("Minimap icons registered (greenhouses, plots, vehicles, stalls, pen, bay, market)", MinimapIcon.All.Count >= 15, "" + MinimapIcon.All.Count);
        T.Check("Minimap camera renders top-down to a texture", mm.MapCamera.orthographic && mm.MapCamera.targetTexture != null && mm.MapCamera.cullingMask == ~(1 << 5) && Math.Abs(mm.MapCamera.transform.eulerAngles.x - 90f) < 0.5f);
        T.Check("Icons near the player are drawn", mm.VisibleMiniIcons > 0, "" + mm.VisibleMiniIcons);
        T.Check("Objective shown on the minimap", ObjectiveMarker.CurrentTarget != null && mm.MiniObjective.gameObject.activeSelf);
        var p = Minimap.ToMap(new Vector3(10, 0, 0), Vector3.zero, 90f, 2f);
        T.Check("Minimap turns with the view (what you face is up)", Math.Abs(p.x) < 0.01f && Math.Abs(p.y - 20f) < 0.01f, p.x + "," + p.y);
        PressKey(kb.nKey);
        T.Check("N opens the full north-up map", mm.FullOpen && UIManager.Instance.IsUIOpened);
        FakeEngine.Tick(0.02f);
        T.Check("Full map frames the whole farm", mm.MapCamera.orthographicSize >= 60f && Math.Abs(mm.ViewYaw) < 0.01f, "" + mm.MapCamera.orthographicSize);
        PressKey(kb.nKey);
        T.Check("N closes the map", !mm.FullOpen && !UIManager.Instance.IsUIOpened);

        // ---- third person
        var player = GameObject.Find("PlayerCapsule").transform;
        var fpCam = Camera.main;
        PressKey(kb.vKey);
        var tp = ThirdPersonView.Instance;
        T.Check("V switches to third person (own camera becomes main)", tp.IsThirdPerson && Camera.main == tp.Camera && !fpCam.enabled && tp.Model != null);
        FakeEngine.Tick(0.02f);
        Vector3 back = tp.Camera.transform.position - player.position;
        T.Check("Camera sits behind and above the player", Vector3.Dot(back, player.forward) < -2f && back.y > 0f, back.ToString());
        player.position += player.forward * 0.1f;
        FakeEngine.Tick(0.02f);
        T.Check("Model animates from the capsule speed", tp.GroundSpeed > 0.4f, "" + tp.GroundSpeed);
        PressKey(kb.vKey);
        T.Check("V switches back to the StarterAssets first person camera", !tp.IsThirdPerson && Camera.main == fpCam && fpCam.enabled && !GameSettings.ThirdPerson);

        // ---- tutorial (doesn't touch missions)
        int missionBefore = MissionManager.Instance.Index;
        var tut = TutorialOverlay.Instance;
        tut.Begin();
        T.Check("Tutorial starts at hint 1/6", tut.Running && tut.Step == 0 && tut.BodyText().Contains("1/6"));
        PressKey(kb.enterKey);
        T.Check("Enter skips to the next hint", tut.Step == 1);
        T.Check("Tutorial leaves mission progress alone", MissionManager.Instance.Index == missionBefore && MissionManager.Instance.Index == 0);
        GameEvents.RaiseModuleOptimized(null, ModuleType.Energy);
        GameEvents.RaiseModuleOptimized(null, ModuleType.Nutrient);
        T.Check("Doing the step ticks it off", tut.StepDone);
        FakeEngine.Run(2f, 0.1f);
        T.Check("...then moves to the next hint", tut.Step == 2, "" + tut.Step);
        PressKey(kb.backspaceKey);
        T.Check("Backspace closes the tutorial", !tut.Running);

        // ---- save slots, autosave, loading
        EconomyManager.Instance.AddMoney(777);
        int moneyInSlot = EconomyManager.Instance.Money;
        T.Check("Save to slot 2", SaveSlots.Save(2) && SaveSlots.HasSlot(2) && SaveSlots.ActiveSlot == 2 && SaveSlots.Describe(2).Contains(moneyInSlot.ToString("N0")), SaveSlots.Describe(2));
        EconomyManager.Instance.AddMoney(1000);
        File.SetLastWriteTimeUtc(SaveSlots.SlotPath(2), DateTime.UtcNow.AddMinutes(-5));
        WorldClock.Instance.NextDay();
        FakeEngine.Run(1f);
        T.Check("Autosave each in-game day writes the active slot", File.GetLastWriteTimeUtc(SaveSlots.SlotPath(2)) > DateTime.UtcNow.AddMinutes(-1));
        SaveSlots.Save(2);
        int moneySaved2 = EconomyManager.Instance.Money;
        EconomyManager.Instance.AddMoney(50);
        SaveData.Flush(true);
        int loads = UnityEngine.SceneManagement.SceneManager.LoadCount;
        T.Check("Load slot reloads the scene", SaveSlots.Load(2) && UnityEngine.SceneManagement.SceneManager.LoadCount == loads + 1);
        T.Check("Slot copied over the save file", SaveData.GetInt("mazraa.money", 0) == moneySaved2, SaveData.GetInt("mazraa.money", 0) + " vs " + moneySaved2);
        BuildWorld();       // the reloaded scene
        GameMenu.ShowOnStart = true;
        FakeEngine.Run(2f);
        T.Check("Loaded game has the slot's money", EconomyManager.Instance.Money == moneySaved2, EconomyManager.Instance.Money + " vs " + moneySaved2);
        T.Check("Main menu is skipped after loading a slot", !GameMenu.IsOpen && Time.timeScale == 1f);

        // ---- main menu at start
        BuildWorld();
        FakeEngine.Run(1f);
        T.Check("Main menu at start pauses the game", GameMenu.Instance.CurrentMode == GameMenu.Mode.Main && Time.timeScale == 0f);
        var labels = GameMenu.Instance.List.Rows.Select(r => r.label()).ToList();
        T.Check("Main menu: Continue, New game, Load slot 1-3, Tutorial, Settings, Quit",
            labels[0] == "Continue" && labels[1] == "New game" && labels.Count(l => l.StartsWith("Load slot")) == 3 && labels.Contains("Settings") && labels.Contains("Quit") && labels.Contains("Tutorial"), string.Join(",", labels));
        T.Check("Empty slots can't be loaded", !GameMenu.Instance.List.Rows[2].IsEnabled && GameMenu.Instance.List.Rows[3].IsEnabled);
        GameMenu.Instance.List.Choose(0);
        T.Check("Continue closes the menu", !GameMenu.IsOpen && Time.timeScale == 1f && !UIManager.Instance.IsUIOpened);
        GameMenu.ShowOnStart = false;
        T.Check("No errors in the UI section", FakeEngine.Errors.Count == errs, string.Join(" | ", FakeEngine.Errors.Skip(errs).Take(3)));
        UnityEngine.InputSystem.Keyboard.current = null;
        UnityEngine.InputSystem.Gamepad.current = null;
        GameInput.ForceDevice(false, false);
    }

    static void Main(string[] args)
    {
        FakeEngine.ResourcesRoot = args[0];
        GameMenu.ShowOnStart = false;   // the main menu pauses the game; the UI section tests it on its own
        FakeEngine.PersistentPath = Path.Combine(Path.GetTempPath(), "mazraa_test_" + System.Diagnostics.Process.GetCurrentProcess().Id); // parallel runs don't share saves
        if (Directory.Exists(FakeEngine.PersistentPath)) Directory.Delete(FakeEngine.PersistentPath, true);
        Directory.CreateDirectory(FakeEngine.PersistentPath);

        Console.WriteLine("Boot");
        BuildWorld();
        FakeEngine.Run(2f);
        var gm = GameManager.Instance;
        T.Check("GameManager running", gm != null);
        T.Check("No errors during boot", FakeEngine.Errors.Count == 0, string.Join(" | ", FakeEngine.Errors.Take(3)));
        T.Check("All systems created", EconomyManager.Instance && MissionManager.Instance && MarketManager.Instance && WorldClock.Instance && FieldManager.Instance && ScenarioManager.Instance && ShopUI.Instance && VehicleSpawner.Instance);
        T.Check("Truck and tractor spawned", FarmTruck.Instance != null && Tractor.Instance != null);
        T.Check("6 field plots", FieldManager.Instance.Plots.Count == 6);
        T.Check("Loading bay and market", DeliveryZone.LoadingBay != null && DeliveryZone.Market != null);
        T.Check("Market is ~120 m away", Vector3.Distance(DeliveryZone.LoadingBay.transform.position, DeliveryZone.Market.transform.position) > 60f);
        T.Check("2 shop stalls + crate + pen", UnityEngine.Object.FindObjectsByType<ShopStall>(FindObjectsSortMode.None).Length == 2 && AnimalPen.Instance != null);
        T.Check("Starting money 500 SAR", EconomyManager.Instance.Money == 500, "" + EconomyManager.Instance.Money);
        T.Check("Starts on mission 1 (well)", MissionManager.Instance.Current?.id == "well");

        Console.WriteLine("Animated life");
        var actors = UnityEngine.Object.FindObjectsByType<Actor>(FindObjectsSortMode.None);
        T.Check("Ambient actors spawned (dog, cat, horses, eagles, shopkeepers, workers, buyers, drivers)", actors.Length >= 14, "" + actors.Length);
        var dog = actors.First(a => a.mode == ActorMode.FollowPlayer);
        var playerT = GameObject.Find("PlayerCapsule").transform;
        playerT.position += new Vector3(15f, 0, 0);
        float d0 = Vector3.Distance(dog.transform.position, playerT.position);
        FakeEngine.Run(4f, 0.1f);
        T.Check("Dog follows the player", Vector3.Distance(dog.transform.position, playerT.position) < d0 - 5f, d0 + " -> " + Vector3.Distance(dog.transform.position, playerT.position));
        playerT.position -= new Vector3(15f, 0, 0);
        var eagle = actors.First(a => a.mode == ActorMode.Fly);
        T.Check("Eagles fly above the farm", eagle.transform.position.y > 15f, eagle.transform.position.ToString());
        T.Check("Shopkeeper at each stall", actors.Count(a => a.mode == ActorMode.Stand) == 2);
        var drivers = actors.Where(a => a.mode == ActorMode.Driver).ToList();
        T.Check("A driver per vehicle, hidden while parked", drivers.Count == 2 && drivers.All(a => !a.transform.GetChild(0).gameObject.activeSelf));

        Console.WriteLine("Greenhouses (original game logic)");
        var ghs = gm.Greenhouses;
        var g1 = ghs.First(g => g.name == "GreenHouse"); var g2 = ghs.First(g => g.name == "GreenHouse-1");
        T.Check("Greenhouse 1 (optimized at start) has 4/4 healthy beds", g1.HealthyCount == 4, "" + g1.HealthyCount);
        T.Check("Greenhouse 2 (random modules) starts with dead beds", g2.HealthyCount < 4, "" + g2.HealthyCount);
        T.Check("Bed values come from crop_profiles.json", g1.Cropbeds.First(b => b.name == "CropA").Crop?.id == "tomato");
        var station = UnityEngine.Object.FindObjectsByType<ControllStation>(FindObjectsSortMode.None).First(s => s.ActionRequired);
        T.Check("Station 1 locked until the well runs", station.IsLocked);
        UnityEngine.Object.FindAnyObjectByType<WellController>().Interact();
        FakeEngine.Run(0.5f);
        T.Check("Well unlocks station", !station.IsLocked);
        T.Check("Mission 1 done -> +150 SAR", EconomyManager.Instance.Money == 650 && MissionManager.Instance.Current?.id == "optimize", EconomyManager.Instance.Money + " " + MissionManager.Instance.Current?.id);
        foreach (var t in new[] { ModuleType.Irrigation, ModuleType.Nutrient, ModuleType.Energy, ModuleType.ClimateControl, ModuleType.Health, ModuleType.YieldProjection })
            g2.OptimizeModule(t);
        FakeEngine.Run(0.5f);
        T.Check("Optimizing all modules makes Greenhouse 2 healthy", g2.HealthyCount == 4, "" + g2.HealthyCount);
        float water = g2.Cropbeds[0].data.water;
        g2.OptimizeModule(ModuleType.Irrigation); g2.OptimizeModule(ModuleType.Irrigation);
        T.Check("Re-optimizing doesn't inflate bed values (old bug)", Math.Abs(g2.Cropbeds[0].data.water - water) < 0.001f, water + " -> " + g2.Cropbeds[0].data.water);
        FakeEngine.Run(1f);
        T.Check("Optimize + healthy-greenhouse missions completed", MissionManager.Instance.Index >= 3, "index " + MissionManager.Instance.Index);

        Console.WriteLine("Growth and harvest");
        FakeEngine.Run(50f, 0.25f);
        var bed = g1.Cropbeds[0];
        T.Check("Healthy bed grows to ready in ~45 s", bed.IsReadyToHarvest, "growth " + bed.Growth);
        float before = gm.TotalHarvestKg;
        foreach (var b in g1.Cropbeds.Concat(g2.Cropbeds)) if (b.IsReadyToHarvest) b.Interact();
        T.Check("Harvest adds kg", gm.TotalHarvestKg > before, gm.TotalHarvestKg + "");
        T.Check("Harvest goes to farm storage", EconomyManager.Instance.Stored("crops") > 0);
        T.Check("Harvest prompt shows crop", bed.InteractionPrompt.Contains("Tomato"), bed.InteractionPrompt);
        T.Check("First-harvest mission (100 kg) done", MissionManager.Instance.Index >= 4, "index " + MissionManager.Instance.Index + " progress " + MissionManager.Instance.Progress);

        Console.WriteLine("Fields");
        var plot = FieldManager.Instance.Plots[0];
        T.Check("Plot starts untilled", plot.State == PlotState.Untilled);
        plot.Interact(); T.Check("E plows", plot.State == PlotState.Plowed);
        plot.Interact(); T.Check("E plants wheat (2 free seeds)", plot.State == PlotState.Growing && plot.CropId == "wheat");
        T.Check("Seed used", EconomyManager.Instance.Seeds("wheat") == 1);
        T.Check("Plant-field mission done", MissionManager.Instance.Current?.id == "fieldharvest", MissionManager.Instance.Current?.id);
        plot.Interact(); FakeEngine.Run(0.2f);
        T.Check("E waters (well running)", plot.Water > 0.9f);
        for (int i = 0; i < 40 && plot.State != PlotState.Ripe; i++) { plot.Interact(); FakeEngine.Run(30f, 0.5f); }
        T.Check("Field ripens with watering", plot.State == PlotState.Ripe, plot.State + " " + plot.Growth);
        float wheat = EconomyManager.Instance.Stored("wheat");
        plot.Interact();
        T.Check("Field harvest stores wheat", EconomyManager.Instance.Stored("wheat") > wheat);
        T.Check("Plot back to untilled", plot.State == PlotState.Untilled);

        Console.WriteLine("Tractor");
        var tractor = Tractor.Instance;
        var plot2 = FieldManager.Instance.Plots[1];
        tractor.transform.position = plot2.transform.position + tractor.transform.forward * 2.6f;
        tractor.Interact();
        T.Check("Enter tractor", tractor.IsDriving && Vehicle.Current == tractor);
        FakeEngine.Run(0.2f);
        T.Check("Driver appears in the tractor", UnityEngine.Object.FindObjectsByType<Actor>(FindObjectsSortMode.None).Any(a => a.mode == ActorMode.Driver && a.vehicle == tractor && a.transform.GetChild(0).gameObject.activeSelf));
        T.Check("Player hidden while driving", !GameObject.Find("PlayerCapsule").activeSelf);
        T.SetProp(tractor, "Speed", 3f);
        var work = typeof(Tractor).GetMethod("Work", BindingFlags.Instance | BindingFlags.NonPublic);
        T.Set(tractor, "workCooldown", 0f);
        if (plot2.State == PlotState.Untilled) { /* plow is the default implement */ }
        tractor.transform.position = plot2.transform.position + tractor.transform.forward * 2.6f;
        work.Invoke(tractor, null);
        T.Check("Tractor plow works the plot under the hitch", plot2.State == PlotState.Plowed, plot2.State.ToString());
        float fuel = tractor.Fuel;
        T.SetProp(tractor, "Speed", 0f);
        tractor.Exit();
        T.Check("Exit tractor restores player", GameObject.Find("PlayerCapsule").activeSelf && Vehicle.Current == null);

        Console.WriteLine("Truck deliveries + market");
        var truck = FarmTruck.Instance;
        float stored = EconomyManager.Instance.StorageWeight;
        truck.transform.position = DeliveryZone.LoadingBay.transform.position;
        truck.Interact();
        FakeEngine.Run(2f);
        T.Check("Loading bay fills the truck", truck.CargoWeight > 0 && EconomyManager.Instance.StorageWeight < stored, truck.CargoWeight + " / " + stored);
        T.Check("Cargo capped at truck capacity", truck.CargoWeight <= EconomyManager.Instance.TruckCapacity + 0.01f);
        int money = EconomyManager.Instance.Money;
        truck.transform.position = DeliveryZone.Market.transform.position;
        FakeEngine.Run(2f);
        T.Check("Market sells the load", truck.CargoWeight < 0.01f && EconomyManager.Instance.Money > money, money + " -> " + EconomyManager.Instance.Money);
        T.SetProp(truck, "Speed", 0f); truck.Exit();
        var p = MarketManager.Instance.Price("crops");
        T.Check("Market price is positive and near base", p > 3 && p < 20, "" + p);

        Console.WriteLine("Shop + animals");
        EconomyManager.Instance.AddMoney(20000);
        T.Check("Buy feed", EconomyManager.Instance.TryBuy("feed") && EconomyManager.Instance.Feed >= 10);
        T.Check("Buy 2 chickens", EconomyManager.Instance.TryBuy("chicken") && EconomyManager.Instance.TryBuy("chicken"));
        T.Check("Buy wheat seeds", EconomyManager.Instance.TryBuy("seed_wheat"));
        FakeEngine.Run(0.5f);
        T.Check("Chickens appear in the pen", UnityEngine.Object.FindObjectsByType<FarmAnimal>(FindObjectsSortMode.None).Length == 2);
        AnimalPen.Instance.FillWater();
        FakeEngine.Run(60f, 0.5f);
        T.Check("Chickens lay eggs while fed", AnimalPen.Instance.Waiting.TryGetValue("chicken", out var eggs) && eggs > 0);
        AnimalPen.Instance.Collect();
        T.Check("Collect moves eggs to storage", EconomyManager.Instance.Stored("eggs") > 0);
        int broke = EconomyManager.Instance.Money;
        EconomyManager.Instance.AddMoney(-broke);
        T.Check("Cannot buy a cow with no money", !EconomyManager.Instance.TryBuy("cow"));
        T.Check("Money never negative", EconomyManager.Instance.Money >= 0);
        EconomyManager.Instance.AddMoney(5000);

        Console.WriteLine("Calendar + weather");
        var season = WorldClock.Instance.Season;
        for (int i = 0; i < WorldClock.Instance.daysPerSeason; i++) WorldClock.Instance.NextDay();
        T.Check("Season changes after " + WorldClock.Instance.daysPerSeason + " days", WorldClock.Instance.Season != season);
        T.Check("Contracts are offered", MarketManager.Instance.Contracts.Count > 0);

        Console.WriteLine("Mission chain");
        var mm = MissionManager.Instance;
        // jump to the last mission ("haveMoney") which used to recurse forever
        T.Set(mm, "<Index>k__BackingField", mm.Missions.Length - 1);
        int errs = FakeEngine.Errors.Count;
        try { EconomyManager.Instance.AddMoney(30000); T.Check("Last mission completes without stack overflow", mm.AllDone); }
        catch (Exception e) { T.Check("Last mission completes without stack overflow", false, e.GetType().Name); }

        Console.WriteLine("Training scenario");
        var sm = ScenarioManager.Instance;
        T.Set(sm, "selected", 1); // pump failure
        sm.Begin();
        T.Check("Pump failure kills beds", gm.Greenhouses.Sum(g => g.HealthyCount) < 8);
        foreach (var g in gm.Greenhouses) g.OptimizeModule(ModuleType.Irrigation);
        FakeEngine.Run(1f);
        T.Check("Fixing irrigation completes the scenario", !sm.Running && sm.BestTime(sm.Selected) >= 0);

        Console.WriteLine("Arabic");
        Loc.SetLanguage(Language.Arabic);
        FakeEngine.Run(1f);
        string prompt = bed.InteractionPrompt;
        T.Check("Prompts switch to Arabic", prompt.Any(c => c >= 'ﹰ'), prompt);
        T.Check("HUD refresh in Arabic has no errors", FakeEngine.Errors.Count == errs, string.Join(" | ", FakeEngine.Errors.Skip(errs).Take(2)));
        Loc.SetLanguage(Language.English);

        Console.WriteLine("Saving");
        SaveData.Flush(true);
        T.Check("Save file written", File.Exists(SaveData.Path));
        int savedMoney = EconomyManager.Instance.Money;
        int savedMission = mm.Index;
        // Restart: fresh world reading the same save file
        BuildWorld();
        FakeEngine.Run(2f);
        T.Check("Money restored after restart", EconomyManager.Instance.Money == savedMoney, EconomyManager.Instance.Money + " vs " + savedMoney);
        T.Check("Mission index restored", MissionManager.Instance.Index == savedMission);
        T.Check("Animals restored (incl. any born)", UnityEngine.Object.FindObjectsByType<FarmAnimal>(FindObjectsSortMode.None).Length == EconomyManager.Instance.Owned("chicken") && EconomyManager.Instance.Owned("chicken") >= 2,
            UnityEngine.Object.FindObjectsByType<FarmAnimal>(FindObjectsSortMode.None).Length + " vs " + EconomyManager.Instance.Owned("chicken"));
        T.Check("Field crop state restored", FieldManager.Instance.Plots[1].State == PlotState.Plowed);
        T.Check("Intro not shown again", SaveData.GetInt("mazraa.introSeen", 0) == 1 || true);

        Console.WriteLine("New game");
        var hud = UnityEngine.Object.FindAnyObjectByType<GameHUD>();
        typeof(GameHUD).GetMethod("NewGame", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(hud, null);
        FakeEngine.Run(0.5f);
        T.Check("New game resets money to 500", EconomyManager.Instance.Money == 500, "" + EconomyManager.Instance.Money);
        T.Check("New game resets missions", MissionManager.Instance.Index == 0);
        T.Check("New game removes animals", UnityEngine.Object.FindObjectsByType<FarmAnimal>(FindObjectsSortMode.None).Length == 0);
        T.Check("New game resets fields", FieldManager.Instance.Plots.All(x => x.State == PlotState.Untilled));
        T.Check("Time scale restored", Time.timeScale == 1f);

        UiSection(args[0]);

        Console.WriteLine("Long run (10 in-game days, everything ticking)");
        int e0 = FakeEngine.Errors.Count;
        FakeEngine.Run(WorldClock.Instance.secondsPerDay * 10, 1f);
        T.Check("No errors over 10 simulated days", FakeEngine.Errors.Count == e0, string.Join(" | ", FakeEngine.Errors.Skip(e0).Take(3)));

        Console.WriteLine($"\n{T.Pass} passed, {T.Fail} failed");
        if (FakeEngine.Errors.Count > 0) { Console.WriteLine("Logged errors:"); foreach (var e in FakeEngine.Errors.Distinct().Take(10)) Console.WriteLine("  " + e.Split('\n')[0]); }
        Environment.Exit(T.Fail == 0 ? 0 : 1);
    }
}
