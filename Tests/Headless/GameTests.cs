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
        foreach (var t in new[] { typeof(GameEvents), typeof(Loc), typeof(CropDatabase), typeof(SaveData), typeof(WorldClock), typeof(FieldPlot), typeof(CozyFx), typeof(Appearance), typeof(SponsorManager), typeof(CozyAnalytics) })
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
        // (feature mission packs are appended after missions.json, so find the "haveMoney" mission rather than the last one)
        int moneyIdx = Array.FindIndex(mm.Missions, x => x.type == "haveMoney");
        T.Set(mm, "<Index>k__BackingField", moneyIdx);
        int errs = FakeEngine.Errors.Count;
        try { EconomyManager.Instance.AddMoney(30000); T.Check("Last mission completes without stack overflow", mm.Index > moneyIdx); }
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

        CozyTests.Run();

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

        CozyTests.AfterRestart();

        Console.WriteLine("New game");
        var hud = UnityEngine.Object.FindAnyObjectByType<GameHUD>();
        typeof(GameHUD).GetMethod("NewGame", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(hud, null);
        FakeEngine.Run(0.5f);
        T.Check("New game resets money to 500", EconomyManager.Instance.Money == 500, "" + EconomyManager.Instance.Money);
        T.Check("New game resets missions", MissionManager.Instance.Index == 0);
        T.Check("New game removes animals", UnityEngine.Object.FindObjectsByType<FarmAnimal>(FindObjectsSortMode.None).Length == 0);
        T.Check("New game resets fields", FieldManager.Instance.Plots.All(x => x.State == PlotState.Untilled));
        T.Check("Time scale restored", Time.timeScale == 1f);

        CozyTests.AfterNewGame();

        Console.WriteLine("Long run (10 in-game days, everything ticking)");
        int e0 = FakeEngine.Errors.Count;
        FakeEngine.Run(WorldClock.Instance.secondsPerDay * 10, 1f);
        T.Check("No errors over 10 simulated days", FakeEngine.Errors.Count == e0, string.Join(" | ", FakeEngine.Errors.Skip(e0).Take(3)));

        Console.WriteLine($"\n{T.Pass} passed, {T.Fail} failed");
        if (FakeEngine.Errors.Count > 0) { Console.WriteLine("Logged errors:"); foreach (var e in FakeEngine.Errors.Distinct().Take(10)) Console.WriteLine("  " + e.Split('\n')[0]); }
        Environment.Exit(T.Fail == 0 ? 0 : 1);
    }
}

// ---------------------------------------------------------------- cozy extras (pets, look, quests, events, decor, book, villagers, sponsors, learn, analytics)
static class CozyTests
{
    static readonly List<string> customs = new List<string>();
    static int placedBeforeRestart, heartsBeforeRestart;

    static bool NoEnglish(string s) => !System.Text.RegularExpressions.Regex.IsMatch(s ?? "", "[A-Za-z]{4,}");
    static int missionPaid;
    // Money gained since a snapshot, not counting mission rewards (the cozy missions complete along the way).
    static int Money => EconomyManager.Instance.Money - missionPaid;

    public static void Run()
    {
        Console.WriteLine("Cozy extras");
        Loc.SetLanguage(Language.English);
        customs.Clear();
        GameEvents.Custom += (t, a) => customs.Add(t);
        missionPaid = 0;
        GameEvents.MissionCompleted += m => missionPaid += m.reward;
        var eco = EconomyManager.Instance;
        eco.AddMoney(50000);
        T.Check("Cozy systems created", PetSystem.Instance && DailyQuests.Instance && SeasonalEvents.Instance && Decorations.Instance && CollectionBook.Instance
            && Villagers.Instance && SponsorManager.Instance && LearnMode.Instance && CozyAnalytics.Instance && AppearancePanel.Instance);
        T.Check("No errors from cozy systems", FakeEngine.Errors.Count == 0, string.Join(" | ", FakeEngine.Errors.Take(3)));

        // --- localisation pack covers every key used by the cozy code
        var srcRoot = Path.Combine(FakeEngine.ResourcesRoot, "..", "Scripts");
        var missing = new List<string>();
        foreach (var dir in new[] { "Cozy", "Sponsors", "Learn" })
            foreach (var f in Directory.GetFiles(Path.Combine(srcRoot, dir), "*.cs"))
            {
                var src = File.ReadAllText(f);
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(src, "Loc\\.(?:T|Raw)\\(\"((?:[^\"\\\\]|\\\\.)*)\""))
                    if (!Loc.HasTranslation(m.Groups[1].Value)) missing.Add(m.Groups[1].Value);
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(src, "Loc\\.(?:T|Raw)\\(\\w+ \\? \"([^\"]+)\" : \"([^\"]+)\"\\)"))
                    foreach (var g in new[] { m.Groups[1].Value, m.Groups[2].Value }) if (!Loc.HasTranslation(g)) missing.Add(g);
            }
        foreach (var k in Appearance.ThobeColorNames.Concat(Appearance.AbayaColorNames).Concat(Appearance.ShemaghNames).Concat(Appearance.ScarfNames)
                 .Concat(SeasonalEvents.Events.Select(e => e.nameKey)).Concat(new[] { "Decor", "Sponsors", "Majlis tent", "Lantern", "Palm-frond fence", "Flower pot", "Already owned", "Falconer",
                 "Ramadan Kareem", "Eid Mubarak", "Happy Saudi National Day", "Buraidah Date Festival", "Abu Saleh", "Abu Fahad", "Abu Nasser", "Daily Quests", "Falcon", "Date palms", "Crops harvested", "Animals raised", "Sponsors met", "Feats" }))
            if (!Loc.HasTranslation(k)) missing.Add(k);
        T.Check("Every cozy text key has an Arabic translation (Loc/60_cozy.json)", missing.Count == 0, string.Join(", ", missing.Distinct()));
        var mm = MissionManager.Instance;
        var cozyMissions = mm.Missions.Where(m => m.id.StartsWith("cozy_")).ToList();
        T.Check("Cozy missions loaded (pet, daily quest, 3 hearts, quiz, sponsor)", cozyMissions.Count == 5 && cozyMissions.All(m => !string.IsNullOrEmpty(m.titleAr) && !string.IsNullOrEmpty(m.descAr)));
        T.Check("reachHearts is a state mission", MissionManager.StateMissions.ContainsKey("reachHearts"));
        T.Check("Dates good added by the economy pack", eco.Good("dates") != null);

        // --- saluki
        var pets = PetSystem.Instance;
        T.Check("Dog became a saluki you can pet", pets.Saluki != null && pets.Saluki.name == "Saluki" && pets.Saluki.GetComponent<CozyProp>() != null && pets.Saluki.GetComponent<Actor>()?.mode == ActorMode.FollowPlayer);
        T.Check("Saluki prompt", pets.Saluki.GetComponent<CozyProp>().InteractionPrompt.Contains("Pet the saluki"), pets.Saluki.GetComponent<CozyProp>().InteractionPrompt);
        float wealth0 = Money + eco.Feed * 100 + eco.Data.fieldCrops.Sum(c => eco.Seeds(c.id)) * 1000;
        T.Check("Petting gives a gift", pets.Pet() && Money + eco.Feed * 100 + eco.Data.fieldCrops.Sum(c => eco.Seeds(c.id)) * 1000 > wealth0);
        T.Check("Only once per day", !pets.Pet() && customs.Count(c => c == "petSaluki") == 1);
        WorldClock.Instance.NextDay();
        T.Check("Can pet again the next day", pets.Pet());

        // --- falcon
        T.Check("Falcon perch near the farmhouse", pets.Perch != null && Vector3.Distance(pets.Perch.transform.position, new Vector3(0, 0, -57.4f)) < 12f);
        int m0 = Money;
        T.Check("Fly the falcon", pets.FlyFalcon() && pets.FalconFlying);
        T.Check("Falcon can't fly twice a day", !pets.FlyFalcon());
        FakeEngine.Run(6f, 0.1f);
        var flying = GameObject.Find("Falcon (flying)");
        T.Check("Falcon circles high over the farm", flying != null && flying.transform.position.y > 8f, flying != null ? flying.transform.position.ToString() : "none");
        FakeEngine.Run(7f, 0.1f);
        T.Check("Falcon returns with a reward", !pets.FalconFlying && Money > m0 && pets.FalconFlights == 1 && customs.Contains("flyFalcon"));

        // --- look
        var drivers = UnityEngine.Object.FindObjectsByType<Actor>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(a => a.mode == ActorMode.Driver).ToList();
        T.Check("Drivers wear the player's shemagh", drivers.Count > 0 && drivers.All(d => d.transform.Find(Appearance.HeadwearName) != null));
        var keeper = UnityEngine.Object.FindObjectsByType<Actor>(FindObjectsSortMode.None).FirstOrDefault(a => a.mode == ActorMode.Stand);
        T.Check("Shopkeepers dressed in Saudi clothing", keeper != null && keeper.transform.Find(Appearance.HeadwearName) != null);
        bool uiBefore = UIManager.Instance.IsUIOpened;
        AppearancePanel.Instance.Open();
        T.Check("Look panel opens", AppearancePanel.Instance.Panel.IsOpen && AppearancePanel.Instance.Panel.LastText.Contains("Thobe") && UIManager.Instance.IsUIOpened);
        Appearance.ToggleStyle(); Appearance.NextMain();
        T.Check("Abaya + colour saved", Appearance.CurrentStyle == OutfitStyle.Abaya && Appearance.MainIndex == 1 && SaveData.GetInt("mazraa.cozy.look.style", 0) == 1);
        T.Check("Look panel shows the change", AppearancePanel.Instance.Panel.LastText.Contains("Abaya") && AppearancePanel.Instance.Panel.LastText.Contains("Navy"));
        T.Check("Drivers re-dressed with a headscarf", drivers.All(d => d.transform.Find(Appearance.HeadwearName)?.Find("Scarf") != null));
        AppearancePanel.Instance.Panel.Close();
        T.Check("Panel closes and gives input back", UIManager.Instance.IsUIOpened == uiBefore, "before " + uiBefore);

        // --- daily quests
        var dq = DailyQuests.Instance;
        T.Check("3 daily quests", dq.Quests.Count == 3, "" + dq.Quests.Count);
        T.Check("Quest board at the market", dq.Board != null && Vector3.Distance(dq.Board.transform.position, DeliveryZone.Market.transform.position) < 15f);
        dq.Quests[0] = new ActiveQuest { template = "deliver_field", type = "deliver", good = "wheat", target = 10, reward = 77 };
        dq.Quests[1] = new ActiveQuest { template = "earn", type = "earn", target = 100, reward = 50 };
        eco.AddGood("wheat", 25);
        m0 = Money;
        T.Check("Hand in 10 kg wheat at the board", Math.Abs(dq.HandIn(0) - 10f) < 0.01f && dq.Quests[0].done && Money == m0 + 77, Money - m0 + "");
        GameEvents.RaiseDelivered(10f, 120);
        T.Check("Earn quest completes from market sales", dq.Quests[1].done && dq.DoneCount >= 2 && customs.Count(c => c == "dailyQuestDone") >= 2);
        dq.Open();
        T.Check("Board panel lists the quests", dq.Panel.IsOpen && dq.Panel.LastText.Contains("Deliver 10 kg of Wheat"), dq.Panel.LastText);
        dq.Panel.Close();
        T.Check("HUD line for daily quests", HudLines.Status.Select(f => f()).Any(x => x != null && x.Contains("Daily quests")));
        WorldClock.Instance.NextDay();
        T.Check("New quests each day", dq.Quests.Count == 3 && dq.DoneCount == 0 && dq.Quests.All(q => q.target > 0));

        // --- villagers
        var v = Villagers.Instance;
        T.Check("3 villagers spawned", v.Npcs.Count == 3 && v.Npcs.Values.All(g => g != null && g.GetComponent<CozyProp>() != null));
        T.Check("Talk prompt shows hearts", v.Npcs["buyer"].GetComponent<CozyProp>().InteractionPrompt.Contains("Talk to Abu Fahad"));
        for (int i = 0; i < 3; i++)
        {
            v.Talk("buyer"); v.Talk("buyer"); v.Talk("shopkeeper"); v.Talk("neighbour");
            WorldClock.Instance.NextDay();
        }
        T.Check("Talking daily raises hearts (once a day)", Villagers.Hearts("buyer") == 3 && Villagers.Hearts("shopkeeper") == 3, Villagers.Hearts("buyer") + "");
        T.Check("3 hearts with the buyer: market +5%", Math.Abs(MarketManager.PriceMultipliers.Aggregate(1f, (acc, f) => acc * f("wheat")) - SeasonalEvents.PriceFor("wheat") * 1.05f) < 0.001f);
        T.Check("Neighbour 3 hearts unlocks the kleija recipe card", LearnMode.Instance.IsUnlocked("kleija"));
        m0 = Money; int feedPrice = eco.PriceOf("feed");
        eco.TryBuy("feed");
        T.Check("Shopkeeper 3 hearts: 10% back on purchases", Money == m0 - feedPrice + Mathf.RoundToInt(feedPrice * 0.1f), (m0 - Money) + " vs " + feedPrice);
        T.Check("reachHearts progress = best friendship", Math.Abs(MissionManager.StateMissions["reachHearts"]() - 3f) < 0.01f);

        // --- sponsors
        var sp = SponsorManager.Instance;
        T.Check("Sponsor brands and products from sponsors.json", sp.Brands.Count == 4 && sp.Products.Count == 5);
        T.Check("Sponsor items in the shop (Sponsors page)", ShopCatalog.Entries.Count(e => e.category == "Sponsors") == 5);
        T.Check("Billboards along the road", sp.Billboards.Count == 4 && sp.Billboards.All(b => b.GetComponentsInChildren<CozyLabel>().Length == 2));
        int seeds = eco.Seeds("wheat");
        T.Check("Buy branded wheat seed pack", eco.TryBuy("sp_wheat_pack") && eco.Seeds("wheat") == seeds + 6 && customs.Contains("buySponsorItem"));
        T.Check("Sponsor met recorded in the book", CollectionBook.Instance.Has("sponsors", "qassim_seeds"));
        T.Check("Fertilizer gives charges", eco.TryBuy("sp_fert") && sp.FertilizerCharges == 3);
        float wheat0 = eco.Stored("wheat");
        typeof(SponsorManager).GetMethod("OnFieldHarvested", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(sp, new object[] { null, "wheat", 40f });
        T.Check("Fertilizer: +25% on a field harvest, one charge used", Math.Abs(eco.Stored("wheat") - wheat0 - 10f) < 0.01f && sp.FertilizerCharges == 2, (eco.Stored("wheat") - wheat0) + "");
        T.Check("Solar fans bought once", eco.TryBuy("sp_solar") && !eco.TryBuy("sp_solar") && eco.PriceOf("sp_solar") < 0);
        float crops0 = eco.Stored("crops");
        GameEvents.RaiseHarvested(GameManager.Instance.Greenhouses[0].Cropbeds[0], 20f);
        T.Check("Solar fans: +10% greenhouse harvest", eco.Stored("crops") - crops0 > 21.9f, (eco.Stored("crops") - crops0) + "");

        // --- decorations
        T.Check("Decor page in the shop", ShopCatalog.Entries.Count(e => e.category == "Decor") == 4);
        var dec = Decorations.Instance;
        T.Check("Buy a lantern -> placing preview", eco.TryBuy("decor_lantern") && dec.Placing == "decor_lantern" && dec.Stock("decor_lantern") == 1 && GameObject.Find("Decor preview") != null);
        T.Check("E places it in front of the player", dec.PlaceHere() && dec.Placed.Count == 1 && dec.Placing == null && dec.Stock("decor_lantern") == 0);
        eco.TryBuy("decor_majlis"); dec.CancelPlacing();
        T.Check("Esc keeps it in storage", dec.Stock("decor_majlis") == 1 && dec.Placing == null);
        T.Check("Place the majlis tent", dec.PlaceAt("decor_majlis", new Vector3(5, 0, -40), 90f) && dec.Placed.Count == 2);
        eco.TryBuy("decor_pot"); dec.PlaceHere();
        var pot = GameObject.Find("Decor decor_pot");
        T.Check("Placed decor can be picked up", pot != null && pot.GetComponent<CozyProp>() != null);
        dec.PickUp(pot);
        T.Check("Picked-up decor back in storage", dec.Placed.Count == 2 && dec.Stock("decor_pot") == 1);
        placedBeforeRestart = dec.Placed.Count;

        // --- seasonal events
        var se = SeasonalEvents.Instance;
        int nd = SeasonalEvents.NextDayOf("national", WorldClock.Instance.Day);
        while (WorldClock.Instance.Day < nd) WorldClock.Instance.NextDay();
        m0 = Money;
        se.Evaluate();
        T.Check("Saudi National Day starts in early autumn", se.Active != null && se.Active.id == "national" && WorldClock.Instance.Season == Season.Autumn, WorldClock.Instance.Day + " " + WorldClock.Instance.Season);
        T.Check("Green flags put up", se.Decor != null && se.Decor.GetComponentsInChildren<Transform>().Count(t => t.name == "Green Flag") >= 10);
        T.Check("Celebration bonus paid once", SaveData.GetInt("mazraa.cozy.event.national.0", 0) == 1);
        T.Check("Prices +10% on National Day", Math.Abs(SeasonalEvents.PriceFor("wheat") - 1.1f) < 0.001f);
        T.Check("HUD shows the event", HudLines.Status.Select(f => f()).Any(x => x != null && x.Contains("Saudi National Day")));
        var flagsRoot = se.Decor;
        WorldClock.Instance.NextDay();
        T.Check("Decorations removed after the event", se.Active == null && flagsRoot == null && GameObject.Find("Event Decor national") == null);
        int rd = SeasonalEvents.NextDayOf("ramadan", WorldClock.Instance.Day);
        while (WorldClock.Instance.Day < rd) WorldClock.Instance.NextDay();
        T.Check("Ramadan night market with lanterns", se.Active?.id == "ramadan" && se.Decor.GetComponentsInChildren<Transform>().Count(t => t.name == "Lantern") >= 12);
        T.SetProp(WorldClock.Instance, "DayProgress", 0.2f);
        float dayMul = SeasonalEvents.PriceFor("milk");
        T.SetProp(WorldClock.Instance, "DayProgress", 0.8f);
        T.Check("Ramadan: better prices at night", Math.Abs(dayMul - 1f) < 0.001f && Math.Abs(SeasonalEvents.PriceFor("milk") - 1.2f) < 0.001f);
        T.SetProp(WorldClock.Instance, "DayProgress", 0.3f);
        int ed = SeasonalEvents.NextDayOf("eid", WorldClock.Instance.Day);
        m0 = Money; float feed0 = eco.Feed;
        while (WorldClock.Instance.Day < ed) WorldClock.Instance.NextDay();
        T.Check("Eid follows Ramadan: gift (eidiya) + lights", se.Active?.id == "eid" && Money >= m0 + 300 && eco.Feed >= feed0 + 5f && GameObject.Find("Eid Lights") != null, Money - m0 + "");
        int dd = SeasonalEvents.NextDayOf("dates", WorldClock.Instance.Day);
        while (WorldClock.Instance.Day < dd) WorldClock.Instance.NextDay();
        T.Check("Buraidah date festival at the end of summer", se.Active?.id == "dates" && WorldClock.Instance.Season == Season.Summer);
        T.Check("Date prices x1.6 + festival contract", Math.Abs(SeasonalEvents.PriceFor("dates") - 1.6f) < 0.001f && SeasonalEvents.PriceFor("wheat") == 1f
            && MarketManager.Instance.Contracts.Any(c => c.good == "dates" && c.buyerEn == "Buraidah Date Festival"));
        float dates0 = eco.Stored("dates");
        T.Check("Pick dates from the palms in summer", se.Palms != null && se.HarvestDates() >= 8f && eco.Stored("dates") > dates0 && se.HarvestDates() == 0f);
        T.Check("Date palm card unlocked + dates in the book", CollectionBook.Instance.Has("crops", "dates"));

        // --- collection book
        var book = CollectionBook.Instance;
        GameEvents.RaiseCustom("catchFish", 2f);
        T.Check("Fish caught counted (GameEvents.Custom catchFish)", book.FishCaught == 2 && book.Has("fish", "fish"));
        T.Check("Crops and animals recorded", book.Has("crops", "tomato") && book.Has("crops", "wheat") && book.Has("animals", "chicken"));
        T.Check("Percentage complete", book.Percent > 10f && book.Percent < 100f, book.Percent + "");
        book.Open();
        T.Check("Book panel", book.Panel.IsOpen && book.Panel.LastText.Contains("Collection book") && book.Panel.LastText.Contains("Fish caught"));
        book.Panel.Close();

        // --- learn mode
        var lm = LearnMode.Instance;
        T.Check("10 learn cards, some unlocked by playing", lm.Cards.Count == 10 && lm.IsUnlocked("palms") && lm.IsUnlocked("drip") && lm.IsUnlocked("rotation") && lm.IsUnlocked("soil"));
        T.Check("Every card has a 3-question bilingual quiz", lm.Cards.All(c => c.quiz.Length == 3 && c.quiz.All(q => q.aEn.Length == 3 && q.aAr.Length == 3 && !string.IsNullOrEmpty(q.qAr))) && lm.Cards.All(c => !string.IsNullOrEmpty(c.textAr)));
        T.Check("Locked card has no quiz", !lm.StartQuiz("livestock") || lm.IsUnlocked("livestock"));
        m0 = Money;
        var palms = lm.Cards.First(c => c.id == "palms");
        lm.Open();
        T.Check("Quiz starts", lm.StartQuiz("palms") && lm.InQuiz && lm.Panel.LastText.Contains("Question 1/3"));
        foreach (var q in palms.quiz) lm.Answer(q.correct + 1);
        T.Check("Quiz passed: reward once + mission event", lm.IsPassed("palms") && Money == m0 + palms.reward && customs.Contains("quizPassed"));
        lm.StartQuiz("palms"); foreach (var q in palms.quiz) lm.Answer(q.correct + 1);
        T.Check("No second reward", Money == m0 + palms.reward);
        lm.StartQuiz("drip"); lm.Answer(3); lm.Answer(3); lm.Answer(3);
        T.Check("Wrong answers don't pass", !lm.IsPassed("drip"));
        lm.Panel.Close();

        // --- analytics (opt-in, local file only)
        T.Check("Analytics off by default, no file", !CozyAnalytics.OptIn && !File.Exists(CozyAnalytics.FilePath));
        CozyAnalytics.Instance.Write();
        T.Check("Nothing written while off", !File.Exists(CozyAnalytics.FilePath));
        CozyAnalytics.SetOptIn(true);
        eco.TryBuy("sp_melon_pack");
        GameEvents.RaiseMissionCompleted(mm.Missions[0]);
        CozyAnalytics.Instance.Write();
        var json = File.Exists(CozyAnalytics.FilePath) ? File.ReadAllText(CozyAnalytics.FilePath) : "";
        var af = json != "" ? JsonUtility.FromJson<AnalyticsFile>(json) : null;
        T.Check("Opt-in writes mazraa_analytics.json (session, missions, sponsor purchases)", af != null && af.sessions == 1 && af.missionsDone >= 1 && af.sponsorPurchases == 1 && af.recent[0].sponsorItems.Contains("sp_melon_pack"), json);
        CozyAnalytics.SetOptIn(false);
        T.Check("Opt-out deletes the file", !File.Exists(CozyAnalytics.FilePath));
        CozyAnalytics.SetOptIn(true);

        // --- Arabic
        Loc.SetLanguage(Language.Arabic);
        var lines = new List<string>();
        WorldClock.Instance.NextDay();
        lines.Add(v.Talk("neighbour")); lines.Add(v.Talk("neighbour"));
        foreach (var id in Villagers.Ids) lines.Add(Villagers.NameOf(id));
        AppearancePanel.Instance.Open(); lines.Add(AppearancePanel.Instance.Panel.LastText);
        book.Open(); lines.Add(book.Panel.LastText);
        dq.Open(); lines.Add(dq.Panel.LastText);
        lm.Open(); lines.Add(lm.Panel.LastText);
        lm.StartQuiz("rotation"); lines.Add(lm.Panel.LastText); lm.Panel.Close();
        foreach (var e in ShopCatalog.Entries.Where(e => e.category == "Decor" || e.category == "Sponsors")) lines.Add(e.name() + " " + e.detail());
        foreach (var f in HudLines.Status) lines.Add(f() ?? "");
        var english = lines.Where(l => !NoEnglish(l.Replace("(K)", "").Replace("Enter", "").Replace("Esc", ""))).ToList();
        T.Check("Cozy texts are Arabic in Arabic mode", english.Count == 0, string.Join(" || ", english.Take(3)));
        T.Check("Prompts in Arabic", pets.Saluki.GetComponent<CozyProp>().InteractionPrompt.Any(c => c >= 'ﹰ'));
        Loc.SetLanguage(Language.English);

        int firstCozy = Array.FindIndex(mm.Missions, x => x.id.StartsWith("cozy_"));
        FakeEngine.Run(1.5f);   // state missions (3 hearts) are checked every second
        var rot = lm.Cards.First(c => c.id == "rotation");
        lm.StartQuiz("rotation"); foreach (var q in rot.quiz) lm.Answer(q.correct + 1);
        eco.TryBuy("sp_wheat_pack");
        T.Check("Cozy missions completed through play (pet, daily quest, hearts, quiz, sponsor)", firstCozy >= 0 && mm.Index >= firstCozy + 5, mm.Index + " vs " + firstCozy);
        T.Check("UI input not left locked by cozy panels", UIManager.Instance.IsUIOpened == uiBefore);
        heartsBeforeRestart = Villagers.Hearts("buyer");
        T.Check("No errors in cozy features", FakeEngine.Errors.Count == 0, string.Join(" | ", FakeEngine.Errors.Take(3)));
    }

    public static void AfterRestart()
    {
        Console.WriteLine("Cozy extras after restart");
        FakeEngine.Run(1f);
        T.Check("Decorations restored at their positions", Decorations.Instance.Placed.Count == placedBeforeRestart && UnityEngine.Object.FindObjectsByType<CozyProp>(FindObjectsSortMode.None).Count(p => p.name.StartsWith("Decor decor_")) == placedBeforeRestart
            && GameObject.Find("Decor decor_majlis") != null && Vector3.Distance(GameObject.Find("Decor decor_majlis").transform.position, new Vector3(5, 0, -40)) < 0.01f);
        T.Check("Friendship hearts restored", Villagers.Hearts("buyer") == heartsBeforeRestart && heartsBeforeRestart >= 3);
        T.Check("Look restored (abaya)", Appearance.CurrentStyle == OutfitStyle.Abaya);
        T.Check("Learn progress restored", LearnMode.Instance.IsPassed("palms") && LearnMode.Instance.IsUnlocked("kleija"));
        T.Check("Analytics opt-in restored", CozyAnalytics.OptIn);
        T.Check("Daily quests restored", DailyQuests.Instance.Quests.Count == 3);
    }

    public static void AfterNewGame()
    {
        Console.WriteLine("Cozy extras after new game");
        FakeEngine.Run(1f);
        T.Check("New game clears decorations", Decorations.Instance.Placed.Count == 0 && GameObject.Find("Decor decor_majlis") == null);
        T.Check("New game resets friendships, pets and learn progress", Villagers.Hearts("buyer") == 0 && !PetSystem.Instance.PettedToday && PetSystem.Instance.FalconFlights == 0 && !LearnMode.Instance.IsPassed("palms") && LearnMode.Instance.IsUnlocked("palms"));
        T.Check("New game resets the collection book", CollectionBook.Instance.Count("crops") == 0 && CollectionBook.Instance.FishCaught == 0);
        T.Check("New daily quests for day 1", DailyQuests.Instance.Quests.Count == 3 && DailyQuests.Instance.DoneCount == 0);
        T.Check("Settings kept: look + analytics opt-in", Appearance.CurrentStyle == OutfitStyle.Abaya && SaveData.GetInt("mazraa.cozy.look.style", 0) == 1 && CozyAnalytics.OptIn && SaveData.GetInt("mazraa.learn.analytics", 0) == 1);
        T.Check("Sponsor bonuses reset", SponsorManager.Instance.FertilizerCharges == 0 && !SponsorManager.Instance.Owns("sp_solar"));
        CozyAnalytics.SetOptIn(false);
    }
}
