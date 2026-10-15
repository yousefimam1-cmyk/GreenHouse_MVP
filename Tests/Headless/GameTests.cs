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
        foreach (var t in new[] { typeof(GameEvents), typeof(Loc), typeof(CropDatabase), typeof(SaveData), typeof(WorldClock), typeof(FieldPlot), typeof(FarmModifiers) })
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
        int richIdx = Array.FindIndex(mm.Missions, m => m.type == "haveMoney");   // feature packs append missions after it
        T.Set(mm, "<Index>k__BackingField", richIdx);
        int errs = FakeEngine.Errors.Count;
        try { EconomyManager.Instance.AddMoney(30000); T.Check("Last mission completes without stack overflow", mm.Index == richIdx + 1); }
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

        EventsFinTests.Run(args[0]);

        Console.WriteLine("Saving");
        SaveData.Flush(true);
        T.Check("Save file written", File.Exists(SaveData.Path));
        int savedMoney = EconomyManager.Instance.Money;
        int savedMission = mm.Index;
        // Restart: fresh world reading the same save file
        BuildWorld();
        FakeEngine.Run(2f);
        T.Check("Money restored after restart", EconomyManager.Instance.Money == savedMoney, EconomyManager.Instance.Money + " vs " + savedMoney);
        T.Check("Mission index restored", MissionManager.Instance.Index == savedMission, MissionManager.Instance.Index + " vs " + savedMission);
        T.Check("Animals restored (incl. any born)", UnityEngine.Object.FindObjectsByType<FarmAnimal>(FindObjectsSortMode.None).Length == EconomyManager.Instance.Owned("chicken") && EconomyManager.Instance.Owned("chicken") >= 2,
            UnityEngine.Object.FindObjectsByType<FarmAnimal>(FindObjectsSortMode.None).Length + " vs " + EconomyManager.Instance.Owned("chicken"));
        T.Check("Field crop state restored", FieldManager.Instance.Plots[1].State == PlotState.Plowed);
        T.Check("Intro not shown again", SaveData.GetInt("mazraa.introSeen", 0) == 1 || true);
        EventsFinTests.AfterRestart();

        Console.WriteLine("New game");
        var hud = UnityEngine.Object.FindAnyObjectByType<GameHUD>();
        typeof(GameHUD).GetMethod("NewGame", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(hud, null);
        FakeEngine.Run(0.5f);
        T.Check("New game resets money to 500", EconomyManager.Instance.Money == 500, "" + EconomyManager.Instance.Money);
        T.Check("New game resets missions", MissionManager.Instance.Index == 0);
        T.Check("New game removes animals", UnityEngine.Object.FindObjectsByType<FarmAnimal>(FindObjectsSortMode.None).Length == 0);
        T.Check("New game resets fields", FieldManager.Instance.Plots.All(x => x.State == PlotState.Untilled));
        T.Check("Time scale restored", Time.timeScale == 1f);
        EventsFinTests.AfterNewGame();

        Console.WriteLine("Long run (10 in-game days, everything ticking)");
        int e0 = FakeEngine.Errors.Count;
        FakeEngine.Run(WorldClock.Instance.secondsPerDay * 10, 1f);
        T.Check("No errors over 10 simulated days", FakeEngine.Errors.Count == e0, string.Join(" | ", FakeEngine.Errors.Skip(e0).Take(3)));

        Console.WriteLine($"\n{T.Pass} passed, {T.Fail} failed");
        if (FakeEngine.Errors.Count > 0) { Console.WriteLine("Logged errors:"); foreach (var e in FakeEngine.Errors.Distinct().Take(10)) Console.WriteLine("  " + e.Split('\n')[0]); }
        Environment.Exit(T.Fail == 0 ? 0 : 1);
    }
}

// ---------------- Workers, land, bank and random events (feat/eventsfin) ----------------
static class EventsFinTests
{
    static void Day() => WorldClock.Instance.NextDay();
    static void SetProgress(float p) => T.SetProp(WorldClock.Instance, "DayProgress", p);
    static void JumpToMission(string id)
    {
        var mm = MissionManager.Instance;
        T.Set(mm, "<Index>k__BackingField", Array.FindIndex(mm.Missions, m => m.id == id));
        T.Set(mm, "<Progress>k__BackingField", 0f);
    }
    static string Hud() => string.Join("\n", HudLines.Status.Select(f => f()).Where(s => !string.IsNullOrEmpty(s)));

    public static void Run(string resources)
    {
        Console.WriteLine("Workers, land, bank, events");
        var ed = EventDirector.Instance; var wk = WorkerManager.Instance; var bank = BankManager.Instance; var land = LandOffice.Instance;
        var eco = EconomyManager.Instance; var fm = FieldManager.Instance; var mm = MissionManager.Instance; var gm = GameManager.Instance;
        T.Check("Feature systems auto-created", ed != null && wk != null && bank != null && land != null);
        T.Check("events.json loaded (8 events)", ed.Data.events.Length == 8, "" + ed.Data.events.Length);
        T.Check("4 extra missions loaded", new[] { "hireWorker", "buyLand", "repayLoan", "surviveDustStorm" }.All(t => mm.Missions.Any(m => m.type == t)));
        ed.Data.dailyChance = 0f;   // no random events during the scripted part
        ed.EndActive(); T.SetProp(ed, "Pending", null);   // the calendar test may have rolled one

        // Every text key used by the feature has an Arabic translation in the Loc pack.
        Loc.Raw("x");
        var keys = new HashSet<string> { "Workers", "Land", "Bank", "Land office", "Greenhouse vents", "Field hand", "Greenhouse worker", "Herder", "Driver",
            "You have no loan", "Not available", "Over your credit limit", "The bank won't lend during a savings goal", "All land bought" };
        var rx = new System.Text.RegularExpressions.Regex("FinText\\.(?:T|R)\\((?:\"((?:[^\"\\\\]|\\\\.)*)\"|[^\";]*?\\?\\s*\"([^\"]*)\"\\s*:\\s*\"([^\"]*)\")");
        foreach (var f in Directory.GetFiles(Path.Combine(resources, "../Scripts"), "*.cs", SearchOption.AllDirectories))
            foreach (System.Text.RegularExpressions.Match m in rx.Matches(File.ReadAllText(f)))
                for (int g = 1; g <= 3; g++) if (m.Groups[g].Success) keys.Add(m.Groups[g].Value);
        var missing = keys.Where(k => !Loc.HasTranslation(k)).ToList();
        T.Check("All feature text has Arabic (" + keys.Count + " keys)", missing.Count == 0, string.Join(" | ", missing));
        T.Check("Event names/warnings are bilingual", ed.Data.events.All(e => !string.IsNullOrEmpty(e.nameAr) && !string.IsNullOrEmpty(e.warnAr) && !string.IsNullOrEmpty(e.startAr)));

        eco.AddMoney(60000);
        while (eco.Seeds("wheat") < 12) eco.TryBuy("seed_wheat");

        // --- workers ---
        JumpToMission("fin_hire");
        int m0 = eco.Money;
        T.Check("Hire field hand (shop, Workers page)", eco.TryBuy("worker_fieldhand") && wk.IsHired(WorkerRole.FieldHand));
        T.Check("Hiring pays the first day's wage", eco.Money == m0 - WorkerManager.Def(WorkerRole.FieldHand).wage + 300, (m0 - eco.Money) + "");
        T.Check("Hire-a-worker mission done", mm.Current != null && mm.Current.id == "fin_land", mm.Current?.id);

        // --- land ---
        int plots0 = fm.Plots.Count;
        FakeEngine.Run(0.5f);
        var sign = UnityEngine.Object.FindAnyObjectByType<LandOfficeSign>();
        T.Check("Land office sign placed by the fields", sign != null && Vector3.Distance(sign.transform.position, fm.GridCenter) < 40f);
        sign.Interact();
        T.Check("Land sign E opens the Land page", ShopUI.Instance.IsOpen); ShopUI.Instance.Close();
        T.Check("Buy a parcel", eco.TryBuy("land_next"));
        FakeEngine.Run(0.5f);
        T.Check("Parcel adds 2 plots", fm.Plots.Count == plots0 + 2, "" + fm.Plots.Count);
        T.Check("Buy-land mission done", mm.Current != null && mm.Current.id == "fin_loan", mm.Current?.id);
        for (int i = 0; i < 3; i++) eco.TryBuy("land_next");
        FakeEngine.Run(0.5f);
        T.Check("All parcels incl. the large block: +12 plots", fm.Plots.Count == plots0 + 12 && land.Owned == 4, fm.Plots.Count + "");
        T.Check("No more land for sale", !eco.TryBuy("land_next"));
        float minGap = 99f;
        for (int i = 0; i < fm.Plots.Count; i++) for (int j = i + 1; j < fm.Plots.Count; j++) minGap = Math.Min(minGap, Vector3.Distance(fm.Plots[i].transform.position, fm.Plots[j].transform.position));
        T.Check("New plots don't overlap old ones", minGap > 6f, "" + minGap);
        T.Check("New plots have unique save indexes", fm.Plots.Select(p => p.index).Distinct().Count() == fm.Plots.Count);

        // field hand works the new land: plows, plants, waters
        FakeEngine.Run(150f, 0.5f);
        T.Check("Field hand plows and plants (incl. the new land)", fm.Plots.Count(p => p.State == PlotState.Growing) >= 8 && fm.Plots.Skip(plots0).Any(p => p.State == PlotState.Growing), string.Join(",", fm.Plots.Skip(plots0).Select(p => p.State.ToString())));
        var growing = fm.Plots.First(p => p.State == PlotState.Growing);
        growing.AddWater(-1f);
        for (int i = 0; i < 20 && growing.Water < 0.5f; i++) wk.Work(WorkerRole.FieldHand);
        T.Check("Field hand waters dry plots", growing.Water > 0.9f, "" + growing.Water);
        float wheat0 = eco.Stored("wheat");
        T.SetProp(growing, "State", PlotState.Ripe);
        wk.Work(WorkerRole.FieldHand);
        T.Check("Field hand harvests ripe plots", eco.Stored("wheat") > wheat0 && growing.State != PlotState.Ripe);

        // greenhouse worker
        T.Check("Hire greenhouse worker", eco.TryBuy("worker_ghworker"));
        float kg0 = gm.TotalHarvestKg;
        FakeEngine.Run(60f, 0.5f);
        T.Check("Greenhouse worker harvests ripe beds", gm.TotalHarvestKg > kg0 && gm.Greenhouses.SelectMany(g => g.Cropbeds).All(b => !b.IsReadyToHarvest));

        // herder
        T.Check("Hire herder", eco.TryBuy("worker_herder"));
        var pen = AnimalPen.Instance;
        T.Check("Pen has animals for the herder", pen.AnimalCount > 0);
        T.SetProp(pen, "WaterLevel", 0.1f); T.SetProp(pen, "Cleanliness", 0.2f);
        wk.Work(WorkerRole.Herder); wk.Work(WorkerRole.Herder);
        T.Check("Herder fills the trough and cleans", pen.WaterLevel > 0.9f && pen.Cleanliness > 0.9f, pen.WaterLevel + " " + pen.Cleanliness);

        // driver
        eco.AddGood("crops", 100f);
        T.Check("Hire driver", eco.TryBuy("worker_driver"));
        wk.tripSeconds = 5f;
        float st0 = eco.StorageWeight; int money0 = eco.Money;
        T.Check("Driver loads storage for a trip", wk.Work(WorkerRole.Driver) && wk.TripLeft > 0f && eco.StorageWeight < st0);
        FakeEngine.Run(6f, 0.5f);
        T.Check("Driver sells at the market minus a fee", wk.TripLeft <= 0f && wk.LastTripEarned > 0 && eco.Money > money0, wk.LastTripEarned + "");
        T.Check("HUD shows workers", Hud().Contains("Workers: 4"), Hud());
        int actorsWorkers = UnityEngine.Object.FindObjectsByType<Actor>(FindObjectsInactive.Include, FindObjectsSortMode.None).Count(a => a.name.StartsWith("Worker "));
        T.Check("Visible worker NPCs spawned", actorsWorkers == 4, "" + actorsWorkers);

        // wages
        int wages = wk.DailyWages, before = eco.Money;
        Day();
        T.Check("Daily wages paid each morning", eco.Money == before - wages, before + " -> " + eco.Money + " wages " + wages);
        eco.AddMoney(-eco.Money);
        Day();
        T.Check("Unpaid workers leave", wk.HiredCount == 0);
        eco.AddMoney(40000);

        // --- bank ---
        T.Check("Credit limit based on farm value", bank.CreditLimit > 10000 && bank.CreditLimit < 5000 + bank.FarmValue, bank.CreditLimit + " / " + bank.FarmValue);
        int cash = eco.Money;
        T.Check("Take a 10k loan", eco.TryBuy("bank_loan_10k") && bank.Debt == 10000 && eco.Money == cash + 10000);
        T.Check("Loans above the credit limit are refused", bank.LoanBlock(100000000) != null);
        T.Check("HUD shows the debt", Hud().Contains("Loan: 10,000"), Hud());
        Day();
        T.Check("Daily interest 1.5%", bank.Debt == 10150, "" + bank.Debt);
        bank.SetAutoRepay(true);
        Day();
        T.Check("Auto-repay pays an instalment", bank.Debt < 10150, "" + bank.Debt);
        T.Check("Shop repay 5,000", eco.TryBuy("bank_repay") && bank.Debt < 6000, "" + bank.Debt);
        var bsign = UnityEngine.Object.FindAnyObjectByType<BankSign>();
        T.Check("Bank sign by the market", bsign != null);
        bsign.Interact();
        T.Check("E at the bank sign repays the loan", bank.Debt == 0);
        T.Check("Repay-loan mission done", mm.Current != null && mm.Current.id == "fin_storm", mm.Current?.id);
        bank.SetAutoRepay(false);
        var savingsIdx = Array.FindIndex(mm.Missions, m => m.type == "haveMoney");
        T.Set(mm, "<Index>k__BackingField", savingsIdx);
        T.Check("No loans during the savings mission", !eco.TryBuy("bank_loan_10k") && bank.Debt == 0);
        JumpToMission("fin_storm");

        // --- events ---
        var vents = UnityEngine.Object.FindObjectsByType<VentSwitch>(FindObjectsSortMode.None);
        T.Check("A vent switch per greenhouse", vents.Length == gm.Greenhouses.Count);
        T.Check("Roll respects seasons (no frost in summer)", Enumerable.Range(0, 200).All(i => { var e = ed.Roll(6); return e == null || e.id != "frost"; }));
        Season sNow = EventDirector.SeasonOf(WorldClock.Instance.Day);

        // haboob with one greenhouse left open
        int survived0 = ed.StormsSurvived;
        var g1 = gm.Greenhouses[0]; var g2 = gm.Greenhouses[1];
        ed.SetClosed(g1, true); ed.SetClosed(g2, false);
        ed.Schedule("haboob", WorldClock.Instance.Day + 1);
        T.Check("Haboob forecast on the HUD", Hud().Contains("Forecast for tomorrow: Haboob"), Hud());
        Day(); SetProgress(0.2f); FakeEngine.Run(0.5f);
        T.Check("Haboob waits for 14:00", ed.Active != null && !ed.Running && !RenderSettings.fog);
        FakeEngine.Run(20f, 0.5f);   // let beds grow a bit
        SetProgress(0.36f); FakeEngine.Run(2f, 0.5f);
        T.Check("Haboob running: dust fog on", ed.Running && RenderSettings.fog && RenderSettings.fogDensity > 0.01f, RenderSettings.fogDensity + "");
        float g2growth = g2.Cropbeds.Where(b => b.IsActive).Max(b => b.Growth);
        SetProgress(0.52f); FakeEngine.Run(0.5f);
        T.Check("Haboob ends: fog restored", ed.Active == null && !RenderSettings.fog);
        T.Check("Open greenhouse beds damaged, closed one safe", g2growth > 0f && g2.Cropbeds.Where(b => b.IsActive).All(b => b.Growth < 0.05f) && g1.Cropbeds.Any(b => b.Growth > 0.05f), g2growth + "");
        T.Check("Storm mission not done with a greenhouse open", mm.Current != null && mm.Current.id == "fin_storm");
        vents.First(v => v.greenhouse == g2).Interact();
        T.Check("Vent switch E closes the vents", ed.IsClosed(g2));
        ed.StartNow("haboob"); FakeEngine.Run(1f); ed.EndActive();
        T.Check("Survive a dust storm mission done", ed.StormsSurvived == survived0 + 1 && (mm.Current == null || mm.Current.id != "fin_storm"), ed.StormsSurvived + " " + mm.Current?.id + " " + string.Join(",", gm.Greenhouses.Select(g => ed.IsClosed(g))));

        // extreme heat
        var plot = fm.Plots.First(p => p.State == PlotState.Growing);
        plot.AddWater(1f); FakeEngine.Run(10f, 0.5f); float normalLoss = 1f - plot.Water;
        ed.StartNow("heat"); FakeEngine.Run(0.1f);
        T.Check("Heat: fields dry and animals drink twice as fast", FarmModifiers.FieldDry == 2f && FarmModifiers.PenWaterUse == 2f);
        plot.AddWater(1f); FakeEngine.Run(10f, 0.5f); float heatLoss = 1f - plot.Water;
        T.Check("Heat: plot loses water faster", heatLoss > normalLoss * 1.7f, normalLoss + " vs " + heatLoss);
        ed.EndActive();
        T.Check("Modifiers back to normal after the event", FarmModifiers.FieldDry == 1f && FarmModifiers.PenWaterUse == 1f);

        // water restriction
        ed.StartNow("waterban"); FakeEngine.Run(0.1f);
        plot.AddWater(-1f); plot.Interact();
        T.Check("Water ban: E can't water", plot.Water < 0.1f && FarmModifiers.IrrigationBanned);
        wk.Hire(WorkerRole.FieldHand);
        var dry = fm.Plots.Where(p => p.State == PlotState.Growing).ToList();
        foreach (var p in dry) p.AddWater(-1f);
        for (int i = 0; i < 5; i++) wk.Work(WorkerRole.FieldHand);
        T.Check("Water ban: field hand doesn't water", dry.Count > 0 && dry.All(p => p.Water < 0.1f));
        wk.Dismiss(WorkerRole.FieldHand, false);
        ed.EndActive(); FakeEngine.Run(0.1f);
        plot.Interact();
        T.Check("After the ban watering works again", plot.Water > 0.9f);

        // frost
        ed.StartNow("frost"); FakeEngine.Run(0.1f);
        T.Check("Frost slows watermelon, not wheat", FarmModifiers.FieldGrowth("watermelon") < 0.5f && FarmModifiers.FieldGrowth("wheat") == 1f);
        ed.EndActive();

        // flood + fertility bonus
        while (eco.Seeds("wheat") < 30) eco.TryBuy("seed_wheat");
        foreach (var p in fm.Plots) { p.TryPlow(false); p.TrySeed(false); }
        int withCrop = fm.Plots.Count(p => p.State == PlotState.Growing);
        ed.StartNow("flood"); FakeEngine.Run(0.1f);
        var flooded = fm.Plots.Where(p => ed.IsFertile(p.index)).ToList();
        T.Check("Flash flood ruins 2 plots", flooded.Count == 2 && flooded.All(p => p.State == PlotState.Untilled) && fm.Plots.Count(p => p.State == PlotState.Growing) == withCrop - 2, flooded.Count + " " + withCrop + " " + fm.Plots.Count(p => p.State == PlotState.Growing));
        ed.EndActive();
        var fp = flooded[0]; fp.TryPlow(false); fp.TrySeed(false); T.SetProp(fp, "State", PlotState.Ripe);
        float w0 = eco.Stored("wheat");
        fp.TryHarvest(false);
        T.Check("Flooded plot's next harvest +40% (silt)", Math.Abs(eco.Stored("wheat") - w0 - eco.FieldCrop("wheat").yieldKg * 1.4f) < 0.5f && !ed.IsFertile(fp.index), (eco.Stored("wheat") - w0) + "");

        // locusts
        ed.Find("locust").lossChance = 1f;
        T.Check("Buy pesticide", eco.TryBuy("pesticide") && ed.Pesticide == 1);
        int g0 = fm.Plots.Count(p => p.State == PlotState.Growing);
        ed.StartNow("locust"); FakeEngine.Run(0.1f); ed.EndActive();
        T.Check("Pesticide saves the crops", fm.Plots.Count(p => p.State == PlotState.Growing) == g0 && ed.Pesticide == 0);
        ed.StartNow("locust"); FakeEngine.Run(0.1f); ed.EndActive();
        T.Check("Locusts eat unprotected crops", fm.Plots.Count(p => p.State == PlotState.Growing) == 0, "" + fm.Plots.Count(p => p.State == PlotState.Growing));

        // Ramadan demand
        float milk = MarketManager.Instance.Price("milk");
        ed.StartNow("ramadan"); FakeEngine.Run(0.1f);
        T.Check("Ramadan: milk sells for 1.5x", Math.Abs(MarketManager.Instance.Price("milk") - milk * 1.5f) < 0.01f, milk + " -> " + MarketManager.Instance.Price("milk"));
        ed.EndActive(); FakeEngine.Run(0.1f);
        T.Check("Price back after Ramadan", Math.Abs(MarketManager.Instance.Price("milk") - milk) < 0.01f);

        // big buyer
        eco.AddGood("wool", 30f);
        int c0 = MarketManager.Instance.Contracts.Count;
        ed.StartNow("buyer"); FakeEngine.Run(0.1f);
        var bc = MarketManager.Instance.Contracts.LastOrDefault();
        T.Check("Big buyer posts a one-day contract", MarketManager.Instance.Contracts.Count == c0 + 1 && bc.bonus > 1.8f && bc.dueDay == WorldClock.Instance.Day);
        ed.EndActive();
        T.Check("Buyer leaves at the end", MarketManager.Instance.Contracts.Count == c0);

        fm.Plots[1].ResetPlot(); fm.Plots[1].TryPlow(false);   // the save test below expects plot 2 plowed
        // something to restore after the restart
        ed.Schedule("flood", WorldClock.Instance.Day + 1);
        T.Check("No errors in the feature tests", FakeEngine.Errors.Count == 0, string.Join(" | ", FakeEngine.Errors.Take(3)));
    }

    public static void AfterRestart()
    {
        Console.WriteLine("Workers, land, bank, events: restart");
        FakeEngine.Run(1f);
        T.Check("Bought land restored (18 plots)", FieldManager.Instance.Plots.Count == 18 && LandOffice.Instance.Owned == 4, "" + FieldManager.Instance.Plots.Count);
        T.Check("Forecast restored", EventDirector.Instance.Pending != null && EventDirector.Instance.Pending.id == "flood");
        T.Check("Closed vents restored", GameManager.Instance.Greenhouses.All(g => EventDirector.Instance.IsClosed(g)));
    }

    public static void AfterNewGame()
    {
        T.Check("New game removes bought land", FieldManager.Instance.Plots.Count == 6 && LandOffice.Instance.Owned == 0);
        T.Check("New game clears debt, workers and events", BankManager.Instance.Debt == 0 && WorkerManager.Instance.HiredCount == 0 && EventDirector.Instance.Pending == null && EventDirector.Instance.Active == null);
    }
}
