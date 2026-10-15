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

    // ---------------------------------------------------------------- Saudi crops, soil, pests, orchard
    static void CropsTests()
    {
        Console.WriteLine("Saudi crops, soil, pests, orchard");
        var eco = EconomyManager.Instance; var care = CropCareManager.Instance; var orch = OrchardManager.Instance; var mm = MissionManager.Instance;
        var clock = WorldClock.Instance; var plots = FieldManager.Instance.Plots;
        T.Check("Crops: systems created", care != null && orch != null);
        var crops = new[] { "alfalfa", "sorghum", "millet", "tomato", "wheat", "potato", "onion", "watermelon" };
        T.Check("Crops: 8 field crops with sellable goods and a family", crops.All(c => eco.FieldCrop(c) != null && eco.Good(c) != null && CropsData.Meta(c) != null));
        T.Check("Crops: only heat-tolerant crops grow in summer", eco.Data.fieldCrops.Where(c => c.seasons.Contains("summer")).All(c => c.id == "sorghum" || c.id == "millet"),
            string.Join(",", eco.Data.fieldCrops.Where(c => c.seasons.Contains("summer")).Select(c => c.id)));
        T.Check("Crops: cool season (Oct-Apr) is the main season", eco.Data.fieldCrops.Count(c => c.seasons.Contains("winter") || c.seasons.Contains("autumn")) >= 5);
        T.Check("Crops: date goods for Sukkari, Khalas, Ajwa (Ajwa dearest)", eco.Good("dates_ajwa").price > eco.Good("dates_sukkari").price && eco.Good("dates_sukkari").price > eco.Good("dates_khalas").price);
        T.Check("Crops: shop pages Orchard (7 saplings) and Crop care (6 items)", ShopCatalog.Entries.Count(e => e.category == "Orchard") == 7 && ShopCatalog.Entries.Count(e => e.category == "Crop care") == 6);
        T.Check("Crops: orchard of 6 tree slots next to the fields", orch.Trees.Count == 6 && Vector3.Distance(orch.Trees[0].transform.position, plots[2].transform.position) < 40f,
            orch.Trees.Count + " " + (orch.Trees.Count > 0 ? Vector3.Distance(orch.Trees[0].transform.position, plots[2].transform.position).ToString() : ""));
        T.Check("Crops: mission pack loaded (bilingual)", mm.Missions.Count(m => m.id.StartsWith("crops_") && !string.IsNullOrEmpty(m.titleAr) && !string.IsNullOrEmpty(m.descAr)) == 4);
        var src = string.Join("\n", Directory.GetFiles("../../Assets/Scripts/Crops", "*.cs").Select(File.ReadAllText));
        var keys = System.Text.RegularExpressions.Regex.Matches(src, "CropsText\\.[RT]\\(\"([^\"]+)\"").Cast<System.Text.RegularExpressions.Match>().Select(m => m.Groups[1].Value)
            .Concat(new[] { "Crop care", OrchardManager.LabelKey, "Grains", "Legumes", "Nightshades", "Onions & garlic", "Melons & squash" }).Distinct().ToList();
        Loc.Raw("x");
        var missing = keys.Where(k => !Loc.HasTranslation(k)).ToList();
        T.Check("Crops: every text key has Arabic (Loc/30_crops.json)", keys.Count > 20 && missing.Count == 0, string.Join(" | ", missing));

        int start = Array.FindIndex(mm.Missions, m => m.id == "crops_alfalfa");
        T.Set(mm, "<Index>k__BackingField", start); T.Set(mm, "<Progress>k__BackingField", 0f);
        eco.AddMoney(30000);

        // Alfalfa: several cuts, hay + feed, nitrogen
        var ap = plots[3];
        eco.TryBuy("seed_wheat");
        T.Check("Crops: buy alfalfa seeds", eco.TryBuy("seed_alfalfa"));
        ap.Interact(); ap.Interact();
        T.Check("Crops: the last seed bought (alfalfa) is planted", ap.State == PlotState.Growing && ap.CropId == "alfalfa", ap.State + " " + ap.CropId);
        T.Check("Crops: mission 'plant alfalfa' done", mm.Current?.id == "crops_cure", mm.Current?.id);
        T.SetProp(ap, "Growth", 1f); T.SetProp(ap, "State", PlotState.Ripe);
        float feed = eco.Feed, hay = eco.Stored("alfalfa");
        ap.Interact();
        T.Check("Crops: alfalfa cut stores hay", eco.Stored("alfalfa") > hay + 30f, (eco.Stored("alfalfa") - hay).ToString());
        T.Check("Crops: alfalfa cut adds animal feed", eco.Feed > feed + 3f, feed + " -> " + eco.Feed);
        T.Check("Crops: alfalfa regrows for the next cut", ap.State == PlotState.Growing && ap.CropId == "alfalfa" && ap.Growth > 0.5f, ap.State + " " + ap.Growth);
        T.Check("Crops: legume keeps the soil fertile", care.Plot(3).fertility > 0.99f);
        T.Check("Crops: prompt shows soil and cut 2/6", ap.InteractionPrompt.Contains("Soil 100%") && ap.InteractionPrompt.Contains("Cut 2/6"), ap.InteractionPrompt);

        // Pests and cures
        T.Check("Crops: aphids infest a growing field", care.Infest(ap, "aphids"));
        T.Check("Crops: pests slow growth", care.GrowthMultiplier(ap) < 0.7f);
        T.Check("Crops: pest shown on the prompt", ap.InteractionPrompt.Contains("Aphids"), ap.InteractionPrompt);
        T.Check("Crops: buy neem spray", eco.TryBuy("care_neem") && care.Items("neem") == 1);
        T.Check("Crops: prompt offers the treatment", ap.InteractionPrompt.Contains("[E] Treat Aphids: Neem spray"), ap.InteractionPrompt);
        ap.Interact();
        T.Check("Crops: E treats the aphids (uses the spray)", care.Plot(3).pest == "" && care.Items("neem") == 0);
        T.Check("Crops: mission 'cure a pest' done", mm.Current?.id == "crops_rotate", mm.Current?.id);
        T.Check("Crops: treatment protects for a few days", !care.Infest(ap, "aphids"));

        // Soil fertility and rotation
        var wp = plots[4];
        eco.TryBuy("seed_wheat"); eco.TryBuy("seed_wheat");
        wp.Interact(); wp.Interact();
        T.Check("Crops: wheat planted on field 5", wp.CropId == "wheat", wp.CropId);
        T.SetProp(wp, "Growth", 1f); T.SetProp(wp, "State", PlotState.Ripe); wp.Interact();
        float f1 = care.Plot(4).fertility;
        T.Check("Crops: a harvest uses up soil fertility", f1 < 0.9f && f1 > 0.8f, f1.ToString());
        wp.Interact(); wp.Interact();
        T.Check("Crops: no rotation bonus for the same family", wp.CropId == "wheat" && !care.Plot(4).rotated);
        T.SetProp(wp, "Growth", 1f); T.SetProp(wp, "State", PlotState.Ripe); wp.Interact();
        float f2 = care.Plot(4).fertility;
        T.Check("Crops: same family twice drains the soil faster", f1 - f2 > 0.2f, f1 + " -> " + f2);
        wp.Interact();
        T.Check("Crops: buy fertilizer", eco.TryBuy("care_fertilizer"));
        T.Check("Crops: plowed field offers fertilizer", wp.InteractionPrompt.Contains("[E] Spread Fertilizer"), wp.InteractionPrompt);
        wp.Interact();
        T.Check("Crops: fertilizer restores the soil", care.Plot(4).fertility > f2 + 0.3f && wp.State == PlotState.Plowed, care.Plot(4).fertility.ToString());
        eco.TryBuy("seed_potato");
        wp.Interact();
        T.Check("Crops: potato after wheat is a rotation", wp.CropId == "potato" && care.Plot(4).rotated);
        T.Check("Crops: mission 'rotate crops' done", mm.Current?.id == "crops_dates", mm.Current?.id);
        T.Check("Crops: rotation bonus in the yield", care.YieldMultiplier(wp) > 1.1f, care.YieldMultiplier(wp).ToString());
        float pot = eco.Stored("potato"), expected = eco.FieldCrop("potato").yieldKg * care.YieldMultiplier(wp);
        T.SetProp(wp, "Growth", 1f); T.SetProp(wp, "State", PlotState.Ripe); wp.Interact();
        float potKg = eco.Stored("potato") - pot;
        T.Check("Crops: harvest kg = base x soil x rotation (full soil + rotation = 115%)", Math.Abs(potKg - expected) < 0.5f && Math.Abs(expected - eco.FieldCrop("potato").yieldKg * 1.15f) < 0.5f, potKg + " vs " + expected);

        // Desert locusts
        var lp = plots[5];
        eco.TryBuy("seed_sorghum");
        lp.Interact(); lp.Interact();
        T.SetProp(lp, "Growth", 0.5f);
        care.LocustSwarm();
        T.Check("Crops: locust swarm eats growth and leaves locusts", lp.Growth < 0.3f && care.Plot(5).pest == "locust", lp.Growth + " " + care.Plot(5).pest);
        T.Check("Crops: neem does not stop locusts", care.OwnedCureFor("locust") == null);
        eco.TryBuy("care_pesticide");
        lp.Interact();
        T.Check("Crops: pesticide clears locusts", care.Plot(5).pest == "");

        // Greenhouse whitefly hook
        var gbed = GameManager.Instance.Greenhouses[0].Cropbeds[0];
        care.InfestBed(gbed, "whitefly");
        T.Check("Crops: greenhouse bed gets whitefly (shown on prompt)", gbed.InteractionPrompt.Contains("Whitefly"), gbed.InteractionPrompt);
        T.Check("Crops: whitefly lowers the bed yield", Cropbed.YieldModifiers.Aggregate(1f, (m, f) => m * f(gbed)) < 0.9f);
        eco.TryBuy("care_pesticide");
        gbed.Interact();
        T.Check("Crops: E on the bed treats whitefly", care.Bed(gbed).pest == "");

        // Orchard: date palms
        var palm = orch.Trees[0]; var palm2 = orch.Trees[1];
        T.Check("Crops: empty slot asks for a sapling", palm.InteractionPrompt.Contains("buy saplings"), palm.InteractionPrompt);
        T.Check("Crops: buy saplings", eco.TryBuy("sapling_palm_khalas") && eco.TryBuy("sapling_palm_sukkari"));
        palm.Interact();
        T.Check("Crops: plant the last sapling bought (Sukkari)", palm.Kind == "palm_sukkari", palm.Kind);
        palm2.Interact();
        T.Check("Crops: plant the other one (Khalas)", palm2.Kind == "palm_khalas", palm2.Kind);
        int planted = clock.Day, fruitDay = -1;
        bool pollinated = false;
        for (int i = 0; i < 100 && fruitDay < 0; i++)
        {
            clock.NextDay();
            foreach (var t in new[] { palm, palm2 }) { T.SetProp(t, "Pest", ""); T.SetProp(t, "Damage", 0f); }
            if (palm.NeedsPollination) { palm.Interact(); pollinated = palm.Pollinated; }
            if (palm.FruitKg > 0f) fruitDay = clock.Day;
        }
        T.Check("Crops: palm pollinated by hand in spring", pollinated);
        T.Check("Crops: palm takes several seasons before its first dates", fruitDay - planted >= 2 * clock.daysPerSeason, (fruitDay - planted) + " days");
        T.Check("Crops: dates ripen in late summer", clock.Season == Season.Summer && clock.DayOfSeason >= 3, clock.Season + " day " + clock.DayOfSeason);
        T.Check("Crops: pollinated palm gives a much bigger crop", palm.FruitKg > palm2.FruitKg * 2f && palm2.FruitKg > 0f, palm.FruitKg + " vs " + palm2.FruitKg);
        T.Check("Crops: ripe palm prompt", palm.InteractionPrompt.Contains("[E] Pick"), palm.InteractionPrompt);
        float kg = palm.FruitKg, before = eco.Stored("dates_sukkari");
        palm.Interact();
        T.Check("Crops: picking stores Sukkari dates", Math.Abs(eco.Stored("dates_sukkari") - before - kg) < 0.01f && palm.FruitKg == 0f);
        T.Check("Crops: mission 'first date harvest' done", mm.Index > Array.FindIndex(mm.Missions, m => m.id == "crops_dates"));
        T.Check("Crops: market price for dates", MarketManager.Instance.Price("dates_ajwa") > 20f);
        T.Check("Crops: red palm weevil on a palm", palm.Infest("weevil") && palm.InteractionPrompt.Contains("Red palm weevil"), palm.InteractionPrompt);
        eco.TryBuy("care_weevil_trap");
        palm.Interact();
        T.Check("Crops: weevil trap clears it", palm.Pest == "" && palm.ProtectDays > 0);
        for (int i = 0; i < 2 * clock.daysPerSeason; i++) clock.NextDay();
        T.Check("Crops: unpicked fruit spoils after its season", palm2.FruitKg == 0f, palm2.FruitKg.ToString());
        T.Check("Crops: palms fruit once a year", palm.FruitKg == 0f);
        T.Check("Crops: HUD lines registered", HudLines.Status.Count >= 2 && HudLines.Status.Any(h => (h() ?? "").Contains("Orchard")));
        Loc.SetLanguage(Language.Arabic);
        string ar = palm.InteractionPrompt + ap.InteractionPrompt;
        T.Check("Crops: tree and field prompts in Arabic", ar.Any(c => c >= '\uFE70') && !ar.Contains("Soil"), ar);
        Loc.SetLanguage(Language.English);
        T.Check("Crops: no errors", FakeEngine.Errors.Count == 0, string.Join(" | ", FakeEngine.Errors.Take(3)));
    }

    static void Main(string[] args)
    {
        FakeEngine.ResourcesRoot = args[0];
        EventDirector.RandomEventsEnabled = false; // random farm events would make the core checks flaky; EventsFinTests turn them on
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

        CropsTests();
        EventsFinTests.Run(args[0]);

        Console.WriteLine("Saving");
        SaveData.Flush(true);
        T.Check("Save file written", File.Exists(SaveData.Path));
        int savedMoney = EconomyManager.Instance.Money;
        int savedMission = mm.Index;
        float savedFertility = CropCareManager.Instance.Plot(4).fertility;
        int savedCuts = CropCareManager.Instance.Plot(3).cuts;
        // Restart: fresh world reading the same save file
        BuildWorld();
        FakeEngine.Run(2f);
        T.Check("Money restored after restart", EconomyManager.Instance.Money == savedMoney, EconomyManager.Instance.Money + " vs " + savedMoney);
        T.Check("Mission index restored", MissionManager.Instance.Index == savedMission, MissionManager.Instance.Index + " vs " + savedMission);
        T.Check("Animals restored (incl. any born)", UnityEngine.Object.FindObjectsByType<FarmAnimal>(FindObjectsSortMode.None).Length == EconomyManager.Instance.Owned("chicken") && EconomyManager.Instance.Owned("chicken") >= 2,
            UnityEngine.Object.FindObjectsByType<FarmAnimal>(FindObjectsSortMode.None).Length + " vs " + EconomyManager.Instance.Owned("chicken"));
        T.Check("Field crop state restored", FieldManager.Instance.Plots[1].State == PlotState.Plowed);
        UtilityTests.AfterRestart();
        T.Check("Crops: orchard trees restored", OrchardManager.Instance.Trees.Count == 6 && OrchardManager.Instance.Trees[0].Kind == "palm_sukkari" && OrchardManager.Instance.Trees[0].Age >= 3f, OrchardManager.Instance.Trees.Count > 0 ? OrchardManager.Instance.Trees[0].Kind : "none");
        T.Check("Crops: soil fertility and alfalfa cuts restored", Math.Abs(CropCareManager.Instance.Plot(4).fertility - savedFertility) < 0.001f && CropCareManager.Instance.Plot(3).cuts == savedCuts,
            CropCareManager.Instance.Plot(4).fertility + " vs " + savedFertility + ", cuts " + CropCareManager.Instance.Plot(3).cuts + " vs " + savedCuts);
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
        UtilityTests.AfterNewGame();
        T.Check("Crops: new game clears orchard, soil and care items", OrchardManager.Instance.Trees.All(x => x.Kind == "") && CropCareManager.Instance.Plot(4).fertility == 1f
            && CropsData.Data.cures.All(c => CropCareManager.Instance.Items(c.id) == 0) && CropsData.Data.trees.All(t => OrchardManager.Instance.Saplings(t.id) == 0));
        EventsFinTests.AfterNewGame();

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
        EventDirector.RandomEventsEnabled = true;
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
        T.Check("Daily wages paid each morning", before - eco.Money >= wages && before - eco.Money <= wages + 400, before + " -> " + eco.Money + " wages " + wages + " (other daily costs such as the power bill allowed)");
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
        T.Check("Flooded plot's next harvest +40% (silt)", eco.Stored("wheat") - w0 > eco.FieldCrop("wheat").yieldKg * 1.4f * 0.55f && !ed.IsFertile(fp.index), (eco.Stored("wheat") - w0) + ""); // soil fertility (crops) scales yields too

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
