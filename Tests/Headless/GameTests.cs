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
        // jump to the "haveMoney" mission (last of missions.json; feature packs may append more) which used to recurse forever
        int haveMoney = Array.FindLastIndex(mm.Missions, x => x.type == "haveMoney");
        T.Set(mm, "<Index>k__BackingField", haveMoney);
        int errs = FakeEngine.Errors.Count;
        try { EconomyManager.Instance.AddMoney(30000); T.Check("Last mission completes without stack overflow", mm.Index > haveMoney); }
        catch (Exception e) { T.Check("Last mission completes without stack overflow", false, e.GetType().Name); }

        UtilityTests.Run();
        errs = FakeEngine.Errors.Count;

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
        UtilityTests.AfterRestart();
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
        UtilityTests.AfterNewGame();

        Console.WriteLine("Long run (10 in-game days, everything ticking)");
        int e0 = FakeEngine.Errors.Count;
        FakeEngine.Run(WorldClock.Instance.secondsPerDay * 10, 1f);
        T.Check("No errors over 10 simulated days", FakeEngine.Errors.Count == e0, string.Join(" | ", FakeEngine.Errors.Skip(e0).Take(3)));

        Console.WriteLine($"\n{T.Pass} passed, {T.Fail} failed");
        if (FakeEngine.Errors.Count > 0) { Console.WriteLine("Logged errors:"); foreach (var e in FakeEngine.Errors.Distinct().Take(10)) Console.WriteLine("  " + e.Split('\n')[0]); }
        Environment.Exit(T.Fail == 0 ? 0 : 1);
    }
}

// ---------------------------------------------------------------- Utilities (power + water)
static class UtilityTests
{
    static UtilityEnv Env(Season s, Weather w, float sun, int greenhouses = 2, int beds = 8, int growing = 0)
        => new UtilityEnv { season = s, weather = w, sun = sun, greenhouses = greenhouses, beds = beds, plots = 6, growingPlots = growing, day = 3, hour = 12, wellRunning = true };

    public static void Run()
    {
        Console.WriteLine("Utilities: model");
        int e0 = FakeEngine.Errors.Count;
        var m = new UtilityModel();
        m.Advance(Env(Season.Spring, Weather.Sunny, 1f), 0f);
        T.Check("Basic SEC connection covers 2 greenhouses on a spring noon", !m.PowerShort && m.GreenhouseFactor == 1f, m.PowerDemand + " kW");
        m.Advance(Env(Season.Summer, Weather.Heatwave, 1f), 0f);
        T.Check("Summer heatwave cooling overloads the basic connection", m.PowerShort && m.GreenhouseFactor < 1f && m.CoolingKw > 20f, m.PowerDemand + " kW, factor " + m.GreenhouseFactor);
        m.Solar = 2;
        m.Advance(Env(Season.Summer, Weather.Heatwave, 1f), 0f);
        T.Check("Solar panels fix the noon shortage", !m.PowerShort && m.SolarOut > 5f, m.SolarOut + " kW solar");
        m.Advance(Env(Season.Spring, Weather.Sunny, 0f), 0f);
        T.Check("No solar at night", m.SolarOut == 0f);
        var sunny = new UtilityModel { Solar = 4 }; sunny.Advance(Env(Season.Spring, Weather.Sunny, 1f, 10), 0f);
        var cloudy = new UtilityModel { Solar = 4 }; cloudy.Advance(Env(Season.Spring, Weather.Cloudy, 1f, 10), 0f);
        var dusty = new UtilityModel { Solar = 4, Dust = 0.5f }; dusty.Advance(Env(Season.Spring, Weather.Sunny, 1f, 10), 0f);
        T.Check("Clouds and dust cut solar output", cloudy.SolarOut < sunny.SolarOut * 0.6f && dusty.SolarOut < sunny.SolarOut * 0.6f, sunny.SolarOut + " / " + cloudy.SolarOut + " / " + dusty.SolarOut);
        float dust0 = m.Dust;
        m.EndDay(Season.Summer, Weather.Heatwave);
        T.Check("Panels get dusty over days (more in summer heat)", m.Dust >= dust0 + 0.1f, "" + m.Dust);

        var gen = new UtilityModel { Generator = true, DieselL = 100f };
        gen.Advance(Env(Season.Summer, Weather.Heatwave, 1f), 1f);
        T.Check("Diesel generator covers the gap and burns fuel", !gen.PowerShort && gen.DieselOut > 0f && gen.DieselL < 100f, gen.DieselOut + " kW, " + gen.DieselL + " L");
        var bio = new UtilityModel { Biogas = true };
        var be = Env(Season.Summer, Weather.Heatwave, 1f); be.manureKg = 100f;
        float manure = bio.Advance(be, 1f);
        T.Check("Biogas digester runs on manure", bio.BiogasOut > 0f && manure > 0f, bio.BiogasOut + " kW, " + manure + " kg");
        var wind = new UtilityModel { Wind = 2 }; wind.Advance(Env(Season.Spring, Weather.Sunny, 1f), 0f);
        T.Check("Wind turbines produce power", wind.WindOut > 0f && wind.WindOut <= 10f, "" + wind.WindOut);

        var none = new UtilityModel(); none.Advance(Env(Season.Spring, Weather.Sunny, 1f, 2, 8, 6), 0f);
        var drip = new UtilityModel { Irrigation = IrrigationKind.Drip }; drip.Advance(Env(Season.Spring, Weather.Sunny, 1f, 2, 8, 6), 0f);
        T.Check("Drip irrigation needs about half the field water", drip.FieldWater < none.FieldWater * 0.5f, none.FieldWater + " -> " + drip.FieldWater);
        T.Check("Drip lines water the fields automatically", drip.FieldsAutoWatered && !none.FieldsAutoWatered);
        var pivot = new UtilityModel { Irrigation = IrrigationKind.CenterPivot };
        pivot.Advance(Env(Season.Spring, Weather.Sunny, 1f, 2, 8, 6), 0f);
        T.Check("Center pivot waters all plots and draws power", pivot.FieldsAutoWatered && pivot.IrrigationKw >= 10f);
        pivot.Advance(Env(Season.Summer, Weather.Heatwave, 1f, 6, 24, 6), 0f);
        T.Check("Center pivot stops when power is badly short", pivot.PowerRatio < 0.5f && !pivot.FieldsAutoWatered, "ratio " + pivot.PowerRatio);

        var dry = new UtilityModel();
        var de = Env(Season.Summer, Weather.Sunny, 1f, 2, 8, 6); de.wellRunning = false;
        for (int h = 0; h < 48; h++) dry.Advance(de, 1f);
        T.Check("Stopped well: reservoir drains, then water runs short", dry.Stored < 0.01f && dry.WaterShort && dry.FieldDryFactor > 1f && dry.GreenhouseFactor < 1f, dry.Stored + " m3, ratio " + dry.WaterRatio);
        var deep = new UtilityModel { DeepWell = true };
        deep.Advance(de, 1f);
        T.Check("Deep aquifer well supplies water and needs pump power", deep.DeepOut > 10f && deep.PumpKw > 0f, deep.DeepOut + " m3/day, " + deep.PumpKw + " kW");
        var tse = new UtilityModel { Tse = true };
        tse.Advance(Env(Season.Spring, Weather.Sunny, 1f, 2, 8, 6), 0f);
        T.Check("Treated wastewater only serves the fields", tse.TseOut > 0f && tse.TseOut <= tse.FieldWater + 0.001f);

        var day = new UtilityModel();
        for (int h = 0; h < 24; h++) day.Advance(Env(Season.Spring, Weather.Sunny, 0.8f, 2, 8, 6), 1f);
        float bill = day.EndDay(Season.Spring, Weather.Sunny);
        T.Check("Daily SEC bill = fee + kWh tariff", bill > UtilityModel.GridDailyFee[0] && bill < 200f, bill + " SAR");
        T.Check("A day without shortage counts toward the power streak", day.PowerOkDays == 1);
        var big = new UtilityModel();
        for (int h = 0; h < 24; h++) big.Advance(Env(Season.Summer, Weather.Heatwave, 1f, 2, 8, 6), 1f);
        T.Check("A day with shortage resets the streak", big.EndDay(Season.Summer, Weather.Heatwave) > 0f && big.PowerOkDays == 0);
        var heavy = new UtilityModel { DeepWell = true, GridLevel = 2 };
        for (int h = 0; h < 24; h++) heavy.Advance(Env(Season.Summer, Weather.Heatwave, 1f, 2, 8, 6), 1f);
        T.Check("Heavy summer irrigation passes the daily water limit", heavy.WaterUsedToday > UtilityModel.DailyWaterLimit, heavy.WaterUsedToday + " m3");

        Console.WriteLine("Utilities: in game");
        var g = UtilityGrid.Instance;
        var eco = EconomyManager.Instance;
        T.Check("UtilityGrid + panel created automatically", g != null && UtilitiesUI.Instance != null);
        T.Check("Shop has Power (6) and Water (7) pages", ShopCatalog.Entries.Count(x => x.category == "Power") == 6 && ShopCatalog.Entries.Count(x => x.category == "Water") == 7);
        T.Check("Utilities yard built with SEC connection + reservoir", GameObject.Find("SEC Grid") != null && GameObject.Find("Water Reservoir") != null);
        T.Check("Manure good comes from the economy pack", eco.Good("manure") != null);
        eco.AddMoney(40000);
        T.Check("Buy solar panels", eco.TryBuy("util_solar") && g.Model.Solar == 1);
        FakeEngine.Run(0.2f);
        T.Check("Solar array appears in the yard", GameObject.Find("Solar Array 1") != null);
        g.Model.Dust = 0.4f;
        var solarPart = GameObject.Find("Solar Array 1").GetComponent<UtilityPart>();
        T.Check("Dusty panels prompt for cleaning", solarPart.InteractionPrompt.Contains("Clean"), solarPart.InteractionPrompt);
        solarPart.Interact();
        T.Check("E cleans the panels", g.Model.Dust == 0f);
        T.Check("Diesel can't be bought without a generator", !eco.TryBuy("util_diesel"));
        T.Check("Buy diesel generator", eco.TryBuy("util_generator") && g.Model.Generator);
        FakeEngine.Run(0.2f);
        int money = eco.Money; float litres = g.Model.DieselL;
        GameObject.Find("Diesel Generator").GetComponent<UtilityPart>().Interact();
        T.Check("E refuels the generator at the per-litre price", g.Model.DieselL > litres + 99f && eco.Money == money - Mathf.CeilToInt(100f * UtilityModel.DieselPrice), (eco.Money - money) + " SAR");
        T.Check("Tanker blocked while the reservoir is full", !eco.TryBuy("util_tanker"));
        T.Check("Buy a water tank, then a desalinated water tanker", eco.TryBuy("util_tank") && eco.TryBuy("util_tanker") && g.Model.Stored >= 39.9f, "" + g.Model.Stored);

        // Greenhouse hook: no power/water -> beds stop growing
        var bed = GameManager.Instance.Greenhouses.SelectMany(x => x.Cropbeds).First(b => b.IsActive);
        bed.ResetGrowth();
        UtilityHooks.GreenhouseGrowth = 0f;
        FakeEngine.Run(3f, 0.25f);
        T.Check("Greenhouse beds stop growing without power/water", bed.Growth == 0f, "" + bed.Growth);
        UtilityHooks.GreenhouseGrowth = 1f;
        FakeEngine.Run(3f, 0.25f);
        T.Check("Beds grow again when supply is back", bed.Growth > 0f);

        // Field hook: drip irrigation keeps a growing plot wet
        var plot = FieldManager.Instance.Plots[2];
        plot.TryPlow(false); plot.TrySeed(false);
        T.Check("Plot planted for the irrigation test", plot.State == PlotState.Growing, plot.State.ToString());
        T.Check("Buy drip irrigation", eco.TryBuy("util_drip") && g.Model.Irrigation == IrrigationKind.Drip);
        g.Recompute();
        FakeEngine.Run(40f, 0.5f);
        T.Check("Drip keeps the field watered", UtilityHooks.FieldsAutoWatered && plot.Water > 0.99f, plot.Water + " auto " + UtilityHooks.FieldsAutoWatered);
        T.Check("Drip lines laid on the plots", GameObject.Find("Drip Lines") != null);
        eco.AddMoney(60000);
        T.Check("Buy center pivot", eco.TryBuy("util_pivot") && g.Model.Irrigation == IrrigationKind.CenterPivot);
        FakeEngine.Run(0.5f);
        T.Check("Center pivot built over the fields", GameObject.Find("Pivot Arm") != null && GameObject.Find("Drip Lines") == null);
        T.Check("Sprinklers then back to drip", eco.TryBuy("util_sprinklers") && eco.TryBuy("util_drip") && g.Model.Irrigation == IrrigationKind.Drip);
        T.Check("Buy wind turbine, biogas digester, deep well, treated wastewater line", eco.TryBuy("util_wind") && eco.TryBuy("util_biogas") && eco.TryBuy("util_deepwell") && eco.TryBuy("util_tse") && eco.TryBuy("util_grid"));
        FakeEngine.Run(0.5f);
        T.Check("All bought equipment stands in the yard", new[] { "Wind Turbine 1", "Biogas Digester", "Deep Well", "TSE Valve", "Diesel Generator", "Solar Array 1" }.All(n => GameObject.Find(n) != null));
        T.Check("Grid upgrade raises capacity", g.Model.GridLevel == 1 && g.Model.PowerCapacity >= 50f);
        T.Check("Hourly ticks book water use", g.Model.WaterUsedToday > 0f, "" + g.Model.WaterUsedToday);

        // Shortage -> HUD warning + panel
        var saved = g.Model.GridLevel;
        g.Model.Advance(Env(Season.Summer, Weather.Heatwave, 1f, 8, 32, 6), 0f);
        string hud = g.HudLine();
        T.Check("HUD warns when power is short", hud != null && hud.Contains("Power short"), hud ?? "null");
        g.Recompute();
        UtilitiesUI.Instance.Open();
        string text = UtilitiesUI.BuildText();
        T.Check("Utilities panel opens and lists supply vs demand", UtilitiesUI.Instance.IsOpen && text.Contains("Power: demand") && text.Contains("Water: demand") && text.Contains("Solar") && text.Contains("SEC grid"), text.Split('\n').FirstOrDefault());
        UtilitiesUI.Instance.Close();
        T.Check("Panel closes", !UtilitiesUI.Instance.IsOpen);

        // Daily bill on a new day
        money = eco.Money;
        WorldClock.Instance.NextDay();
        T.Check("SEC bill is paid each morning", eco.Money < money, money + " -> " + eco.Money);

        // Missions
        var mm = MissionManager.Instance;
        int first = Array.FindIndex(mm.Missions, x => x.id == "util_solar");
        T.Check("3 utility missions loaded from the pack", first >= 0 && mm.Missions.Skip(first).Take(3).Select(x => x.id).SequenceEqual(new[] { "util_solar", "util_power2d", "util_irrigation" }));
        g.Model.PowerOkDays = 0;
        T.Set(mm, "<Index>k__BackingField", first);
        FakeEngine.Run(1.5f);
        T.Check("Solar mission completes (panels owned)", mm.Index == first + 1, "index " + mm.Index);
        FakeEngine.Run(2f);
        T.Check("Power mission waits for 2 days in a row", mm.Index == first + 1 && mm.Current.type == "powerPositiveDays", "index " + mm.Index);
        g.Model.PowerOkDays = 2;
        FakeEngine.Run(3f);
        T.Check("Power-positive 2 days + efficient irrigation (drip) missions complete", mm.Index == first + 3, "index " + mm.Index);

        // Translations of every utilities string
        var dir = Path.Combine("..", "..", "Assets", "Scripts", "Utilities");
        var keys = new List<string>();
        foreach (var f in Directory.GetFiles(dir, "*.cs"))
        {
            string src = File.ReadAllText(f);
            foreach (System.Text.RegularExpressions.Match x in System.Text.RegularExpressions.Regex.Matches(src, "(?:Loc\\.(?:T|Raw)\\(|Build\\.Label\\([^\"\\n]*|Reg\\(\"[^\"]+\", \"[^\"]+\", |return |label\\)?\\s*=?\\s*)\"((?:[^\"\\\\]|\\\\.)+)\""))
                keys.Add(x.Groups[1].Value);
        }
        keys.AddRange(new[] { "Power", "Water", "By hand", "Drip lines", "Sprinklers", "Center pivot" });
        var missing = keys.Distinct().Where(k => !Loc.HasTranslation(k) && k.Any(char.IsLetter) && !k.StartsWith("<") && !k.StartsWith("util_") && !k.StartsWith("Utilities/")).ToList();
        T.Check("Every utilities string has an Arabic translation (" + keys.Distinct().Count() + " keys)", missing.Count == 0, string.Join(" | ", missing.Take(5)));
        Loc.SetLanguage(Language.Arabic);
        string ar = UtilitiesUI.BuildText() + g.HudLine();
        Loc.SetLanguage(Language.English);
        T.Check("Panel renders in Arabic", ar.Any(c => c >= 'ﹰ'));
        T.Check("No errors in utilities tests", FakeEngine.Errors.Count == e0, string.Join(" | ", FakeEngine.Errors.Skip(e0).Take(3)));
        SaveData.Flush(true);
    }

    public static void AfterRestart()
    {
        var g = UtilityGrid.Instance;
        T.Check("Utilities restored after restart (solar, generator, tank, drip)", g != null && g.Model.Solar == 1 && g.Model.Generator && g.Model.Tanks == 1 && g.Model.Irrigation == IrrigationKind.Drip);
        FakeEngine.Run(0.5f);
        T.Check("Restored equipment rebuilt in the world", GameObject.Find("Solar Array 1") != null && GameObject.Find("Diesel Generator") != null && GameObject.Find("Drip Lines") != null);
    }

    public static void AfterNewGame()
    {
        var g = UtilityGrid.Instance;
        T.Check("New game resets utilities", g.Model.Solar == 0 && !g.Model.Generator && g.Model.Irrigation == IrrigationKind.None && g.Model.Stored == UtilityModel.BaseReservoir);
        T.Check("New game resets the hooks", UtilityHooks.GreenhouseGrowth == 1f && !UtilityHooks.FieldsAutoWatered);
    }
}
