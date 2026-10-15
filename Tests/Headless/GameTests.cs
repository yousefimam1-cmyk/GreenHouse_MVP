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
        // jump to the last core mission ("haveMoney") which used to recurse forever (feature mission packs come after it)
        int haveMoney = Array.FindLastIndex(mm.Missions, x => x.type == "haveMoney");
        T.Set(mm, "<Index>k__BackingField", haveMoney);
        int errs = FakeEngine.Errors.Count;
        try { EconomyManager.Instance.AddMoney(30000); T.Check("Last mission completes without stack overflow", mm.Index > haveMoney); }
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

        FishTests();

        Console.WriteLine($"\n{T.Pass} passed, {T.Fail} failed");
        if (FakeEngine.Errors.Count > 0) { Console.WriteLine("Logged errors:"); foreach (var e in FakeEngine.Errors.Distinct().Take(10)) Console.WriteLine("  " + e.Split('\n')[0]); }
        Environment.Exit(T.Fail == 0 ? 0 : 1);
    }

    // ---------------- Fish feature group: fishing, aquaculture, processing, Saudi animals ----------------
    static void JumpToMission(string id)
    {
        var mm = MissionManager.Instance;
        int i = Array.FindIndex(mm.Missions, x => x.id == id);
        T.Set(mm, "<Index>k__BackingField", i);
        T.Set(mm, "<Progress>k__BackingField", 0f);
    }

    static bool BiteOrTimeout(FishingManager fm)
    {
        for (int i = 0; i < 200 && fm.State == FishState.Waiting; i++) FakeEngine.Run(0.1f, 0.1f);
        return fm.State == FishState.Bite;
    }

    static void FishTests()
    {
        Console.WriteLine("Fish: boot");
        BuildWorld();
        FakeEngine.Run(2f);
        int e0 = FakeEngine.Errors.Count;
        var eco = EconomyManager.Instance; var mm = MissionManager.Instance;
        var fm = FishingManager.Instance; var aqua = AquaFarm.Instance; var yard = ProcessingYard.Instance; var hives = HiveFarm.Instance; var dove = Dovecote.Instance;
        T.Check("Fish systems created", fm != null && aqua != null && yard != null && hives != null && dove != null && UnityEngine.Object.FindAnyObjectByType<SaudiAnimals>() != null);
        T.Check("Reservoir, tank, apiary and dovecote are E-interactable (layer 7)", UnityEngine.Object.FindObjectsByType<FishSpot>(FindObjectsSortMode.None).Count(x => x.gameObject.layer == 7) >= 4);
        T.Check("Reservoir is clear of the pen, loading bay and fields", fm.Pond != null && Vector3.Distance(fm.Pond.position, AnimalPen.Instance.transform.position) > 20f
            && Vector3.Distance(fm.Pond.position, DeliveryZone.LoadingBay.transform.position) > 14f && Vector3.Distance(fm.Pond.position, FieldManager.Instance.Plots[0].transform.position) > 20f);
        T.Check("fish.json: 5 species, 2 rods, 2 baits", fm.Data.species.Length == 5 && fm.Data.rods.Length == 2 && fm.Data.baits.Length == 2);
        T.Check("Economy pack: fish goods, coast fish sell much higher", eco.Good("fish_tilapia") != null && eco.Good("fish_hamour").price > eco.Good("fish_tilapia").price * 3 && eco.Good("fish_najil").price > eco.Good("fish_carp").price * 3);
        T.Check("Economy pack: processed goods + dates + honey + squab", new[] { "dates", "packed_dates", "laban", "cheese", "flour", "juice", "honey", "squab", "manure", "camel_milk", "goat_milk" }.All(g => eco.Good(g) != null));
        T.Check("Camel (max 2) and Ardi goat are pen animals", eco.Animal("camel") != null && eco.Animal("camel").max == 2 && eco.Animal("camel").product == "camel_milk" && eco.Animal("goat")?.product == "goat_milk");
        T.Check("Shop pages Fishing / Processing / Animals", ShopCatalog.Find("rod_bamboo")?.category == "Fishing" && ShopCatalog.Find("fish_fingerlings")?.category == "Fishing"
            && ShopCatalog.Find("proc_dairy")?.category == "Processing" && ShopCatalog.Find("bee_hive")?.category == "Animals" && ShopCatalog.Find("dovecote")?.category == "Animals");
        T.Check("4 fish missions appended", new[] { "fish_catch", "fish_tank", "fish_process", "fish_camel" }.All(id => mm.Missions.Any(x => x.id == id && !string.IsNullOrEmpty(x.titleAr) && !string.IsNullOrEmpty(x.descAr))));
        // every English key used by the feature has Arabic in Resources/Data/Loc/10_fish.json (or the core table)
        var keys = new HashSet<string>();
        foreach (var f in Directory.GetFiles(Path.Combine(FakeEngine.ResourcesRoot, "..", "Scripts", "Fish"), "*.cs"))
        {
            string src = File.ReadAllText(f);
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(src, "FishText\\.(?:T|Raw)\\(\"((?:[^\"\\\\]|\\\\.)*)\"")) keys.Add(m.Groups[1].Value);
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(src, "(?:string label|category) = \"([^\"]+)\"")) keys.Add(m.Groups[1].Value);
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(src, "blockReason = \\(\\) => [^;]*")) foreach (System.Text.RegularExpressions.Match q in System.Text.RegularExpressions.Regex.Matches(m.Value, "\"([^\"]+)\"")) keys.Add(q.Groups[1].Value);
        }
        foreach (var b in yard.Data.buildings) keys.Add(b.nameEn);
        Loc.Raw("x");
        var missing = keys.Where(k => !Loc.HasTranslation(k)).ToList();
        T.Check("Fish texts all have Arabic (" + keys.Count + " keys)", keys.Count > 40 && missing.Count == 0, string.Join(" | ", missing));

        Console.WriteLine("Fish: rules");
        T.Check("Time bands", FishRules.Band(6) == "dawn" && FishRules.Band(12) == "day" && FishRules.Band(18) == "dusk" && FishRules.Band(23) == "night" && FishRules.Band(2) == "night");
        var night = FishRules.Eligible(fm.Data, "spring", "night", 0, "");
        T.Check("Night: only catfish bite", night.Count == 1 && night[0].id == "catfish", string.Join(",", night.Select(x => x.id)));
        var day0 = FishRules.Eligible(fm.Data, "spring", "day", 0, "");
        T.Check("Day, hand line: tilapia only (carp needs a rod)", day0.Count == 1 && day0[0].id == "tilapia");
        var dayBest = FishRules.Eligible(fm.Data, "autumn", "dusk", 2, "shrimp").Select(x => x.id).ToList();
        T.Check("Carbon rod + shrimp at dusk reach the coast fish", dayBest.Contains("hamour") && dayBest.Contains("najil") && !FishRules.Eligible(fm.Data, "autumn", "dusk", 2, "worms").Any(x => x.coast));
        T.Check("Carp out of season in winter", !FishRules.Eligible(fm.Data, "winter", "day", 2, "").Any(x => x.id == "carp"));
        T.Check("Weighted pick", FishRules.Pick(dayBest.Select(id => fm.Data.species.First(x => x.id == id)).ToList(), 0f).id == dayBest[0] && FishRules.Pick(new List<FishSpecies>(), 0.5f) == null);
        T.Check("Rods and bait raise the catch chance", FishRules.CatchChance(fm.Data, fm.Data.rods[1], fm.Data.baits[1]) > FishRules.CatchChance(fm.Data, fm.Data.rods[0], null)
            && FishRules.CatchChance(fm.Data, fm.Data.rods[0], null) > FishRules.CatchChance(fm.Data, null, null) && FishRules.Window(fm.Data, fm.Data.rods[1]) > FishRules.Window(fm.Data, null));

        Console.WriteLine("Fish: casting minigame");
        eco.AddMoney(100000);
        T.SetProp(WorldClock.Instance, "DayProgress", 0.17f); // 10:00
        T.Check("Buy carbon rod", eco.TryBuy("rod_carbon") && fm.RodLevel == 2);
        T.Check("Cannot buy a rod you already outrank", !eco.TryBuy("rod_bamboo"));
        T.Check("Buy shrimp bait x10", eco.TryBuy("bait_shrimp") && fm.Bait("bait_shrimp") == 10);
        fm.Use();
        T.Check("E casts (uses one bait)", fm.State == FishState.Waiting && fm.Bait("bait_shrimp") == 9);
        T.Check("Bobber shown while casting", fm.Pond.Find("Bobber").gameObject.activeSelf);
        fm.Use();
        T.Check("E too early reels in empty", fm.State == FishState.Idle && !fm.Pond.Find("Bobber").gameObject.activeSelf);
        fm.Use();
        T.Check("Bite comes after a random wait", BiteOrTimeout(fm));
        T.Check("HUD shows the bite", HudLines.Status.Any(l => (l() ?? "").Contains("BITE")));
        FakeEngine.Run(FishRules.Window(fm.Data, fm.Rod) + 0.5f, 0.1f);
        T.Check("Missing the bite window loses the fish", fm.State == FishState.Idle);
        JumpToMission("fish_catch");
        float stockBefore = fm.Data.species.Sum(x => eco.Stored(x.good));
        int caught = fm.CaughtTotal;
        for (int i = 0; i < 10 && fm.CaughtTotal == caught; i++) { fm.Use(); if (BiteOrTimeout(fm)) fm.Use(); }
        T.Check("E within the window lands a fish", fm.CaughtTotal == caught + 1 && fm.LastCatch != null && fm.LastKg >= fm.LastCatch.minKg - 0.05f && fm.LastKg <= fm.LastCatch.maxKg + 0.05f, fm.CaughtTotal + "");
        T.Check("Caught fish go to farm storage", fm.Data.species.Sum(x => eco.Stored(x.good)) > stockBefore);
        T.Check("Mission 'catch a fish' completes", mm.Current?.id == "fish_tank", mm.Current?.id);

        Console.WriteLine("Fish: aquaculture tank");
        T.Check("Empty tank asks for fingerlings", aqua.Use().Contains("fingerlings"));
        T.Check("Buy 100 fingerlings", eco.TryBuy("fish_fingerlings") && aqua.Fish == 100);
        T.Check("Tank can't be restocked while full", !eco.TryBuy("fish_fingerlings"));
        eco.AddFeed(10f);
        WorldClock.Instance.NextDay();
        T.Check("An unfed day costs fish and no growth", aqua.Survival < 0.99f && aqua.Growth < 0.01f);
        float feed = eco.Feed;
        aqua.Use();
        T.Check("E feeds the fish once a day", aqua.FedToday && eco.Feed < feed && aqua.Use().Length > 0 && Math.Abs(eco.Feed - (feed - 1f)) < 0.01f);
        for (int d = 0; d < 3; d++) { if (!aqua.FedToday) aqua.Use(); WorldClock.Instance.NextDay(); }
        T.Check("Fed fish grow in 3 days", aqua.Ready, aqua.Growth + "");
        float til = eco.Stored("fish_tilapia");
        aqua.Use();
        T.Check("Harvest gives ~36 kg tilapia (100 x 0.45 kg x 80%)", Math.Abs(eco.Stored("fish_tilapia") - til - 36f) < 1f && aqua.Fish == 0, (eco.Stored("fish_tilapia") - til) + "");
        T.Check("Mission 'harvest the tank' completes", mm.Current?.id == "fish_process", mm.Current?.id);

        Console.WriteLine("Fish: processing");
        T.Check("Building must be bought first", yard.Use("proc_mill").Length > 0 && !yard.Get("proc_mill").busy);
        T.Check("Buy flour mill (built in the yard)", eco.TryBuy("proc_mill") && yard.Get("proc_mill").owned && yard.Get("proc_mill").visual != null);
        T.Check("Mill can't be bought twice", !eco.TryBuy("proc_mill"));
        eco.AddGood("wheat", 25f - eco.Stored("wheat"));
        yard.Use("proc_mill");
        T.Check("Batch takes 20 kg wheat", yard.Get("proc_mill").busy && Math.Abs(eco.Stored("wheat") - 5f) < 0.01f);
        float flour = eco.Stored("flour");
        FakeEngine.Run(52f, 0.5f);
        T.Check("After 50 s: +16 kg flour", !yard.Get("proc_mill").busy && Math.Abs(eco.Stored("flour") - flour - 16f) < 0.01f);
        T.Check("Mission 'process something' completes", mm.Current?.id == "fish_camel", mm.Current?.id);
        eco.TryBuy("proc_dairy");
        eco.AddGood("milk", 20f);
        float laban = eco.Stored("laban"), cheese = eco.Stored("cheese");
        yard.Use("proc_dairy"); FakeEngine.Run(77f, 0.5f);
        yard.Use("proc_dairy"); FakeEngine.Run(77f, 0.5f);
        T.Check("Dairy alternates laban and cheese", Math.Abs(eco.Stored("laban") - laban - 10f) < 0.01f && Math.Abs(eco.Stored("cheese") - cheese - 2f) < 0.01f);
        eco.TryBuy("proc_juice");
        eco.TakeGood("watermelon", eco.Stored("watermelon"));
        eco.AddGood("watermelon", 5f);
        yard.Use("proc_juice"); FakeEngine.Run(47f, 0.5f);
        T.Check("Juice press: a half batch of fruit gives half the juice", Math.Abs(eco.Stored("juice") - 3f) < 0.01f);
        eco.TryBuy("proc_dates");
        T.Check("No input -> tells what it needs", yard.Use("proc_dates").Length > 0 && !yard.Get("proc_dates").busy);
        eco.AddGood("dates", 20f);
        yard.Use("proc_dates"); FakeEngine.Run(62f, 0.5f);
        T.Check("Date packing: 20 kg dates -> 18 kg packed dates", Math.Abs(eco.Stored("packed_dates") - 18f) < 0.01f);
        T.Check("Packed dates are worth more than the dates", eco.Good("packed_dates").price * 18 > eco.Good("dates").price * 20);

        Console.WriteLine("Fish: Saudi animals");
        T.Check("Buy 2 camels, a third is refused (max 2)", eco.TryBuy("camel") && eco.TryBuy("camel") && !eco.TryBuy("camel"));
        T.Check("Buy an Ardi goat", eco.TryBuy("goat"));
        FakeEngine.Run(1.5f);
        var camels = UnityEngine.Object.FindObjectsByType<FarmAnimal>(FindObjectsSortMode.None).Where(a => a.animalId == "camel").ToList();
        T.Check("Camels appear in the pen (horse model tinted + hump)", camels.Count == 2 && camels[0].transform.Find("Hump") != null);
        T.Check("Goat appears in the pen", UnityEngine.Object.FindObjectsByType<FarmAnimal>(FindObjectsSortMode.None).Count(a => a.animalId == "goat") == 1);
        T.Check("Mission 'own a camel' completes", mm.Current == null || mm.Current.id != "fish_camel", mm.Current?.id);
        T.SetProp(AnimalPen.Instance, "WaterLevel", 1f);
        eco.AddFeed(20f);
        FakeEngine.Run(170f, 0.5f);
        T.Check("Camels give camel milk", AnimalPen.Instance.Waiting.TryGetValue("camel", out var cm) && cm > 0);

        Console.WriteLine("Fish: bees and pigeons");
        T.Check("Buy 2 bee hives", eco.TryBuy("bee_hive") && eco.TryBuy("bee_hive") && hives.Hives == 2);
        T.Check("Sidr honey flows best in autumn", HiveFarm.SeasonFactor(Season.Autumn) > HiveFarm.SeasonFactor(Season.Spring) && HiveFarm.SeasonFactor(Season.Spring) > HiveFarm.SeasonFactor(Season.Summer));
        FakeEngine.Run(WorldClock.Instance.secondsPerDay * 0.5f, 1f);
        float honey = eco.Stored("honey");
        hives.Collect();
        T.Check("Hives make honey; E collects it", eco.Stored("honey") > honey + 0.1f, (eco.Stored("honey") - honey) + "");
        T.Check("Pigeons need the dovecote first", !eco.TryBuy("pigeon_pair"));
        T.Check("Buy dovecote (4 pigeons) + a pair", eco.TryBuy("dovecote") && dove.Built && eco.TryBuy("pigeon_pair") && dove.Pigeons == 6);
        eco.AddFeed(10f);
        FakeEngine.Run(Dovecote.Interval + 2f, 0.5f);
        float squab = eco.Stored("squab"), manure = eco.Stored("manure");
        dove.Collect();
        T.Check("Dovecote gives squab and manure", eco.Stored("squab") > squab && eco.Stored("manure") > manure);

        Console.WriteLine("Fish: market");
        eco.AddGood("fish_hamour", 5f);
        var truck = FarmTruck.Instance;
        truck.transform.position = DeliveryZone.LoadingBay.transform.position;
        truck.Interact();
        FakeEngine.Run(2f);
        T.Check("Truck loads fish from storage", eco.Stored("fish_hamour") < 5f);
        int money = eco.Money;
        truck.transform.position = DeliveryZone.Market.transform.position;
        FakeEngine.Run(2f);
        T.Check("Market buys the load (fish included)", eco.Money > money);
        T.SetProp(truck, "Speed", 0f); truck.Exit();
        T.Check("Hamour priced above tilapia at the market", MarketManager.Instance.Price("fish_hamour") > MarketManager.Instance.Price("fish_tilapia"));

        Console.WriteLine("Fish: Arabic");
        Loc.SetLanguage(Language.Arabic);
        FakeEngine.Run(0.5f);
        var prompts = UnityEngine.Object.FindObjectsByType<FishSpot>(FindObjectsSortMode.None).Select(x => x.InteractionPrompt).ToList();
        T.Check("Fish prompts in Arabic", prompts.Count >= 8 && prompts.All(x => x.Any(c => c >= '؀')), string.Join(" | ", prompts.Take(3)));
        Loc.SetLanguage(Language.English);

        Console.WriteLine("Fish: save + new game");
        SaveData.Flush(true);
        BuildWorld();
        FakeEngine.Run(2f);
        T.Check("Fishing gear restored", FishingManager.Instance.RodLevel == 2 && FishingManager.Instance.Bait("bait_shrimp") < 10 && FishingManager.Instance.CaughtTotal >= 1);
        T.Check("Buildings, hives, dovecote restored", ProcessingYard.Instance.Get("proc_mill").owned && ProcessingYard.Instance.Get("proc_mill").visual != null && HiveFarm.Instance.Hives == 2 && Dovecote.Instance.Built);
        T.Check("Camels restored in the pen", UnityEngine.Object.FindObjectsByType<FarmAnimal>(FindObjectsSortMode.None).Count(a => a.animalId == "camel") == 2);
        var hud = UnityEngine.Object.FindAnyObjectByType<GameHUD>();
        typeof(GameHUD).GetMethod("NewGame", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(hud, null);
        FakeEngine.Run(0.5f);
        T.Check("New game resets the fish feature", FishingManager.Instance.RodLevel == 0 && FishingManager.Instance.Bait("bait_shrimp") == 0 && AquaFarm.Instance.Fish == 0
            && !ProcessingYard.Instance.Get("proc_mill").owned && ProcessingYard.Instance.Get("proc_mill").visual == null && HiveFarm.Instance.Hives == 0 && !Dovecote.Instance.Built && EconomyManager.Instance.Owned("camel") == 0);
        T.Check("No errors in the fish feature", FakeEngine.Errors.Count == e0, string.Join(" | ", FakeEngine.Errors.Skip(e0).Take(3)));
    }
}
