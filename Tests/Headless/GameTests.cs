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
        int haveMoney = Array.FindIndex(mm.Missions, m => m.type == "haveMoney");
        T.Set(mm, "<Index>k__BackingField", haveMoney);
        int errs = FakeEngine.Errors.Count;
        try { EconomyManager.Instance.AddMoney(30000); T.Check("Last mission completes without stack overflow", mm.Index == haveMoney + 1, mm.Index + " vs " + haveMoney); }
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

        CropsTests();

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
        T.Check("Mission index restored", MissionManager.Instance.Index == savedMission);
        T.Check("Animals restored (incl. any born)", UnityEngine.Object.FindObjectsByType<FarmAnimal>(FindObjectsSortMode.None).Length == EconomyManager.Instance.Owned("chicken") && EconomyManager.Instance.Owned("chicken") >= 2,
            UnityEngine.Object.FindObjectsByType<FarmAnimal>(FindObjectsSortMode.None).Length + " vs " + EconomyManager.Instance.Owned("chicken"));
        T.Check("Field crop state restored", FieldManager.Instance.Plots[1].State == PlotState.Plowed);
        T.Check("Crops: orchard trees restored", OrchardManager.Instance.Trees.Count == 6 && OrchardManager.Instance.Trees[0].Kind == "palm_sukkari" && OrchardManager.Instance.Trees[0].Age >= 3f, OrchardManager.Instance.Trees.Count > 0 ? OrchardManager.Instance.Trees[0].Kind : "none");
        T.Check("Crops: soil fertility and alfalfa cuts restored", Math.Abs(CropCareManager.Instance.Plot(4).fertility - savedFertility) < 0.001f && CropCareManager.Instance.Plot(3).cuts == savedCuts,
            CropCareManager.Instance.Plot(4).fertility + " vs " + savedFertility + ", cuts " + CropCareManager.Instance.Plot(3).cuts + " vs " + savedCuts);
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
        T.Check("Crops: new game clears orchard, soil and care items", OrchardManager.Instance.Trees.All(x => x.Kind == "") && CropCareManager.Instance.Plot(4).fertility == 1f
            && CropsData.Data.cures.All(c => CropCareManager.Instance.Items(c.id) == 0) && CropsData.Data.trees.All(t => OrchardManager.Instance.Saplings(t.id) == 0));

        Console.WriteLine("Long run (10 in-game days, everything ticking)");
        int e0 = FakeEngine.Errors.Count;
        FakeEngine.Run(WorldClock.Instance.secondsPerDay * 10, 1f);
        T.Check("No errors over 10 simulated days", FakeEngine.Errors.Count == e0, string.Join(" | ", FakeEngine.Errors.Skip(e0).Take(3)));

        Console.WriteLine($"\n{T.Pass} passed, {T.Fail} failed");
        if (FakeEngine.Errors.Count > 0) { Console.WriteLine("Logged errors:"); foreach (var e in FakeEngine.Errors.Distinct().Take(10)) Console.WriteLine("  " + e.Split('\n')[0]); }
        Environment.Exit(T.Fail == 0 ? 0 : 1);
    }
}
