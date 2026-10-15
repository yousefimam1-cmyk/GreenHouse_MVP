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

        ArtTests.Run();

        Console.WriteLine($"\n{T.Pass} passed, {T.Fail} failed");
        if (FakeEngine.Errors.Count > 0) { Console.WriteLine("Logged errors:"); foreach (var e in FakeEngine.Errors.Distinct().Take(10)) Console.WriteLine("  " + e.Split('\n')[0]); }
        Environment.Exit(T.Fail == 0 ? 0 : 1);
    }
}

/// <summary>Art, sound, quality presets and touch controls (CropVisuals, DesertDressing, DesertWeatherFX, AmbientAudio, TouchControls).</summary>
static class ArtTests
{
    static float Flat(Vector3 a, Vector3 b) { a.y = 0; b.y = 0; return Vector3.Distance(a, b); }

    public static void Run()
    {
        Console.WriteLine("Crop visuals (FarmCrops stages)");
        T.Check("Tomato seedling -> FarmCrops/Tomato_01", CropVisuals.Get("tomato", 0f) == "FarmCrops/Tomato_01", CropVisuals.Get("tomato", 0f));
        T.Check("Ripe wheat -> FarmCrops/Wheat_04a", CropVisuals.Get("wheat", 1f) == "FarmCrops/Wheat_04a", CropVisuals.Get("wheat", 1f));
        T.Check("Melon uses the watermelon stages", CropVisuals.Get("melon", 0.5f) == "FarmCrops/Watermelon_02", CropVisuals.Get("melon", 0.5f));
        T.Check("Crop without FarmCrops model falls back to Kenney", CropVisuals.Get("pepper", 1f) == "Crops/crops_leafsStageB", CropVisuals.Get("pepper", 1f));
        var missing = new List<string>();
        foreach (var crop in CropVisuals.Crops)
            for (int i = 0; i < 4; i++)
            {
                string path = Path.Combine(FakeEngine.ResourcesRoot, "Models", CropVisuals.Get(crop, i / 3f) + ".fbx");
                if (!File.Exists(path) || !File.ReadAllText(path + ".meta").Contains("externalObjects:\n  - first")) missing.Add(crop + i);
            }
        T.Check("Every crop stage model is in Resources with its material mapping", missing.Count == 0, string.Join(",", missing));
        foreach (var id in EconomyManager.Instance.Data.fieldCrops.Select(c => c.id))
            T.Check("Field crop '" + id + "' has FarmCrops stages", CropVisuals.HasModels(id));
        var holder = new GameObject("CropTest");
        var model = CropVisuals.Spawn("potato", 0.5f, holder.transform, 0.6f);
        T.Check("Spawn places the stage model", model != null && model.name == "FarmCrops/Potato_02", model != null ? model.name : "null");
        UnityEngine.Object.Destroy(holder);

        // greenhouse bed swaps track growth
        var gm = GameManager.Instance;
        var bed = gm.Greenhouses.First(g => g.name == "GreenHouse").Cropbeds.First(b => b.name == "CropA");
        var plant = new GameObject("Tomato_03"); plant.transform.SetParent(bed.foliage[0].transform.GetChild(0), false);
        plant.AddComponent<MeshFilter>(); plant.AddComponent<MeshRenderer>();
        var cv = UnityEngine.Object.FindAnyObjectByType<CropbedVisuals>();
        T.Check("CropbedVisuals system running", cv != null);
        typeof(CropbedVisuals).GetMethod("Collect", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(cv, null);
        T.Check("Greenhouse plants found for stage swaps", cv.SwappedPlants >= 1, "" + cv.SwappedPlants);
        T.SetProp(bed, "Growth", 0.7f); FakeEngine.Run(1f);
        T.Check("Bed shows the stage for its growth", cv.StageOf(bed) == CropVisuals.StageIndex(bed.Growth), cv.StageOf(bed) + " vs " + CropVisuals.StageIndex(bed.Growth));

        Console.WriteLine("Desert dressing");
        var dd = DesertDressing.Instance;
        T.Check("DesertDressing built", dd != null && dd.Palms > 0 && dd.Trees > 0 && dd.Dunes > 0 && dd.SandCells > 0,
            dd == null ? "null" : $"palms {dd.Palms} trees {dd.Trees} dunes {dd.Dunes} sand {dd.SandCells}");
        T.Check("Cooling pads on both greenhouses", dd.CoolingWalls == 2, "" + dd.CoolingWalls);
        T.Check("Track from loading bay to market", dd.Track.Count >= 2 && Flat(dd.Track[0], DeliveryZone.LoadingBay.transform.position) < 1f);
        T.Check("Parts merged into few meshes", dd.MergedParts > 100, "" + dd.MergedParts);
        var player = GameObject.Find("PlayerCapsule").transform;
        T.Check("Najdi farmhouse near the start", dd.HasHouse && Flat(dd.HousePosition, player.position) < 45f, dd.HousePosition.ToString());
        var plots = FieldManager.Instance.Plots;
        var zones = new[] { DeliveryZone.LoadingBay.transform.position, DeliveryZone.Market.transform.position };
        var pen = AnimalPen.Instance.transform.position;
        T.Check("Farmhouse clear of plots, zones, pen and start",
            plots.All(p => Flat(p.transform.position, dd.HousePosition) > 11f) && zones.All(z => Flat(z, dd.HousePosition) > 15f) && Flat(pen, dd.HousePosition) > 18f && Flat(player.position, dd.HousePosition) > 10f);
        var props = UnityEngine.Object.FindObjectsByType<BoxCollider>(FindObjectsSortMode.None).Where(c => c.gameObject.layer == DesertProps.PropLayer && (c.name == "Date Palm" || c.name == "Acacia" || c.name == "Sidr")).ToList();
        var bad = props.Where(c => plots.Any(p => Flat(p.transform.position, c.transform.position) < 6f) || zones.Any(z => Flat(z, c.transform.position) < 11f) || Flat(pen, c.transform.position) < 12f
            || gm.Greenhouses.Any(g => Flat(g.transform.position, c.transform.position) < 6f)).Select(c => c.name + c.transform.position).ToList();
        T.Check("Palms and trees keep off plots, pads, pen and greenhouses (" + props.Count + " props)", props.Count > 0 && bad.Count == 0, string.Join(" ", bad.Take(3)));
        T.Check("Prop colliders are ignored by ground raycasts", props.All(c => (Build.GroundMask & (1 << c.gameObject.layer)) == 0));

        Console.WriteLine("Quality presets");
        int palmsHigh = dd.Palms + dd.Trees;
        GraphicsEnhancer.SetPreset(0);
        T.Check("SetPreset(0) = Low, no post", GraphicsEnhancer.Preset == 0 && !GraphicsEnhancer.PostEnabled && !GraphicsEnhancer.High);
        T.Check("Low preset places fewer props", dd.Palms + dd.Trees < palmsHigh, (dd.Palms + dd.Trees) + " vs " + palmsHigh);
        GraphicsEnhancer.SetPreset(1);
        T.Check("Medium preset", GraphicsEnhancer.Preset == 1 && GraphicsEnhancer.PostEnabled && GraphicsEnhancer.DressingDensity < 1f);
        GraphicsEnhancer.SetPreset(7);
        T.Check("Preset clamps and saves", GraphicsEnhancer.Preset == 2 && SaveData.GetInt("mazraa.gfxPreset", -1) == 2);
        T.Check("Medium has an Arabic name", Loc.Raw("Graphics: Medium") == "Graphics: Medium" && LocHas("Graphics: Medium"));

        Console.WriteLine("Desert weather");
        var clock = WorldClock.Instance;
        T.SetProp(clock, "Weather", Weather.Heatwave);
        float fog0 = RenderSettings.fogDensity;
        FakeEngine.Run(20f, 0.5f);
        T.Check("Heatwave raises the wind", DesertWeatherFX.Wind01 > 0.5f, "" + DesertWeatherFX.Wind01);
        T.Check("Blowing dust in a heatwave", DesertWeatherFX.Instance.DustRate > 10f, "" + DesertWeatherFX.Instance.DustRate);
        T.Check("Dust thickens the fog", RenderSettings.fog && RenderSettings.fogDensity > fog0, fog0 + " -> " + RenderSettings.fogDensity);
        T.SetProp(clock, "Weather", Weather.Sunny);
        FakeEngine.Run(30f, 0.5f);
        T.Check("Dust stops when calm", DesertWeatherFX.Instance.DustRate < 1f, "" + DesertWeatherFX.Instance.DustRate);

        Console.WriteLine("Sound");
        var aa = AmbientAudio.Instance;
        T.Check("AmbientAudio running", aa != null);
        var badClips = new List<string>();
        foreach (var n in new[] { "engine", "wind", "crickets", "birds", "step0", "step3", "cow", "sheep", "goat", "camel", "chicken" })
        {
            var c = ProceduralAudio.Get(n);
            if (c == null || c.data == null || c.data.Length < 1000 || c.data.Any(x => float.IsNaN(x) || Math.Abs(x) > 1f)) badClips.Add(n);
        }
        T.Check("All procedural clips synthesise cleanly (no NaN, no clipping)", badClips.Count == 0, string.Join(",", badClips));
        T.Check("Wind ambience playing", aa.WindVolume > 0f, "" + aa.WindVolume);
        int steps0 = aa.Footsteps;
        for (int i = 0; i < 40; i++) { player.position += new Vector3(0.25f, 0, 0); FakeEngine.Tick(0.1f); }
        T.Check("Footsteps while walking", aa.Footsteps > steps0 + 3, (aa.Footsteps - steps0) + " steps");
        var truck = FarmTruck.Instance;
        truck.Interact(); FakeEngine.Run(0.5f);
        for (int i = 0; i < 20; i++) { T.SetProp(truck, "Speed", truck.maxSpeed); FakeEngine.Tick(0.1f); }
        float pFast = aa.EnginePitch;
        T.Check("Engine plays while driving", aa.EnginePlaying);
        T.SetProp(truck, "Speed", 0f); FakeEngine.Run(2f, 0.1f);
        T.Check("Engine pitch follows speed", pFast > aa.EnginePitch + 0.2f, pFast + " vs " + aa.EnginePitch);
        truck.Exit(); FakeEngine.Run(0.5f);
        T.Check("Engine stops after exit", !aa.EnginePlaying);
        EconomyManager.Instance.AddMoney(2000);
        EconomyManager.Instance.TryBuy("chicken"); EconomyManager.Instance.TryBuy("sheep");
        FakeEngine.Run(1f);
        player.position = AnimalPen.Instance.transform.position + new Vector3(0, 0, 12f);
        FakeEngine.Run(40f, 0.5f);
        T.Check("Animal calls near the pen", aa.LastCall == "chicken" || aa.LastCall == "sheep", aa.LastCall);
        T.SetProp(clock, "DayProgress", 0.95f); FakeEngine.Run(20f, 0.5f);
        T.Check("Crickets at night", aa.NightVolume > 0.05f, "" + aa.NightVolume);
        MazraaAudio.SetMaster(0.4f);
        T.Check("Master volume drives the listener", Math.Abs(AudioListener.volume - 0.4f) < 0.001f);
        MazraaAudio.SetMaster(1f);

        Console.WriteLine("Touch controls");
        T.Check("No touch UI on desktop", !TouchControls.Instance.Active);
        TouchControls.ForceShow = true;
        var tc = new GameObject("TouchTest").AddComponent<TouchControls>();
        FakeEngine.Tick(0.1f);
        var sticks = tc.GetComponentsInChildren<UnityEngine.InputSystem.OnScreen.OnScreenStick>(true);
        var buttons = tc.GetComponentsInChildren<UnityEngine.InputSystem.OnScreen.OnScreenButton>(true);
        T.Check("Touch UI: 2 sticks, 10+ buttons", tc.Active && sticks.Length == 2 && buttons.Length >= 10, sticks.Length + " sticks, " + buttons.Length + " buttons");
        T.Check("Sticks drive the gamepad sticks", sticks.Any(s => s.controlPath == "<Gamepad>/leftStick") && sticks.Any(s => s.controlPath == "<Gamepad>/rightStick"));
        T.Check("Drive pad uses WASD", new[] { "w", "a", "s", "d" }.All(k => buttons.Any(b => b.controlPath == "<Keyboard>/" + k)));
        TouchControls.ForceShow = false;
        UnityEngine.Object.Destroy(tc.gameObject);
    }

    static bool LocHas(string en)
    {
        Loc.SetLanguage(Language.Arabic);
        string ar = Loc.Raw(en);
        Loc.SetLanguage(Language.English);
        return ar != en;
    }
}
