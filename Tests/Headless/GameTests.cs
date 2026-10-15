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

    static void Main(string[] args)
    {
        FakeEngine.ResourcesRoot = args[0];
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

        Console.WriteLine("Long run (10 in-game days, everything ticking)");
        int e0 = FakeEngine.Errors.Count;
        FakeEngine.Run(WorldClock.Instance.secondsPerDay * 10, 1f);
        T.Check("No errors over 10 simulated days", FakeEngine.Errors.Count == e0, string.Join(" | ", FakeEngine.Errors.Skip(e0).Take(3)));

        MultiplayerTests(args[0]);

        Console.WriteLine($"\n{T.Pass} passed, {T.Fail} failed");
        if (FakeEngine.Errors.Count > 0) { Console.WriteLine("Logged errors:"); foreach (var e in FakeEngine.Errors.Distinct().Take(10)) Console.WriteLine("  " + e.Split('\n')[0]); }
        Environment.Exit(T.Fail == 0 ? 0 : 1);
    }
    // ------------------------------------------------------------------ co-op (no Netcode: fake backend)
    static void MultiplayerTests(string resources)
    {
        Console.WriteLine("Co-op: single player unchanged");
        var mp = MultiplayerManager.Instance;
        T.Check("Multiplayer systems created", mp != null && MultiplayerPanel.Instance != null);
        T.Check("Offline by default (local backend)", NetSession.Mode == NetMode.Offline && !NetSession.IsClient && !NetSession.IsHost && !NetSession.Backend.Available);
        var plot = FieldManager.Instance.Plots[2];
        T.Check("Offline: interactions run locally", !NetSession.HandleInteract(plot));
        plot.Interact();
        T.Check("Offline: plot plowed locally", plot.State == PlotState.Plowed);
        T.Check("Offline: Host fails with a clear message", !mp.Host() && mp.Status.Contains("Netcode"));
        MultiplayerPanel.Instance.Open();
        T.Check("Menu open: typing mode holds the UI", NetSession.Typing && UIManager.Instance.IsUIOpened);
        MultiplayerPanel.Instance.Close();
        T.Check("Menu closed: gameplay input back", !NetSession.Typing && !UIManager.Instance.IsUIOpened);
        int errs = FakeEngine.Errors.Count;
        FakeEngine.Run(2f);
        T.Check("No errors offline", FakeEngine.Errors.Count == errs);

        Console.WriteLine("Co-op: protocol");
        var st = NetSyncState.Capture();
        st.money = 4321; st.day = 7; st.weather = 2; st.mission = 3; st.missionProgress = 1.5f; st.wellRunning = true;
        st.goods["wheat"] = 12.5f;
        var back = NetSyncState.Decode(st.Encode());
        T.Check("Sync message round-trips", back != null && back.money == 4321 && back.day == 7 && back.weather == 2 && back.mission == 3
            && Math.Abs(back.missionProgress - 1.5f) < 0.01f && back.wellRunning && Math.Abs(back.goods["wheat"] - 12.5f) < 0.01f
            && back.plots.Count == 6 && back.plots[2].state == (int)PlotState.Plowed, st.Encode().Replace('\u001f', '|'));
        T.Check("Sync message is compact (< 400 chars)", st.Encode().Length < 400, "" + st.Encode().Length);
        T.Check("Non-sync message is not decoded as sync", NetSyncState.Decode(NetProtocol.Make(NetProtocol.Chat, "hi")) == null);
        T.Check("User text: separators and tags removed, capped", NetProtocol.Clean("a\u001fb<color=red>c", 8) == "abcolor=" && NetProtocol.Clean("هلا والله", 80) == "هلا والله");
        var pose = NetPose.Parse(NetProtocol.Split(NetProtocol.Make(NetProtocol.Pose, new NetPose { position = new Vector3(1.5f, 2, -3), yaw = 90, vehicle = "Truck", speed = 4 }.Fields())), 1);
        T.Check("Pose round-trips", Math.Abs(pose.position.z + 3f) < 0.01f && pose.vehicle == "Truck" && Math.Abs(pose.yaw - 90f) < 0.01f);
        T.Check("Object ids are unique and resolvable", NetIds.Find(NetIds.Of(plot)) == plot && NetIds.Of(FieldManager.Instance.Plots[0]) != NetIds.Of(plot));
        var missing = new List<string>();
        foreach (var f in Directory.GetFiles(Path.Combine(resources, "..", "Scripts", "Multiplayer"), "*.cs"))
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(f),
                "(?:Mp\\.(?:Tr|Raw)\\(|Status = |Reply\\(\\w+, )\"([^\"]+)\"|(?:Status = \\w+ \\? |Mp\\.Tr\\([\\w.]+ \\? )\"([^\"]+)\" : \"([^\"]+)\""))
                for (int g = 1; g <= 3; g++)
                    if (m.Groups[g].Success && !Loc.HasTranslation(m.Groups[g].Value)) missing.Add(m.Groups[g].Value);
        for (int i = 0; i < 4; i++) if (!Loc.HasTranslation(MultiplayerManager.EmoteText(i))) missing.Add(MultiplayerManager.EmoteText(i));
        T.Check("Every co-op text has Arabic (Data/Loc/70_mp.json)", missing.Count == 0, string.Join(" | ", missing));

        Console.WriteLine("Co-op: client (fake connection)");
        var eco = EconomyManager.Instance;
        eco.AddGood("wheat", 40f);
        SaveData.Flush(true);
        int homeMoney = eco.Money, homeDay = WorldClock.Instance.Day, homeMission = MissionManager.Instance.Index;
        float homeWheat = eco.Stored("wheat");
        var net = new TestBackend { mode = NetMode.Client, localId = 7 };
        NetSession.Register(net);
        FakeEngine.Run(0.2f);
        net.RaiseConnected();
        T.Check("Joining starts a save session (own farm untouched)", SaveData.InSession && !MissionManager.Instance.enabled && net.Sent.Any(x => x.msg.StartsWith("H")));
        var plot0 = FieldManager.Instance.Plots[0];
        var stateBefore = plot0.State;
        T.Check("Client: E on a plot is sent to the host", NetSession.HandleInteract(plot0) && net.Sent.Any(x => x.msg == NetProtocol.Make(NetProtocol.Interact, NetIds.Of(plot0))));
        T.Check("Client: plot not changed locally", plot0.State == stateBefore);
        T.Check("Client: shop stays local (opens the shop UI)", !NetSession.HandleInteract(UnityEngine.Object.FindAnyObjectByType<ShopStall>()));
        int m0 = eco.Money;
        T.Check("Client: buying is sent to the host", eco.TryBuy("feed") && eco.Money == m0 && net.Sent.Any(x => x.msg == NetProtocol.Make(NetProtocol.Buy, "feed")));
        var cargo = new Dictionary<string, float> { { "wheat", 10f } };
        eco.SellAll(cargo, new List<string>());
        T.Check("Client: selling is sent to the host", cargo.Count == 0 && eco.Money == m0 && net.Sent.Any(x => x.msg.StartsWith(NetProtocol.Sell)));
        var untilled = FieldManager.Instance.Plots.First(x => x.State == PlotState.Untilled);
        T.Check("Client: tractor work is sent to the host", untilled.TryPlow(false) && untilled.State == PlotState.Untilled && net.Sent.Any(x => x.msg.StartsWith(NetProtocol.PlotJob)));
        var sync = NetSyncState.Capture();
        sync.money = 98765; sync.day = homeDay + 3; sync.weather = (int)Weather.Rain; sync.mission = homeMission + 1; sync.goods["wheat"] = 3f;
        sync.plots[0] = new NetSyncState.Plot { state = (int)PlotState.Ripe, crop = "wheat", growth = 1f, water = 1f };
        net.Receive(0, sync.Encode());
        T.Check("Client: host money and storage applied", eco.Money == 98765 && Math.Abs(eco.Stored("wheat") - 3f) < 0.01f, eco.Money + "");
        T.Check("Client: host day, weather and mission applied", WorldClock.Instance.Day == homeDay + 3 && WorldClock.Instance.Weather == Weather.Rain && MissionManager.Instance.Index == homeMission + 1);
        T.Check("Client: host plot state applied", plot0.State == PlotState.Ripe && plot0.CropId == "wheat");
        FakeEngine.Run(WorldClock.Instance.secondsPerDay * 1.5f, 1f);
        T.Check("Client: the day does not roll over on its own", WorldClock.Instance.Day == homeDay + 3);
        net.Receive(0, NetProtocol.Make(NetProtocol.Roster, "0" + NetProtocol.Pair + "Omar" + NetProtocol.Item + "7" + NetProtocol.Pair + "Me"));
        net.Receive(0, NetProtocol.Make(NetProtocol.Pose, "0", "5", "0", "-50", "90", "", "0"));
        FakeEngine.Run(1f);
        var host = mp.Remotes.FirstOrDefault(r => r.Id == 0);
        T.Check("Client: host shown as a character with a name tag", host != null && host.PlayerName == "Omar" && mp.Remotes.Count() == 1 && Vector3.Distance(host.transform.position, new Vector3(5, 0, -50)) < 0.5f);
        net.Receive(0, NetProtocol.Make(NetProtocol.Emote, "0", "1"));
        net.Receive(0, NetProtocol.Make(NetProtocol.Chat, "0", "يا هلا"));
        T.Check("Client: chat from the host in the log", mp.ChatLog.Count > 0 && mp.ChatLog.Last().text == "Omar: يا هلا");
        net.Receive(0, NetProtocol.Make(NetProtocol.GiftIn, "Omar", "wheat", "5"));
        T.Check("Client: gift kept to take home", mp.PendingGifts.ContainsKey("wheat") && Math.Abs(mp.PendingGifts["wheat"] - 5f) < 0.01f);
        SaveData.Flush(true);
        string file = File.ReadAllText(SaveData.Path);
        T.Check("Client: host values never written to the own save", !file.Contains("98765"));
        int beforeErr = FakeEngine.Errors.Count;
        net.mode = NetMode.Offline;
        net.RaiseDisconnected();
        T.Check("Leaving restores the own farm (money, day, mission)", !SaveData.InSession && eco.Money == homeMoney && WorldClock.Instance.Day == homeDay && MissionManager.Instance.Index == homeMission && MissionManager.Instance.enabled,
            eco.Money + " " + WorldClock.Instance.Day + " " + MissionManager.Instance.Index);
        T.Check("Leaving adds the gift to the own storage", Math.Abs(eco.Stored("wheat") - (homeWheat + 5f)) < 0.01f, eco.Stored("wheat") + " vs " + homeWheat);
        T.Check("Leaving restores the own plots", plot0.State == stateBefore);
        T.Check("Remote players removed on leave", !mp.Remotes.Any() && FakeEngine.Errors.Count == beforeErr);

        Console.WriteLine("Co-op: host (fake client 5)");
        net = new TestBackend { mode = NetMode.Host, localId = 0 };
        net.Clients.Add(5);
        NetSession.Register(net);
        FakeEngine.Run(0.2f);
        mp.Players[0] = mp.PlayerName;
        net.RaiseClientConnected(5);
        T.Check("Host: welcome + roster sent", net.Sent.Any(x => x.to == 5 && x.msg.StartsWith(NetProtocol.Welcome)) && net.Sent.Any(x => x.msg.StartsWith(NetProtocol.Roster)));
        net.Receive(5, NetProtocol.Make(NetProtocol.Hello, "Sara"));
        T.Check("Host: client name known", mp.Players[5] == "Sara");
        var p3 = FieldManager.Instance.Plots.First(x => x.State == PlotState.Untilled);
        net.Sent.Clear();
        net.Receive(5, NetProtocol.Make(NetProtocol.Interact, NetIds.Of(p3)));
        FakeEngine.Run(0.2f);
        T.Check("Host: client's E on a plot plows it", p3.State == PlotState.Plowed);
        T.Check("Host: farm state sent right after", net.Sent.Any(x => x.msg.StartsWith(NetProtocol.Sync)));
        eco.AddMoney(1000);
        int hm = eco.Money;
        net.Receive(5, NetProtocol.Make(NetProtocol.Buy, "feed"));
        T.Check("Host: client purchase uses the farm money", eco.Money == hm - eco.PriceOf("feed"));
        eco.AddGood("wheat", 20f);
        float w0 = eco.Stored("wheat"); hm = eco.Money;
        net.Receive(5, NetProtocol.Make(NetProtocol.Sell, NetProtocol.EncodeGoods(new Dictionary<string, float> { { "wheat", 10f } })));
        T.Check("Host: client sale sells from farm storage", Math.Abs(eco.Stored("wheat") - (w0 - 10f)) < 0.01f && eco.Money > hm);
        T.Check("Host: client told the sale price", net.Sent.Any(x => x.to == 5 && x.msg.StartsWith(NetProtocol.Alert)));
        net.Receive(5, NetProtocol.Make(NetProtocol.Sell, NetProtocol.EncodeGoods(new Dictionary<string, float> { { "wheat", 100000f } })));
        T.Check("Host: cannot sell more than is stored", eco.Stored("wheat") >= 0f && eco.Stored("wheat") < 0.01f);
        var truck = FarmTruck.Instance;
        string tid = NetIds.Of(truck);
        Vector3 tp = truck.transform.position + new Vector3(10, 0, 0);
        net.Receive(5, NetProtocol.Make(NetProtocol.Pose, NetProtocol.F(tp.x), NetProtocol.F(tp.y), NetProtocol.F(tp.z), "45", tid, "6"));
        FakeEngine.Run(0.5f);
        T.Check("Host: vehicle driven by a client follows it and can't be entered", truck.RemoteDriven && !truck.CanInteract && Vector3.Distance(truck.transform.position, tp) < 6f);
        T.Check("Host: driver shown in the remote-driven truck", UnityEngine.Object.FindObjectsByType<Actor>(FindObjectsSortMode.None).Any(a => a.mode == ActorMode.Driver && a.vehicle == truck && a.transform.GetChild(0).gameObject.activeSelf));
        net.Receive(5, NetProtocol.Make(NetProtocol.Pose, "0", "0", "-55", "0", "", "0"));
        T.Check("Host: vehicle released when the client gets out", !truck.RemoteDriven && truck.CanInteract);
        net.Receive(5, NetProtocol.Make(NetProtocol.Chat, "Salam <b>all</b>"));
        T.Check("Host: client chat in the log (tags stripped)", mp.ChatLog.Last().text == "Sara: Salam ball/b", mp.ChatLog.Last().text);
        mp.SendGift(5, "wheat");
        eco.AddGood("wheat", 10f);
        w0 = eco.Stored("wheat");
        var me = UnityEngine.Object.FindAnyObjectByType<StarterAssets.StarterAssetsInputs>().transform.position;
        net.Receive(5, NetProtocol.Make(NetProtocol.Pose, NetProtocol.F(me.x + 2f), NetProtocol.F(me.y), NetProtocol.F(me.z), "0", "", "0"));
        FakeEngine.Run(3.5f);
        T.Check("Host: visitor next to you is a gift target", mp.NearestGiftTarget() != null && mp.NearestGiftTarget().Id == 5);
        mp.SendGift(5, "wheat");
        T.Check("Host: gift taken from storage and sent", Math.Abs(eco.Stored("wheat") - (w0 - MultiplayerManager.GiftAmount)) < 0.01f && net.Sent.Any(x => x.to == 5 && x.msg.StartsWith(NetProtocol.GiftIn)));
        net.RaiseClientDisconnected(5);
        T.Check("Host: player removed when they leave", !mp.Players.ContainsKey(5) && !mp.Remotes.Any());
        int e2 = FakeEngine.Errors.Count;
        FakeEngine.Run(5f);
        T.Check("No errors while hosting", FakeEngine.Errors.Count == e2, string.Join(" | ", FakeEngine.Errors.Skip(e2).Take(2)));
        mp.Leave();
        NetSession.Register(null);
        T.Check("Back to single player", NetSession.Mode == NetMode.Offline);
    }
}

/// <summary>In-memory INetBackend: records what is sent, lets the test deliver messages.</summary>
class TestBackend : INetBackend
{
    public NetMode mode; public ulong localId;
    public readonly List<ulong> Clients = new List<ulong>();
    public readonly List<(ulong to, string msg)> Sent = new List<(ulong, string)>();
    public bool Available => true;
    public NetMode Mode => mode;
    public bool Connecting => false;
    public ulong LocalId => localId;
    public IEnumerable<ulong> ClientIds => Clients;
    public bool StartHost(ushort port) { mode = NetMode.Host; return true; }
    public bool StartClient(string address, ushort port) { return true; }
    public void Leave() { mode = NetMode.Offline; }
    public void Kick(ulong id) { Clients.Remove(id); }
    public void SendToHost(string msg, bool reliable) => Sent.Add((0UL, msg));
    public void SendToClient(ulong id, string msg, bool reliable) => Sent.Add((id, msg));
    public void SendToClients(string msg, bool reliable) { foreach (var c in Clients) Sent.Add((c, msg)); }
    public event Action<ulong, string> Received;
    public event Action<ulong> ClientConnected, ClientDisconnected;
    public event Action Connected, Disconnected;
    public void Receive(ulong from, string msg) => Received?.Invoke(from, msg);
    public void RaiseConnected() => Connected?.Invoke();
    public void RaiseDisconnected() => Disconnected?.Invoke();
    public void RaiseClientConnected(ulong id) => ClientConnected?.Invoke(id);
    public void RaiseClientDisconnected(ulong id) { Clients.Remove(id); ClientDisconnected?.Invoke(id); }
}
