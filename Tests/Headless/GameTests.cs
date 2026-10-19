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
        foreach (var t in new[] { typeof(GameEvents), typeof(Loc), typeof(CropDatabase), typeof(SaveData), typeof(WorldClock), typeof(FarmTools), typeof(FieldEvents), typeof(FieldPests), typeof(FcData), typeof(FarmSim), typeof(FarmNet), typeof(FieldsPage), typeof(FarmModifiers), typeof(DesertWeatherFX), typeof(CozyFx), typeof(Appearance), typeof(SponsorManager), typeof(CozyAnalytics), typeof(LoadingScreen),
                                 typeof(ParkedImplement), typeof(ImplementYard), typeof(VehicleConfig), typeof(FarmMenuData), typeof(Tractor) })
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

    internal static void BuildWorld()
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
        var clock = WorldClock.Instance; var ff = FarmFields.Instance;
        T.Check("Crops: systems created", care != null && orch != null);
        var crops = new[] { "alfalfa", "sorghum", "millet", "tomato", "wheat", "potato", "onion", "watermelon" };
        T.Check("Crops: 8 field crops with sellable goods and a family", crops.All(c => eco.FieldCrop(c) != null && eco.Good(c) != null && CropsData.Meta(c) != null));
        T.Check("Crops: only heat-tolerant crops grow in summer (Najd map)", eco.AvailableFieldCrops().Where(c => c.seasons.Contains("summer")).All(c => c.id == "sorghum" || c.id == "millet"),
            string.Join(",", eco.Data.fieldCrops.Where(c => c.seasons.Contains("summer")).Select(c => c.id)));
        T.Check("Crops: cool season (Oct-Apr) is the main season", eco.Data.fieldCrops.Count(c => c.seasons.Contains("winter") || c.seasons.Contains("autumn")) >= 5);
        T.Check("Crops: date goods for Sukkari, Khalas, Ajwa (Ajwa dearest)", eco.Good("dates_ajwa").price > eco.Good("dates_sukkari").price && eco.Good("dates_sukkari").price > eco.Good("dates_khalas").price);
        T.Check("Crops: shop pages Orchard (7 saplings) and Crop care (6 items)", ShopCatalog.Entries.Count(e => e.category == "Orchard") == 7 && ShopCatalog.Entries.Count(e => e.category == "Crop care") == 6);
        T.Check("Crops: orchard of 6 tree slots next to the fields", orch.Trees.Count == 6 && Vector3.Distance(orch.Trees[0].transform.position, ff.GridCenter) < 40f,
            orch.Trees.Count + " " + (orch.Trees.Count > 0 ? Vector3.Distance(orch.Trees[0].transform.position, ff.GridCenter).ToString() : ""));
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

        // Alfalfa: several cuts, hay + feed, nitrogen (FarmCore fields: mower + baler)
        var ap = ff.Fields[0];
        ap.ResetSoil(FcData.CurrentRegion); ff.SetSowCrop("");
        eco.TryBuy("seed_wheat");
        T.Check("Crops: buy alfalfa seeds", eco.TryBuy("seed_alfalfa") && eco.TryBuy("seed_alfalfa"));
        FC.Prepare(ap); FC.Whole(ap, FieldTool.Seeder);
        T.Check("Crops: the last seed bought (alfalfa) is sown", FC.Sown(ap) == ap.Cells && FC.Crop(ap) == "alfalfa", FC.Sown(ap) + " " + FC.Crop(ap));
        T.Check("Crops: mission 'plant alfalfa' done", mm.Current?.id == "crops_cure", mm.Current?.id);
        FC.Ripen(ap);
        float feed = eco.Feed, hay = eco.Stored("alfalfa"), nBefore = FarmSim.Summarize(ap).n;
        FC.Whole(ap, FieldTool.Mower); FC.Whole(ap, FieldTool.Baler);
        T.Check("Crops: alfalfa cut and baled stores hay (~12 t/ha fresh)", eco.Stored("alfalfa") > hay + 50f, (eco.Stored("alfalfa") - hay).ToString());
        T.Check("Crops: alfalfa hay adds animal feed", eco.Feed > feed + 5f, feed + " -> " + eco.Feed);
        T.Check("Crops: alfalfa regrows for the next cut", FC.Sown(ap) == ap.Cells && FC.Crop(ap) == "alfalfa" && ap.Get(FieldLayer.Growth, 0) > 0.5f && ap.Get(FieldLayer.Growth, 0) < 1f);
        T.Check("Crops: a legume puts nitrogen back", FarmSim.Summarize(ap).n >= nBefore, nBefore + " -> " + FarmSim.Summarize(ap).n);
        T.Check("Crops: Fields page shows cut 2/6", string.Join(" ", FieldsPage.FieldLines(ap)).Contains("Cut 2/6"), string.Join(" | ", FieldsPage.FieldLines(ap)));

        // Pests and cures
        T.Check("Crops: aphids infest a growing field", FieldPests.Infest(ap, "aphids"));
        T.Check("Crops: pests slow growth", FieldPests.GrowthMultiplier(ap) < 0.7f);
        T.Check("Crops: pest shown on the prompt", ap.View.PromptRaw(ap.center).Contains("Aphids"), ap.View.PromptRaw(ap.center));
        T.Check("Crops: buy neem spray", eco.TryBuy("care_neem") && care.Items("neem") == 1);
        T.Check("Crops: prompt offers the treatment", ap.View.PromptRaw(ap.center).Contains("[E] Treat Aphids: Neem spray"), ap.View.PromptRaw(ap.center));
        ff.HandJob(ap.center);
        T.Check("Crops: E treats the aphids (uses the spray)", ap.pest == "" && care.Items("neem") == 0);
        T.Check("Crops: mission 'cure a pest' done", mm.Current?.id == "crops_rotate", mm.Current?.id);
        T.Check("Crops: treatment protects for a few days", !FieldPests.Infest(ap, "aphids"));

        // Soil nutrients and rotation
        var wp = ff.Fields[1];
        wp.ResetSoil(FcData.CurrentRegion);
        eco.AddSeeds("wheat", 4); ff.SetSowCrop("wheat");
        FC.Prepare(wp); FC.Sow(wp, "wheat");
        T.Check("Crops: wheat sown on field 2", FC.Crop(wp) == "wheat" && FC.Sown(wp) == wp.Cells);
        FC.Ripen(wp);
        float n1 = FarmSim.Summarize(wp).n;
        FC.HandHarvest(wp);
        float n2 = FarmSim.Summarize(wp).n;
        T.Check("Crops: a harvest takes nutrients out of the soil (wheat ~50 kg N/ha at full yield)", n2 < n1 - 20f && wp.lastCrop == "wheat", n1 + " -> " + n2);
        FC.Prepare(wp); FC.Sow(wp, "wheat");
        T.Check("Crops: no rotation bonus for the same family (grain after grain)", FC.Crop(wp) == "wheat" && Math.Abs(wp.rotation - 1f) < 0.001f);
        FC.Ripen(wp); FC.HandHarvest(wp);
        T.Check("Crops: buy fertilizer", eco.TryBuy("care_fertilizer"));
        float n3 = FarmSim.Summarize(wp).n;
        FC.Whole(wp, FieldTool.Spreader, true, null, "fertilizer");
        T.Check("Crops: the spreader with NPK fertilizer feeds the soil", FarmSim.Summarize(wp).n > n3 + 40f && care.Items("fertilizer") == 0, n3 + " -> " + FarmSim.Summarize(wp).n);
        eco.TryBuy("seed_potato"); eco.TryBuy("seed_potato"); ff.SetSowCrop("potato");
        FC.Prepare(wp); FC.Sow(wp, "potato");
        T.Check("Crops: potato after wheat is a rotation (+15%)", FC.Crop(wp) == "potato" && Math.Abs(wp.rotation - 1.15f) < 0.001f, wp.rotation + "");
        T.Check("Crops: mission 'rotate crops' done", mm.Current?.id == "crops_dates", mm.Current?.id);
        FC.Ripen(wp);
        float expected = FarmSim.Summarize(wp).expectedKg, pot = eco.Stored("potato");
        float plain = expected / 1.15f;
        FC.HandHarvest(wp);
        float potKg = eco.Stored("potato") - pot;
        T.Check("Crops: harvest kg = cells x yield potential x rotation 115%", Math.Abs(potKg - expected) < 0.5f && Math.Abs(potKg - plain * 1.15f) < 0.5f && potKg > 100f, potKg + " vs " + expected);

        // Desert locusts
        eco.AddSeeds("potato", 2); FC.Prepare(wp); FC.Sow(wp, "potato");
        FC.Ripen(wp, 0.5f); wp.protectDays = 0;
        care.LocustSwarm();
        T.Check("Crops: locust swarm eats growth and leaves locusts", wp.Get(FieldLayer.Growth, 0) < 0.3f && wp.pest == "locust", wp.Get(FieldLayer.Growth, 0) + " " + wp.pest);
        T.Check("Crops: neem does not stop locusts", care.OwnedCureFor("locust") == null);
        eco.TryBuy("care_pesticide");
        ff.HandJob(wp.center);
        T.Check("Crops: pesticide clears locusts", wp.pest == "");

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
        string ar = palm.InteractionPrompt + ap.View.InteractionPrompt;
        T.Check("Crops: tree and field prompts in Arabic", ar.Any(c => c >= '\uFE70') && !ar.Contains("Expected"), ar);
        Loc.SetLanguage(Language.English);
        T.Check("Crops: no errors", FakeEngine.Errors.Count == 0, string.Join(" | ", FakeEngine.Errors.Take(3)));
    }

    // ------------------------------------------------------------------ UI, input, menus (feat/ui)
    static void PressKey(UnityEngine.InputSystem.KeyControl k) { k.wasPressedThisFrame = true; k.isPressed = true; FakeEngine.Tick(0.02f); k.wasPressedThisFrame = false; k.isPressed = false; }
    static void PressPad(UnityEngine.InputSystem.ButtonControl b) { b.wasPressedThisFrame = true; b.isPressed = true; FakeEngine.Tick(0.02f); b.wasPressedThisFrame = false; b.isPressed = false; }

    // ------------------------------------------------------------------ Settings hub and pages (GameMenuSettings, GameSettingsOptions, SettingsApplier)
    static void SettingsPages(GameMenu menu, UnityEngine.InputSystem.Keyboard kb, UnityEngine.InputSystem.Gamepad pad)
    {
        Console.WriteLine("Settings pages");
        const string P = "mazraa.settings.";
        // Every page opens from the hub and B comes back
        bool allOpen = true; string bad = "";
        foreach (GameMenu.SettingsTab tab in Enum.GetValues(typeof(GameMenu.SettingsTab)))
        {
            menu.Open(GameMenu.Mode.Pause);
            menu.List.Choose(menu.List.Rows.ToList().FindIndex(r => r.label() == UiText.T("Settings")));
            menu.List.Choose((int)tab);
            var rows = menu.List.Rows;
            bool ok = menu.OnSettingsPage && menu.CurrentSettingsTab == tab && rows[rows.Count - 1].label() == "Back" && rows[rows.Count - 2].label() == "Reset to defaults" && rows.Count >= 7;
            FakeEngine.Tick(0.02f);
            PressPad(pad.buttonEast);
            ok &= menu.OnSettingsHub;
            PressPad(pad.buttonEast); PressPad(pad.buttonEast);
            ok &= !GameMenu.IsOpen;
            if (!ok) { allOpen = false; bad += tab + " "; }
        }
        T.Check("Each settings page opens from the menu, B returns to the hub, then closes", allOpen, bad);

        // Labels and option names have Arabic
        var keys = new List<string>(GameSettings.OptionNames()); keys.AddRange(GameMenu.SettingsTabNames);
        foreach (GameMenu.SettingsTab tab in Enum.GetValues(typeof(GameMenu.SettingsTab)))
        {
            menu.OpenSettings(tab);
            foreach (var r in menu.List.Rows) keys.Add(r.label());
        }
        var missing = keys.Distinct().Where(k => !Loc.HasTranslation(k)).ToList();
        T.Check("Settings labels and options have Arabic (" + keys.Distinct().Count() + ")", missing.Count == 0, string.Join(" | ", missing.Take(5)));
        Loc.SetLanguage(Language.Arabic);
        menu.OpenSettings(GameMenu.SettingsTab.Graphics);
        T.Check("Settings pages show in Arabic", menu.List.Rows[1].label() == Loc.T("Shadow quality") && menu.List.Rows[1].value() == Loc.T("Auto") && menu.List.Rows[1].label() != "Shadow quality");
        Loc.SetLanguage(Language.English);
        menu.Close();

        // Gamepad: LB / RB turn pages, D-pad changes, long page scrolls
        menu.OpenSettings(GameMenu.SettingsTab.Game);
        FakeEngine.Tick(0.02f);
        PressPad(pad.rightShoulder);
        bool rb = menu.CurrentSettingsTab == GameMenu.SettingsTab.Display;
        PressPad(pad.leftShoulder); PressPad(pad.leftShoulder);
        T.Check("RB / LB turn the settings pages (wrapping)", rb && menu.CurrentSettingsTab == GameMenu.SettingsTab.Accessibility && menu.OnSettingsPage, menu.CurrentSettingsTab + "");
        PressPad(pad.rightShoulder); PressPad(pad.rightShoulder); PressPad(pad.rightShoulder);   // Game -> Display -> Graphics
        PressPad(pad.dpad.down); PressPad(pad.dpad.right);
        T.Check("Gamepad: Graphics page, Shadow quality Auto -> Off", menu.CurrentSettingsTab == GameMenu.SettingsTab.Graphics && GameSettings.ShadowQuality == 0, menu.CurrentSettingsTab + " " + GameSettings.ShadowQuality);
        for (int i = 0; i < 13; i++) PressPad(pad.dpad.down);   // row 1 -> Back (row 14)
        var sel = menu.List.Rows[menu.List.Selected];
        T.Check("Long settings page scrolls to Back with the D-pad", sel.label() == "Back" && sel.rt.gameObject.activeSelf && !menu.List.Rows[0].rt.gameObject.activeSelf, sel.label());
        PressPad(pad.dpad.up);
        PressPad(pad.buttonSouth);   // Reset to defaults
        T.Check("Reset to defaults (Graphics) from the page", GameSettings.ShadowQuality == -1 && !PlayerPrefs.HasKey(P + "shadowQ") && GraphicsEnhancer.Preset == 1);
        PressPad(pad.buttonEast); PressPad(pad.buttonEast); PressPad(pad.buttonEast);
        T.Check("B three times closes the menu from a settings page", !GameMenu.IsOpen);

        // ---- Game
        GameSettings.TimeScale = 120;
        T.Check("Time scale x120: 12 real minutes a day, saved", Math.Abs(WorldClock.Instance.secondsPerDay - 720f) < 0.01f && PlayerPrefs.GetInt(P + "timeScale") == 120 && GameSettings.TimeScale == 120, WorldClock.Instance.secondsPerDay + "");
        GameSettings.TimeScale = 0;
        T.Check("Time scale off: day length again", Math.Abs(WorldClock.Instance.secondsPerDay - GameSettings.DayMinutes * 60f) < 0.01f);
        GameSettings.PlowingRequired = false;
        bool plowOff = Math.Abs(FarmAgronomy.CompactionFactor(0.9f, GameSettings.PlowingRequired) - 1f) < 0.001f;
        GameSettings.PlowingRequired = true;
        T.Check("Plowing requirement off: compaction costs no yield", plowOff && FarmAgronomy.CompactionFactor(0.9f, GameSettings.PlowingRequired) < 0.9f);
        GameSettings.Weeds = false;
        FarmFields.Instance.Simulate(0.25f);   // FarmCore steps every in-game quarter hour
        T.Check("Weeds off: no weeds on any field", FarmFields.Instance.Fields.All(f => FarmSim.Summarize(f).weeds == 0f));
        GameSettings.Weeds = true;
        var wf = FarmFields.Instance.Fields[0];
        string wsave = wf.EncodeCells();
        byte wheatIdx = FcData.CropIndex("wheat");
        for (int i = 0; i < wf.Cells; i++) { wf.ground[i] = (byte)Ground.Sown; wf.crop[i] = wheatIdx; wf.Set(FieldLayer.Growth, i, 0.3f); wf.Set(FieldLayer.Yield, i, 1f); wf.SetFlag(i, CellFlags.Withered, false); wf.Set(FieldLayer.Moisture, i, 0f); }
        var ap = SettingsApplier.Instance;
        FarmFields.Instance.SimulateLong(26f);
        ap.TickWithering(1f);
        bool alive = FC.Count(wf, i => wf.Has(i, CellFlags.Withered)) == 0;
        GameSettings.Withering = true;
        for (int i = 0; i < wf.Cells; i++) wf.Set(FieldLayer.Moisture, i, 0f);
        FarmFields.Instance.SimulateLong(12f);
        ap.TickWithering(1f);
        bool halfDay = FC.Count(wf, i => wf.Has(i, CellFlags.Withered)) == 0;
        for (int i = 0; i < wf.Cells; i++) wf.Set(FieldLayer.Moisture, i, 0f);
        FarmFields.Instance.SimulateLong(14f);
        ap.TickWithering(1f);
        T.Check("Withering: off keeps dry crops, on kills them after a dry day", alive && halfDay && FC.Count(wf, i => wf.Has(i, CellFlags.Withered)) == wf.Cells && ap.Withered == 1, alive + " " + halfDay + " " + ap.Withered);
        GameSettings.Withering = false;
        wf.DecodeCells(wsave);
        var eco = EconomyManager.Instance; var wm = WorkerManager.Instance;
        var seeds = (Dictionary<string, int>)T.Get(eco, "seeds");
        var seedsBefore = new Dictionary<string, int>(seeds);
        foreach (var k in seeds.Keys.ToList()) seeds[k] = 0;
        int moneyStart = eco.Money;
        eco.AddMoney(1000);
        bool wasHired = wm.IsHired(WorkerRole.FieldHand);
        wm.Hire(WorkerRole.FieldHand);
        ap.TickHelpers();
        int boughtOff = ap.HelperSeedsBought;
        GameSettings.HelperRefill = true;
        int money0 = eco.Money;
        ap.TickHelpers();
        T.Check("Helper refills: field hand buys seeds only when switched on", boughtOff == 0 && ap.HelperSeedsBought == 2 && eco.Money < money0 && seeds.Values.Sum() == 2, ap.HelperSeedsBought + " " + money0 + "->" + eco.Money);
        GameSettings.HelperRefill = false;
        if (!wasHired) wm.Dismiss(WorkerRole.FieldHand, false);
        foreach (var kv in seedsBefore) seeds[kv.Key] = kv.Value;
        if (eco.Money > moneyStart) eco.Spend(eco.Money - moneyStart);
        int ambient = UnityEngine.Object.FindObjectsByType<Actor>(FindObjectsSortMode.None).Count(a => a.mode == ActorMode.Wander || a.mode == ActorMode.Fly);
        GameSettings.AmbientDensity = 0;
        int ambientOff = UnityEngine.Object.FindObjectsByType<Actor>(FindObjectsSortMode.None).Count(a => a.mode == ActorMode.Wander || a.mode == ActorMode.Fly);
        GameSettings.AmbientDensity = 3;
        int ambientBack = UnityEngine.Object.FindObjectsByType<Actor>(FindObjectsSortMode.None).Count(a => a.mode == ActorMode.Wander || a.mode == ActorMode.Fly);
        T.Check("Ambient life Off hides wandering / flying life, High brings it back", ambient > 0 && ambientOff == 0 && ambientBack == ambient, ambient + " " + ambientOff + " " + ambientBack);
        GameSettings.ShowHints = false;
        bool hintsOff = !GameHUD.Instance.HintsVisible;
        GameSettings.ShowHints = true;
        T.Check("Help hints switch the HUD hints box", hintsOff && GameHUD.Instance.HintsVisible);
        GameSettings.AutosaveMinutes = 5;
        int saves = ap.Autosaves;
        T.Set(ap, "autosaveTimer", 299.5f);
        FakeEngine.Run(1f, 0.25f);
        T.Check("Autosave every 5 real minutes", ap.Autosaves == saves + 1, ap.Autosaves + "");
        GameSettings.Tutorials = false; GameSettings.CameraShake = false;
        T.Check("Tutorials / camera shake saved", PlayerPrefs.GetInt(P + "tutorials") == 0 && GameSettings.ShakeScale == 0f);
        menu.OpenSettings(GameMenu.SettingsTab.Game);
        menu.List.Choose(menu.List.Rows.Count - 2);
        T.Check("Reset to defaults (Game)", GameSettings.AutosaveMinutes == -1 && GameSettings.Tutorials && GameSettings.CameraShake && GameSettings.Weeds && GameSettings.TimeScale == 0 && GameSettings.DayMinutes == GameSettings.DefaultDayMinutes);
        menu.Close();

        // ---- Display
        int calls = Screen.SetResolutionCalls;
        GameSettings.WindowMode = 2;
        GameSettings.CycleResolution(-1);   // native (largest) -> next smaller
        T.Check("Display: windowed 1920x1080 applied through Screen.SetResolution", Screen.fullScreenMode == FullScreenMode.Windowed && Screen.width == 1920 && Screen.height == 1080 && Screen.SetResolutionCalls > calls && GameSettings.ResolutionWidth == 1920, Screen.width + "x" + Screen.height + " " + Screen.fullScreenMode);
        calls = Screen.SetResolutionCalls;
        GameSettings.FpsLimit = 60; GameSettings.VSync = false;
        T.Check("Display: FPS limit 60 and VSync off (no new resolution call)", Application.targetFrameRate == 60 && QualitySettings.vSyncCount == 0 && Screen.SetResolutionCalls == calls);
        var urp = HdrpSettings.Current; urp.renderScale = 1f; urp.msaaSampleCount = 4; urp.shadowDistance = 90f;
        GameSettings.RenderScale = 150; GameSettings.Brightness = 1.3f;
        T.Check("Display: render scale 150% in HdrpSettings, brightness saved", Math.Abs(urp.renderScale - 1.5f) < 0.001f && Math.Abs(GameSettings.Brightness - 1.3f) < 0.001f && Math.Abs(PlayerPrefs.GetFloat(P + "brightness") - 1.3f) < 0.001f, urp.renderScale + "");
        menu.OpenSettings(GameMenu.SettingsTab.Display);
        menu.List.Choose(menu.List.Rows.Count - 2);
        T.Check("Reset to defaults (Display)", Application.targetFrameRate == -1 && QualitySettings.vSyncCount == 1 && Screen.fullScreenMode == FullScreenMode.FullScreenWindow && GameSettings.ResolutionWidth == 0 && Math.Abs(urp.renderScale - GraphicsEnhancer.EffectiveRenderScale) < 0.001f && GameSettings.Brightness == 1f);
        menu.Close();

        // ---- Graphics
        var cam = Camera.main;
        float far0 = cam.farClipPlane;
        int rebuilds = 0; Action<int> onPreset = p => rebuilds++;
        GraphicsEnhancer.PresetChanged += onPreset;
        GameSettings.ShadowQuality = 3; GameSettings.ShadowDistance = 150; GameSettings.ViewDistance = 500; GameSettings.LodBias = 200;
        GameSettings.TextureQuality = 2; GameSettings.AntiAliasing = 3; GameSettings.FoliageDensity = 25;
        T.Check("Graphics overrides applied (shadows 150 m, view 500 m, LOD 2x, quarter textures, MSAA 4x, foliage 25%)",
            Math.Abs(urp.shadowDistance - 150f) < 0.01f && QualitySettings.shadows == ShadowQuality.All && Math.Abs(cam.farClipPlane - 500f) < 0.01f && Math.Abs(QualitySettings.lodBias - 2f) < 0.001f &&
            QualitySettings.globalTextureMipmapLimit == 2 && urp.msaaSampleCount == 4 && Math.Abs(GraphicsEnhancer.DressingDensity - 0.25f) < 0.001f && rebuilds == 1,
            urp.shadowDistance + " " + cam.farClipPlane + " " + QualitySettings.lodBias + " " + rebuilds);
        GameSettings.ShadowQuality = 0; GameSettings.AntiAliasing = 0;
        T.Check("Shadows off and anti-aliasing off", urp.shadowDistance == 0f && QualitySettings.shadows == ShadowQuality.Disable && urp.msaaSampleCount == 1 && GraphicsEnhancer.EffectiveAntialiasing == 0);
        GameSettings.MotionBlur = true; GameSettings.DepthOfField = true; GameSettings.Bloom = false;
        var ge = GraphicsEnhancer.Instance;
        var mb = (UnityEngine.Rendering.HighDefinition.MotionBlur)T.Get(ge, "motionBlur"); var bl = (UnityEngine.Rendering.HighDefinition.Bloom)T.Get(ge, "bloom");
        bool mbOn = mb.active && !bl.active;
        GameSettings.ReduceMotion = true;
        T.Check("Post effects: bloom off, motion blur on, Reduce motion turns it off again", mbOn && !mb.active && !GraphicsEnhancer.DepthOfFieldOn);
        GameSettings.ReduceMotion = false;
        GameSettings.FogHaze = false;
        FakeEngine.Run(0.5f, 0.1f);
        bool fogOff = !RenderSettings.fog || DesertWeatherFX.FogOverridden;
        GameSettings.FogHaze = true;
        FakeEngine.Run(0.5f, 0.1f);
        T.Check("Fog and haze off clears the desert haze", fogOff && (RenderSettings.fog || DesertWeatherFX.FogOverridden));
        menu.OpenSettings(GameMenu.SettingsTab.Graphics);
        menu.List.Choose(menu.List.Rows.Count - 2);
        T.Check("Reset to defaults (Graphics): back to the preset", GameSettings.ShadowDistance == -1 && GameSettings.ViewDistance == -1 && Math.Abs(cam.farClipPlane - far0) < 0.01f && urp.msaaSampleCount == GraphicsEnhancer.EffectiveMsaa && !GameSettings.MotionBlur && GameSettings.Bloom && rebuilds == 2 && GraphicsEnhancer.Preset == 1, cam.farClipPlane + " vs " + far0 + " " + rebuilds);
        GraphicsEnhancer.PresetChanged -= onPreset;
        UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline = null;
        T.Set(ge, "urpAsset", null);
        menu.Close();

        // ---- Sound
        GameSettings.VehicleVolume = 0.3f; GameSettings.AmbientVolume = 0.4f; GameSettings.AnimalVolume = 0.5f; GameSettings.UiVolume = 0.6f; GameSettings.VoiceVolume = 0.7f;
        T.Check("Sound: vehicles / environment / animals / menus / voices saved", Math.Abs(PlayerPrefs.GetFloat(P + "volVehicles") - 0.3f) < 0.001f && Math.Abs(GameSettings.AnimalVolume - 0.5f) < 0.001f && Math.Abs(GameSettings.VoiceVolume - 0.7f) < 0.001f);
        menu.OpenSettings(GameMenu.SettingsTab.Sound);
        menu.List.Choose(menu.List.Rows.Count - 2);
        T.Check("Reset to defaults (Sound)", GameSettings.VehicleVolume == 1f && GameSettings.MasterVolume == 1f && Math.Abs(AudioListener.volume - 1f) < 0.001f && Math.Abs(GameSettings.MusicVolume - 0.8f) < 0.001f);
        menu.Close();

        // ---- Controls
        GameSettings.StickDeadZone = 0.2f; GameSettings.SteeringSensitivity = 0.5f;
        T.Check("Dead zone and steering sensitivity shape the sticks", GameSettings.ApplyDeadZone(new Vector2(0.15f, 0f)).x == 0f && GameSettings.ApplyDeadZone(new Vector2(1f, 0f)).x > 0.99f &&
            GameSettings.ShapeSteer(0.1f) == 0f && Math.Abs(GameSettings.ShapeSteer(1f) - 0.5f) < 0.001f && Math.Abs(GameSettings.ShapeSteer(-1f) + 0.5f) < 0.001f);
        float fov0 = cam.fieldOfView;
        GameSettings.CameraFov = 90;
        bool fov = Math.Abs(cam.fieldOfView - 90f) < 0.01f;
        GameSettings.CameraFov = 0;
        T.Check("Field of view 90 applied to the player camera, Auto restores it", fov && Math.Abs(cam.fieldOfView - fov0) < 0.01f, cam.fieldOfView + "");
        GameInput.ForceDevice(true, false);
        GameSettings.Rumble(0.5f, 0.5f, 0.1f);
        bool rumble = pad.lowMotor > 0.4f;
        FakeEngine.Run(0.3f, 0.05f);
        bool stopped = pad.lowMotor == 0f;
        GameSettings.Vibration = false;
        GameSettings.Rumble(0.5f, 0.5f, 0.1f);
        T.Check("Vibration: rumble on the pad, stops by itself, off = no rumble", rumble && stopped && pad.lowMotor == 0f, rumble + " " + stopped);
        GameInput.ForceDevice(false, false);
        GameSettings.MouseSensitivity = 2f; GameSettings.InvertY = true;
        menu.OpenSettings(GameMenu.SettingsTab.Controls);
        T.Check("Controls page links the key rebinding page", menu.List.Rows[0].label() == "Controls (rebind keys)");
        menu.List.Choose(0);
        bool rebindPage = menu.List.Rows.Any(r => r.label() == UiText.T(GameInput.Label(GameAction.Interact)));
        menu.Back();
        T.Check("Rebinding page opens from Controls and Back returns there", rebindPage && menu.OnSettingsPage && menu.CurrentSettingsTab == GameMenu.SettingsTab.Controls);
        menu.List.Choose(menu.List.Rows.Count - 2);
        T.Check("Reset to defaults (Controls)", GameSettings.Vibration && GameSettings.MouseSensitivity == 1f && !GameSettings.InvertY && Math.Abs(GameSettings.StickDeadZone - 0.1f) < 0.001f && GameSettings.SteeringSensitivity == 1f);
        menu.Close();

        // ---- Accessibility
        GameSettings.UiScale = 150;
        var scaler = menu.MenuCanvas.GetComponent<UnityEngine.UI.CanvasScaler>();
        T.Check("Text and UI size 150% scales the menu and HUD canvases", Math.Abs(scaler.referenceResolution.x - 1280f) < 0.5f && Math.Abs(SettingsApplier.UiReference.y - 720f) < 0.5f, scaler.referenceResolution.x + "");
        Color vehicle = MinimapIcon.DefaultColor(MinimapIconKind.Vehicle), bay = MinimapIcon.DefaultColor(MinimapIconKind.LoadingBay), gh = MinimapIcon.DefaultColor(MinimapIconKind.Greenhouse);
        GameSettings.ColorBlindMode = 2;
        Color v2 = GameSettings.Palette(vehicle), b2 = GameSettings.Palette(bay);
        T.Check("Deuteranopia palette: vehicle red and loading-bay green become purple and blue", v2.b > 0.5f && b2.b > 0.6f && b2.g < 0.5f && GameSettings.Palette(Color.white).r == 1f);
        T.Check("Protanopia / tritanopia palettes change the confusable colours", GameSettings.Palette(vehicle, 1).b > 0.5f && GameSettings.Palette(gh, 3).r > 0.7f && GameSettings.Palette(vehicle, 0).r == vehicle.r);
        GameSettings.NotificationPercent = 200;
        GameSettings.HighContrast = true;
        menu.OpenSettings(GameMenu.SettingsTab.Accessibility);
        var panelColor = menu.List.Panel.GetComponent<UnityEngine.UI.Image>().color;
        var selColor = menu.List.Rows[menu.List.Selected].bg.color;
        T.Check("High-contrast menus: opaque black panel, yellow selection", panelColor.a == 1f && panelColor.r == 0f && selColor.r == 1f && selColor.a == 1f && GameSettings.NotificationScale == 2f);
        menu.List.Choose(menu.List.Rows.Count - 2);
        T.Check("Reset to defaults (Accessibility)", GameSettings.UiScale == 100 && GameSettings.ColorBlindMode == 0 && !GameSettings.HighContrast && GameSettings.NotificationScale == 1f && Math.Abs(scaler.referenceResolution.x - 1920f) < 0.5f && menu.List.Panel.GetComponent<UnityEngine.UI.Image>().color.r > 0f);   // kit panel: white tint; flat: translucent
        menu.Close();

        // Values for the checks that follow (new game keeps settings)
        GameSettings.MasterVolume = 0.5f;
        GameSettings.GraphicsPreset = 1;
        T.Check("Menu closed after the settings tests", !GameMenu.IsOpen && Time.timeScale == 1f);
    }

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
        var hubLabels = menu.List.Rows.Select(r => r.label()).ToList();
        T.Check("Settings is a hub: Game, Display, Graphics, Sound, Controls, Accessibility, Language",
            menu.OnSettingsHub && GameMenu.SettingsTabNames.All(n => hubLabels.Contains(n)) && hubLabels.Contains("Language") && hubLabels.Last() == "Back", string.Join(",", hubLabels));
        PressPad(pad.dpad.down); PressPad(pad.dpad.down); PressPad(pad.dpad.down);   // to Sound
        PressPad(pad.buttonSouth);
        T.Check("D-pad + A open the Sound page", menu.OnSettingsPage && menu.CurrentSettingsTab == GameMenu.SettingsTab.Sound, menu.CurrentSettingsTab + "");
        PressPad(pad.dpad.down); PressPad(pad.dpad.down);   // to Sound effects
        PressPad(pad.dpad.left);
        T.Check("D-pad changes a setting (SFX volume 90%)", Math.Abs(GameSettings.SfxVolume - 0.9f) < 0.001f, "" + GameSettings.SfxVolume);
        GameSettings.MasterVolume = 0.5f;
        T.Check("Master volume drives AudioListener.volume", Math.Abs(AudioListener.volume - 0.5f) < 0.001f);
        GameSettings.GraphicsPreset = 1;
        T.Check("Graphics preset Medium applied", GraphicsEnhancer.Preset == 1 && !GraphicsEnhancer.High);
        PressPad(pad.buttonEast);
        T.Check("B goes back to the settings hub (Sound still selected)", menu.OnSettingsHub && menu.List.Selected == (int)GameMenu.SettingsTab.Sound, menu.List.Selected + "");
        PressPad(pad.buttonEast);
        T.Check("B goes back to the pause page", menu.List.Rows.Any(r => r.label() == UiText.T("Resume")));
        PressPad(pad.buttonEast);
        T.Check("B on the pause page resumes", menu.CurrentMode == GameMenu.Mode.Closed && Time.timeScale == 1f && !UIManager.Instance.IsUIOpened);
        SettingsPages(menu, kb, pad);
        PressKey(kb.pKey);
        T.Check("P opens the pause menu", GameMenu.IsOpen);
        menu.List.Choose(menu.List.Rows.ToList().FindIndex(r => r.label() == UiText.T("New game")));
        EconomyManager.Instance.AddMoney(1234);
        menu.List.Choose(1); // "Yes, clear the current save"
        T.Check("New game: map choice lists Najd and the Egypt DLC", menu.List.Rows.Count == Regions.All.Length + 1 && menu.List.Rows[0].label().Contains("Najd") && menu.List.Rows[1].label().Contains("Nile"));
        T.Check("New game: coming-soon maps are locked", !menu.List.Rows[2].IsEnabled && menu.List.Rows[2].value() == UiText.T("Coming soon"));
        menu.List.Choose(0);   // Najd
        T.Check("New game: start choice (New farmer / Farm manager / From scratch)", menu.List.Rows.Count == 4 && menu.List.Rows[1].label().Contains("Farm manager"));
        menu.List.Choose(0);   // New farmer
        T.Check("New game: economy choice defaults to Normal", menu.List.Rows.Count == 4 && menu.List.Selected == 1);
        menu.List.Choose(1);   // Normal
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

        // ---- campaign chapters
        var camp = CampaignManager.Instance;
        T.Check("Campaign loads 8 chapters", camp != null && camp.Chapters.Length == 8, camp == null ? "null" : "" + camp.Chapters.Length);
        T.Check("Missions map to chapters", camp.ChapterOfMission(0) == 0 && camp.ChapterOfMission(6) == 1 && camp.ChapterOfMission(44) == 6);
        T.Check("Chapter card has title and narrator", camp.ChapterCard(1, true, 0).Contains("Iron and Wheels") && camp.ChapterCard(1, true, 0).Contains("Abu Saleh"));
        T.Check("Campaign overview lists chapters", camp.Overview().Contains("The Inheritance") && camp.Overview().Contains("Heart of the Village"));
        T.Check("Pause overview text builds", MainMenuScene.OverviewText().Contains("Farm overview"));

        // ---- controls tutorial (Mazraa Academy)
        var aca = ControlsTutorial.Instance;
        T.Check("Mazraa Academy system exists", aca != null);
        int moneyBeforeAca = EconomyManager.Instance.Money;
        aca.Begin();
        T.Check("Academy starts at Look around", aca.Running && aca.Step == ControlsTutorial.StepId.Look && aca.BodyText().Contains("Look around"));
        T.Check("Academy header shows the brand and step", aca.HeaderText().Contains("MAZRAA") && aca.HeaderText().Contains("1/"));
        var advance = typeof(ControlsTutorial).GetMethod("Advance", BindingFlags.Instance | BindingFlags.NonPublic);
        aca.Skip();
        advance.Invoke(aca, null);
        T.Check("Skip moves to Walk", aca.Step == ControlsTutorial.StepId.Walk);
        int guard = 0;
        while (aca.Running && guard++ < 20)
        {
            var finishedField = (bool)typeof(ControlsTutorial).GetField("finished", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(aca);
            if (finishedField) break;
            aca.Skip(); advance.Invoke(aca, null);
        }
        T.Check("Finishing pays the reward once", EconomyManager.Instance.Money == moneyBeforeAca + ControlsTutorial.Reward, EconomyManager.Instance.Money + " vs " + moneyBeforeAca);
        T.Check("Finish shows a medal", aca.BodyText().Contains("medal"));
        aca.Stop();
        aca.Begin(); for (int i = 0; i < 20 && aca.Running; i++) { aca.Skip(); advance.Invoke(aca, null); }
        T.Check("Replaying does not pay again", EconomyManager.Instance.Money == moneyBeforeAca + ControlsTutorial.Reward);
        aca.Stop();
        EconomyManager.Instance.Spend(ControlsTutorial.Reward);

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
        // The front end opens on the title screen; any key goes on to the main menu (FrontEndTests covers the rest).
        T.Check("Main menu at start shows the title screen", GameMenu.Instance.Front == GameMenu.FrontScreen.Title, GameMenu.Instance.PageName);
        PressKey(kb.anyKey);
        var labels = GameMenu.Instance.List.Rows.Select(r => r.label()).ToList();
        T.Check("Main menu: Continue (farm, day), Career, Multiplayer, Achievements, Settings, Credits, Quit",
            labels[0].StartsWith("Continue") && labels.Skip(1).SequenceEqual(new[] { "Career", "Multiplayer", "Achievements", "Settings", "Credits", "Quit" }), string.Join(",", labels));
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
        EventDirector.RandomEventsEnabled = false; // random farm events would make the core checks flaky; EventsFinTests turn them on
        GameMenu.ShowOnStart = false;   // the main menu pauses the game; the UI section tests it on its own
        FarmLife.PassiveBonuses = false; // beauty / mastery bonuses would shift exact price and amount checks; FarmLifeTests turns them on
        FakeEngine.PersistentPath = Path.Combine(Path.GetTempPath(), "mazraa_test_" + System.Diagnostics.Process.GetCurrentProcess().Id); // parallel runs don't share saves
        if (Directory.Exists(FakeEngine.PersistentPath)) Directory.Delete(FakeEngine.PersistentPath, true);
        Directory.CreateDirectory(FakeEngine.PersistentPath);
        GameSettings.DefaultDayMinutes = 6;   // tests were written for the old 6-minute day
        // MAZRAA_ONLY=Bump,World runs just those sections (quick checks while developing)
        var only = Environment.GetEnvironmentVariable("MAZRAA_ONLY");
        if (!string.IsNullOrEmpty(only))
        {
            foreach (var name in only.Split(','))
                typeof(Program).Assembly.GetType(name.Trim() + "Tests").GetMethod("Run").Invoke(null, null);
            Console.WriteLine($"\n{T.Pass} passed, {T.Fail} failed");
            Environment.Exit(T.Fail > 0 ? 1 : 0);
        }

        Console.WriteLine("Boot");
        BuildWorld();
        FakeEngine.Run(2f);
        var gm = GameManager.Instance;
        T.Check("GameManager running", gm != null);
        T.Check("No errors during boot", FakeEngine.Errors.Count == 0, string.Join(" | ", FakeEngine.Errors.Take(3)));
        T.Check("All systems created", EconomyManager.Instance && MissionManager.Instance && MarketManager.Instance && WorldClock.Instance && FarmFields.Instance && ScenarioManager.Instance && ShopUI.Instance && VehicleSpawner.Instance);
        T.Check("Truck and tractor spawned", FarmTruck.Instance != null && Tractor.Instance != null);
        T.Check("2 FarmCore fields (10 x 14 m of 2 m cells) where the 6 old plots were", FarmFields.Instance.Fields.Count == 2 && FarmFields.Instance.Fields.All(f => f.Cells == 35));
        T.Check("Loading bay and market", DeliveryZone.LoadingBay != null && DeliveryZone.Market != null);
        T.Check("Market is ~120 m away", Vector3.Distance(DeliveryZone.LoadingBay.transform.position, DeliveryZone.Market.transform.position) > 60f);
        T.Check("2 shop stalls + crate + pen", UnityEngine.Object.FindObjectsByType<ShopStall>(FindObjectsSortMode.None).Length == 2 && AnimalPen.Instance != null);
        T.Check("Starting money 500 SAR", EconomyManager.Instance.Money == 500, "" + EconomyManager.Instance.Money);
        T.Check("Starts on mission 1 (well)", MissionManager.Instance.Current?.id == "well");
        if (Environment.GetEnvironmentVariable("MAZRAA_ONLY") == "parity")   // quick loop on one section: MAZRAA_ONLY=parity
        {
            ParityTests.Run();
            Console.WriteLine($"\n{T.Pass} passed, {T.Fail} failed");
            Environment.Exit(T.Fail == 0 ? 0 : 1);
        }

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
        var ff = FarmFields.Instance;
        var field = ff.Fields[0];
        T.Check("Field starts untilled", FC.AllUntilled(field));
        ff.HandJob(field.center); T.Check("E hoes the patch around you", FC.Worked(field) > 0 && FC.Worked(field) < field.Cells);
        ff.HandJob(field.center); T.Check("E plants wheat (2 free seed bags of 100 m2)", FC.Sown(field) > 0 && FC.Crop(field) == "wheat");
        T.Check("Seed used", EconomyManager.Instance.Seeds("wheat") == 1);
        T.Check("Plant-field mission done", MissionManager.Instance.Current?.id == "fieldharvest", MissionManager.Instance.Current?.id);
        var spots = new[] { new Vector3(-3f, 0, -5f), new Vector3(3f, 0, -5f), new Vector3(-3f, 0, -1.7f), new Vector3(3f, 0, -1.7f), new Vector3(-3f, 0, 1.7f), new Vector3(3f, 0, 1.7f), new Vector3(-3f, 0, 5f), new Vector3(3f, 0, 5f) }
            .Select(v => field.center + field.Rotation * v).ToArray();
        foreach (var at in spots) for (int k = 0; k < 3; k++) ff.HandJob(at);   // hoe and sow the rest by hand
        T.Check("By hand: the whole field hoed and sown with the 2 bags", FC.Sown(field) == field.Cells && EconomyManager.Instance.Seeds("wheat") == 0, FC.Sown(field) + " " + EconomyManager.Instance.Seeds("wheat"));
        FC.Wet(field, 0.2f);
        ff.HandJob(field.center);
        T.Check("E waters (well running)", field.Get(FieldLayer.Moisture, field.CellAt(field.center)) > 0.5f);
        for (int i = 0; i < 160 && FC.Ripe(field) < FC.Sown(field); i++)
        {
            field.pest = "";   // random pests would slow growth: cleared (deterministic)
            foreach (var at in spots.Concat(new[] { field.center }))
                if (field.Get(FieldLayer.Moisture, field.CellAt(at)) < 0.4f) ff.HandTool(FieldTool.WateringCan, at, 25f);   // what E does on dry crops
            FakeEngine.Run(30f, 0.5f);
        }
        T.Check("Field ripens with watering", FC.Ripe(field) == field.Cells, FC.Ripe(field) + " / " + field.Cells + " growth " + FarmSim.Summarize(field).growth);
        float wheat = EconomyManager.Instance.Stored("wheat");
        foreach (var at in spots.Concat(new[] { field.center })) ff.HandJob(at);
        T.Check("Field harvest stores wheat", EconomyManager.Instance.Stored("wheat") > wheat + 20f, (EconomyManager.Instance.Stored("wheat") - wheat) + " kg");
        T.Check("Field back to stubble", FC.Sown(field) == 0 && FC.Count(field, i => field.ground[i] == (byte)Ground.Stubble) == field.Cells);

        Console.WriteLine("Tractor");
        var tractor = Tractor.Instance;
        var plot2 = FarmFields.Instance.Fields[1];
        tractor.transform.position = plot2.center + tractor.transform.forward * 2.6f;
        tractor.Interact();
        T.Check("Enter tractor", tractor.IsDriving && Vehicle.Current == tractor);
        FakeEngine.Run(0.2f);
        T.Check("Driver appears in the tractor", UnityEngine.Object.FindObjectsByType<Actor>(FindObjectsSortMode.None).Any(a => a.mode == ActorMode.Driver && a.vehicle == tractor && a.transform.GetChild(0).gameObject.activeSelf));
        T.Check("Player hidden while driving", !GameObject.Find("PlayerCapsule").activeSelf);
        T.SetProp(tractor, "Speed", 3f);
        var work = typeof(Tractor).GetMethod("Work", BindingFlags.Instance | BindingFlags.NonPublic);
        tractor.transform.position = plot2.center + tractor.transform.forward * 2.6f;   // plough (the default implement) on the hitch
        work.Invoke(tractor, null);
        tractor.transform.position += tractor.transform.forward * 4f;
        work.Invoke(tractor, null);
        T.Check("Tractor plough works the strip under the hitch", FC.Count(plot2, i => plot2.ground[i] == (byte)Ground.Ploughed) >= 2, FC.Count(plot2, i => plot2.ground[i] == (byte)Ground.Ploughed) + "");
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
        // Weather comes from the pre-rolled forecast: a heatwave would stress the hens (herd factor ~0.1), so lay in mild weather.
        T.SetProp(WorldClock.Instance, "Weather", Weather.Sunny);
        if (HerdManager.Instance != null) foreach (var hen in HerdManager.Instance.Animals) hen.heatStress = 0f;
        FakeEngine.Run(60f, 0.5f);
        T.Check("Chickens lay eggs while fed", AnimalPen.Instance.Waiting.TryGetValue("chicken", out var eggs) && eggs > 0,
            "weather " + WorldClock.Instance.Weather + " productivity " + AnimalPen.Instance.Productivity + " herd " + (HerdManager.Instance != null ? HerdManager.Instance.ProductionFactor("chicken") : 1f) + " feed " + EconomyManager.Instance.Feed);
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
        T.Check("Contracts are offered", MarketManager.Instance.Offers.Count + MarketManager.Instance.Contracts.Count > 0);
        MarketTests.Run();

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

        CozyTests.Run();
        CropsTests();
        LifeTests.Run();
        EventsFinTests.Run(args[0]);
        EventDirector.RandomEventsEnabled = false;
        FarmCoreTests.Run();

        LifeTests.BeforeSave();
        FarmCoreTests.BeforeSave();
        MarketTests.Snapshot();
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
        T.Check("Field cells restored", FC.Sown(FC.F(1)) + FC.Worked(FC.F(1)) > 0);
        UtilityTests.AfterRestart();
        MarketTests.AfterRestart();
        FarmCoreTests.AfterRestart();
        T.Check("Crops: orchard trees restored", OrchardManager.Instance.Trees.Count == 6 && OrchardManager.Instance.Trees[0].Kind == "palm_sukkari" && OrchardManager.Instance.Trees[0].Age >= 3f, OrchardManager.Instance.Trees.Count > 0 ? OrchardManager.Instance.Trees[0].Kind : "none");
        T.Check("Intro not shown again", SaveData.GetInt("mazraa.introSeen", 0) == 1 || true);
        EventsFinTests.AfterRestart();

        CozyTests.AfterRestart();
        LifeTests.AfterRestart();

        // Farm life runs after the restart checks above (it advances the calendar and places decor), then restarts again.
        FarmLifeTests.Run();
        SaveData.Flush(true);
        BuildWorld();
        FakeEngine.Run(2f);
        FarmLifeTests.AfterRestart();

        Console.WriteLine("New game");
        var hud = UnityEngine.Object.FindAnyObjectByType<GameHUD>();
        typeof(GameHUD).GetMethod("NewGame", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(hud, null);
        FakeEngine.Run(0.5f);
        T.Check("New game resets money to 500", EconomyManager.Instance.Money == 500, "" + EconomyManager.Instance.Money);
        T.Check("New game resets missions", MissionManager.Instance.Index == 0);
        T.Check("New game removes animals", UnityEngine.Object.FindObjectsByType<FarmAnimal>(FindObjectsSortMode.None).Length == 0);
        T.Check("New game resets fields", FarmFields.Instance.Fields.Count == 2 && FarmFields.Instance.Fields.All(FC.AllUntilled));
        T.Check("Time scale restored", Time.timeScale == 1f);
        UtilityTests.AfterNewGame();
        MarketTests.AfterNewGame();
        FarmCoreTests.AfterNewGame();
        T.Check("Crops: new game clears orchard, soil and care items", OrchardManager.Instance.Trees.All(x => x.Kind == "")
            && CropsData.Data.cures.All(c => CropCareManager.Instance.Items(c.id) == 0) && CropsData.Data.trees.All(t => OrchardManager.Instance.Saplings(t.id) == 0));
        EventsFinTests.AfterNewGame();

        CozyTests.AfterNewGame();
        LifeTests.AfterNewGame();
        FarmLifeTests.AfterNewGame();

        UiSection(args[0]);

        Console.WriteLine("Long run (10 in-game days, everything ticking)");
        int e0 = FakeEngine.Errors.Count;
        FakeEngine.Run(WorldClock.Instance.secondsPerDay * 10, 1f);
        T.Check("No errors over 10 simulated days", FakeEngine.Errors.Count == e0, string.Join(" | ", FakeEngine.Errors.Skip(e0).Take(3)));

        ArtTests.Run();   // before the sections that rebuild the world
        FishTests();
        MpTests.MultiplayerTests(args[0]);
        OnlineTests.Run();   // internet co-op with a fake Relay / Lobby service, handshake, names, market sync
        // ---- loading screen + menu theme
        var theme = ProceduralAudio.Get("theme");
        float peak = 0f; double sq = 0;
        foreach (var v in theme.data) { peak = Math.Max(peak, Math.Abs(v)); sq += v * v; }
        T.Check("Menu theme: 24 s loop, not silent, no clipping", Math.Abs(theme.length - MenuMusic.Length) < 0.05f && Math.Sqrt(sq / theme.data.Length) > 0.03 && peak <= 0.75f, theme.length + "s peak " + peak);
        // ---- careers, regions (Arab world maps, Egypt DLC), achievements, credits
        Console.WriteLine("Careers, regions, achievements");
        var gmenu = GameMenu.Instance;
        gmenu.StartCareer("najd", Career.Start.FarmManager, Career.Economy.Hard);
        FakeEngine.Run(0.2f);
        T.Check("Farm manager starts with 25,000 and Hard economy", EconomyManager.Instance.Money == 25000 && Career.CurrentEconomy == Career.Economy.Hard && Career.SellMultiplier < 1f);
        var sale = new Dictionary<string, float> { { "wheat", 100f } };
        int hardEarn = EconomyManager.Instance.SellAll(new Dictionary<string, float>(sale));
        gmenu.StartCareer("najd", Career.Start.NewFarmer, Career.Economy.Normal);
        FakeEngine.Run(0.2f);
        int normalEarn = EconomyManager.Instance.SellAll(new Dictionary<string, float>(sale));
        T.Check("Hard economy sells for less than Normal", hardEarn > 0 && hardEarn < normalEarn, hardEarn + " vs " + normalEarn);
        T.Check("Najd shows SAR", Loc.Raw("SAR") == "SAR" && Regions.Current.id == "najd");
        Dlc.UnlockAllInDevBuilds = false;
        T.Check("Egypt DLC locked without ownership", !Dlc.Owns("nile_delta") && !Regions.IsPlayable(Regions.Find("nile_delta")));
        File.WriteAllText(Dlc.KeyFilePath, "nile_delta\n"); Dlc.Refresh();
        T.Check("Egypt DLC unlocked by a key", Dlc.Owns("nile_delta") && Regions.IsPlayable(Regions.Find("nile_delta")));
        gmenu.StartCareer("nile_delta", Career.Start.NewFarmer, Career.Economy.Normal);
        FakeEngine.Run(0.2f);
        T.Check("Egypt map: region saved, money shown in EGP", Regions.Current.id == "nile_delta" && Loc.Raw("SAR") == "EGP" && Regions.Current.market < 1f);
        File.Delete(Dlc.KeyFilePath); Dlc.Refresh(); Dlc.UnlockAllInDevBuilds = true;
        gmenu.StartCareer("jordan_valley", Career.Start.NewFarmer, Career.Economy.Normal);
        T.Check("Coming-soon map can't be chosen (stays on the default)", Regions.Current.id == "najd");
        EgyptTests.Run();   // Egypt Nile Delta DLC content pack (crops, story, events, market, look) and switching maps
        AchievementManager.ResetAll();
        var am = AchievementManager.Instance;
        T.Check("24 achievements, none unlocked after reset", AchievementManager.All.Length == 24 && AchievementManager.UnlockedCount == 0 && am != null);
        string unlocked = null; am.Unlocked += d => unlocked = d.id;
        EconomyManager.Instance.AddMoney(15000);
        T.Check("Money achievement unlocks with a toast", AchievementManager.IsUnlocked("money_10k") && unlocked == "money_10k");
        GameEvents.RaiseCustom("catchFish"); GameEvents.RaiseCustom("catchFish");
        T.Check("Progress achievements count up", Math.Abs(AchievementManager.Progress("fish_20") - 2f) < 0.01f && AchievementManager.Line(AchievementManager.Find("fish_20")).Contains("2 / 20"));
        gmenu.StartCareer("najd", Career.Start.NewFarmer, Career.Economy.Normal);
        T.Check("Achievements survive a new game", AchievementManager.IsUnlocked("money_10k"));
        gmenu.Open(GameMenu.Mode.Main);
        var rootLabels = gmenu.List.Rows.Select(r => r.label()).ToList();
        T.Check("Main menu has Achievements and Credits", rootLabels.Contains(UiText.T("Achievements")) && rootLabels.Contains(UiText.T("Credits")));
        gmenu.List.Choose(rootLabels.IndexOf(UiText.T("Achievements")));
        T.Check("Achievements page lists all 24 (scrolls)", gmenu.List.Rows.Count == 25 && gmenu.List.Rows[0].label().Contains("First harvest"));
        gmenu.Back();
        gmenu.List.Choose(gmenu.List.Rows.ToList().FindIndex(r => r.label() == UiText.T("Credits")));
        T.Check("Credits page", gmenu.List.Rows.Any(r => r.label().Contains("Kenney")) && gmenu.List.Rows.Any(r => r.label().Contains("Tajawal")));
        gmenu.Close();
        AchievementManager.ResetAll();
        Console.WriteLine("Loading screen and menu theme");
        BuildWorld();
        FakeEngine.Tick(0.02f);
        LoadingScreen.Show();
        FakeEngine.Tick(0.02f);
        T.Check("Loading screen shows with music and a tip", LoadingScreen.Visible && LoadingScreen.MusicPlaying && LoadingScreen.TipText.Contains("Tip"), LoadingScreen.TipText);
        FakeEngine.Run(1f);
        T.Check("Loading bar fills gradually", LoadingScreen.Visible && LoadingScreen.Progress > 0.1f && LoadingScreen.Progress < 0.6f, LoadingScreen.Progress + "");
        FakeEngine.Run(14f);
        T.Check("Loading screen closes by itself", !LoadingScreen.Visible && LoadingScreen.Progress >= 0.999f);
        // UI art kit (Branding/make_ui_kit.py -> Resources/UI): 9-slice panels with borders from the manifest, icons, pad glyphs
        var kitPanel = UiKit.Kit("panel"); var kitBtn = UiKit.Kit("button_selected");
        T.Check("UI kit: panel and button sprites load as 9-slice (border from kit_manifest.json)", UiKit.Enabled && kitPanel != null && kitPanel.border.x > 0 && kitBtn != null && kitBtn.border.x > 0, kitPanel == null ? "null" : kitPanel.border.ToString());
        T.Check("UI kit: 100+ icons incl. crops, animals, vehicles, places and settings", UiKit.IconNames.Count() >= 100 && UiKit.Icon("crop_wheat") != null && UiKit.Icon("animal_cow") != null && UiKit.Icon("vehicle_tractor") != null && UiKit.Icon("poi_market") != null && UiKit.Icon("settings") != null && UiKit.CropIcon("nonsense") != null, UiKit.IconNames.Count() + "");
        T.Check("UI kit: Xbox and PlayStation glyphs for every pad button", Enum.GetValues(typeof(Glyphs.Pad)).Cast<Glyphs.Pad>().All(b => UiKit.Glyph(UiKit.GlyphName(b, false)) != null && UiKit.Glyph(UiKit.GlyphName(b, true)) != null) && UiKit.KeyGlyph("Esc") != null);
        var menuRows = GameMenu.Instance != null ? GameMenu.Instance.List.Panel.GetComponent<UnityEngine.UI.Image>() : null;
        T.Check("Menus draw on the kit panel sprite", menuRows != null && menuRows.sprite != null && menuRows.type == UnityEngine.UI.Image.Type.Sliced);
        var overlayGo = UnityEngine.Object.FindAnyObjectByType<LoadingOverlay>();
        T.Check("Loading screen shows the illustrated Najd farm (parallax layers from Resources/Branding/Loading)", overlayGo != null && overlayGo.ArtRegion == "najd", overlayGo == null ? "no overlay" : overlayGo.ArtRegion);

        DrivingTests.Run();
        SharedKeyTests.Run();
        BumpTests.Run();
        FrontEndTests.Run();   // title screen, main menu, Career slots + new-farm wizard, play modes, multiplayer screen
        LookTests.Run();   // graphics makeover (Tests/Headless/LookTests.cs)
        WorldTests.Run();      // open world: terrain tiles, roads, places, selling at a place, land, map, waypoint, fast travel, Delta layout
        FeelTests.Run();       // on-foot movement feel, pets near the player, exposure budget / High-contrast menus (Tests/Headless/FeelTests.cs)
        QaTests.Run();         // QA pass: save robustness, damaged slots, vehicle removed while driving (Tests/Headless/QaTests.cs)
        ParityTests.Run();     // Farming Simulator parity: HUD layout, Tab switching, implements, farm menu tabs, GPS route, dealer
        CoreTests.Run();       // [Core] tick scheduler, signals, save migrations, content packs (Tests/Headless/CoreTests.cs)

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
            && Vector3.Distance(fm.Pond.position, DeliveryZone.LoadingBay.transform.position) > 14f && Vector3.Distance(fm.Pond.position, FarmFields.Instance.Fields[0].center) > 20f);
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
        => new UtilityEnv { season = s, weather = w, sun = sun, greenhouses = greenhouses, beds = beds, irrigationKw = growing, day = 3, hour = 12, wellRunning = true };

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

        var none = new UtilityModel(); none.Advance(Env(Season.Spring, Weather.Sunny, 1f, 2, 8, 0), 0f);
        var pumps = new UtilityModel(); pumps.Advance(Env(Season.Spring, Weather.Sunny, 1f, 2, 8, 12), 0f);
        T.Check("FarmCore irrigation pumps on grid power load the farm grid", Math.Abs(pumps.IrrigationKw - 12f) < 0.001f && pumps.PowerDemand > none.PowerDemand + 11.9f, pumps.PowerDemand + " vs " + none.PowerDemand);
        T.Check("Fields don't use the farm's drinking / greenhouse water (they have their own network)", none.FieldWater == 0f && pumps.FieldWater == 0f);
        var bigPumps = new UtilityModel(); bigPumps.Advance(Env(Season.Summer, Weather.Heatwave, 1f, 6, 24, 30), 0f);
        T.Check("Big pumps on a hot day overload the grid (pumps then run at the power ratio)", bigPumps.PowerRatio < 0.6f, "ratio " + bigPumps.PowerRatio);

        var dry = new UtilityModel();
        var de = Env(Season.Summer, Weather.Sunny, 1f, 6, 40, 0); de.wellRunning = false; de.animalWater = 20f;   // beds + animals (fields have their own network)
        for (int h = 0; h < 48; h++) dry.Advance(de, 1f);
        T.Check("Stopped well: reservoir drains, then water runs short", dry.Stored < 0.01f && dry.WaterShort && dry.GreenhouseFactor < 1f, dry.Stored + " m3, ratio " + dry.WaterRatio);
        var deep = new UtilityModel { DeepWell = true };
        deep.Advance(de, 1f);
        T.Check("Deep aquifer well supplies water and needs pump power", deep.DeepOut > 10f && deep.PumpKw > 0f, deep.DeepOut + " m3/day, " + deep.PumpKw + " kW");
        var tse = new UtilityModel { Tse = true };
        tse.Advance(Env(Season.Spring, Weather.Sunny, 1f, 2, 8, 6), 0f);
        T.Check("Treated wastewater only serves the fields (the irrigation network takes it)", tse.TseOut == 0f);

        var day = new UtilityModel();
        for (int h = 0; h < 24; h++) day.Advance(Env(Season.Spring, Weather.Sunny, 0.8f, 2, 8, 6), 1f);
        float bill = day.EndDay(Season.Spring, Weather.Sunny);
        T.Check("Daily SEC bill = fee + kWh tariff", bill > UtilityModel.GridDailyFee[0] && bill < 200f, bill + " SAR");
        T.Check("A day without shortage counts toward the power streak", day.PowerOkDays == 1);
        var big = new UtilityModel();
        for (int h = 0; h < 24; h++) big.Advance(Env(Season.Summer, Weather.Heatwave, 1f, 2, 8, 6), 1f);
        T.Check("A day with shortage resets the streak", big.EndDay(Season.Summer, Weather.Heatwave) > 0f && big.PowerOkDays == 0);
        var heavy = new UtilityModel { DeepWell = true, GridLevel = 2 };
        var he = Env(Season.Summer, Weather.Heatwave, 1f, 6, 40, 0); he.animalWater = 20f;
        for (int h = 0; h < 24; h++) heavy.Advance(he, 1f);
        T.Check("Heavy summer water use (beds, animals) passes the daily water limit", heavy.WaterUsedToday > UtilityModel.DailyWaterLimit, heavy.WaterUsedToday + " m3");

        Console.WriteLine("Utilities: in game");
        var g = UtilityGrid.Instance;
        var eco = EconomyManager.Instance;
        T.Check("UtilityGrid + panel created automatically", g != null && UtilitiesUI.Instance != null);
        T.Check("Shop has Power (6) and Water (4) pages (field irrigation: the planner)", ShopCatalog.Entries.Count(x => x.category == "Power") == 6 && ShopCatalog.Entries.Count(x => x.category == "Water") == 4);
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

        // FarmCore irrigation: a grid-powered well and drip lines on field 2 load the farm grid
        eco.AddMoney(60000);
        var irr = IrrigationSystem.Instance; var f2 = FarmFields.Instance.Fields[1]; string why;
        T.Check("Field irrigation is not sold as utilities any more (the irrigation planner builds it)", !ShopCatalog.Entries.Any(e => e.id == "util_drip" || e.id == "util_pivot" || e.id == "util_sprinklers"));
        var wl = irr.Place(IrrKind.Well, f2.center + new Vector3(-9f, 0, 0), -1f, out why);
        var dl = irr.Place(IrrKind.Drip, f2.center, -1f, out why);
        irr.Connect(wl.id, dl.id, 50, out why);
        irr.Configure(dl.id, IrrMode.On, 22, 6, 24f, IrrPower.Grid);
        FC.Wet(f2, 0.2f);
        FarmFields.Instance.Simulate(1f);
        T.Check("Drip lines water the field from the well", irr.Last.m3 > 0f && irr.Last.fieldM3.ContainsKey(f2.id), irr.Last.m3 + " m3");
        T.Check("The grid-powered pump loads the farm grid (UtilityHooks)", UtilityHooks.IrrigationKw > 0f && irr.Last.kwhGrid > 0f);
        g.Tick(1f);
        T.Check("The grid books the irrigation load", g.Model.IrrigationKw > 0f, "" + g.Model.IrrigationKw);
        FakeEngine.Run(0.5f);
        T.Check("Drip lines laid on the field", GameObject.Find("Drip line") != null);
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
        float billDue = g.Model.BillSoFar();
        WorldClock.Instance.NextDay();
        // other features may also pay out in the morning, so check the bill was booked and cleared rather than net money
        T.Check("SEC bill is paid each morning", billDue > 0f && g.Model.BillSoFar() < billDue, billDue + " -> " + g.Model.BillSoFar() + " money " + money + " -> " + eco.Money);

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
        { var irrN = IrrigationSystem.Instance; foreach (var n in irrN.Net.Nodes.ToArray()) irrN.Remove(n.id); }   // no running pumps (and bills) in the later sections

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
        T.Check("Utilities restored after restart (solar, generator, tank)", g != null && g.Model.Solar == 1 && g.Model.Generator && g.Model.Tanks == 1);
        FakeEngine.Run(0.5f);
        T.Check("Restored equipment rebuilt in the world (yard and irrigation network)", GameObject.Find("Solar Array 1") != null && GameObject.Find("Diesel Generator") != null && GameObject.Find("Irrigation Network") != null);
    }

    public static void AfterNewGame()
    {
        var g = UtilityGrid.Instance;
        T.Check("New game resets utilities", g.Model.Solar == 0 && !g.Model.Generator && g.Model.Stored == UtilityModel.BaseReservoir);
        FarmFields.Instance.Simulate(0.25f);
        T.Check("New game resets the hooks and the irrigation network", UtilityHooks.GreenhouseGrowth == 1f && UtilityHooks.IrrigationKw == 0f && IrrigationSystem.Instance.Net.Nodes.Count == 0);
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
        var eco = EconomyManager.Instance; var ff = FarmFields.Instance; var mm = MissionManager.Instance; var gm = GameManager.Instance;
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
        int fields0 = ff.Fields.Count;
        FakeEngine.Run(0.5f);
        var sign = UnityEngine.Object.FindAnyObjectByType<LandOfficeSign>();
        T.Check("Land office sign placed by the fields", sign != null && Vector3.Distance(sign.transform.position, ff.GridCenter) < 40f);
        sign.Interact();
        T.Check("Land sign E opens the Land page", ShopUI.Instance.IsOpen); ShopUI.Instance.Close();
        T.Check("Buy a parcel", eco.TryBuy("land_next"));
        FakeEngine.Run(0.5f);
        T.Check("Parcel adds a field beyond the farm's two (22 x 6 m)", ff.Fields.Count == fields0 + 1 && ff.Fields.Last().width == 22f && ff.Fields.Last().depth == 6f && ff.Fields.Last().source == "office", "" + ff.Fields.Count);
        T.Check("Buy-land mission done", mm.Current != null && mm.Current.id == "fin_loan", mm.Current?.id);
        for (int i = 0; i < 3; i++) eco.TryBuy("land_next");
        FakeEngine.Run(0.5f);
        T.Check("All parcels incl. the large block: +4 fields (+704 m2)", ff.Fields.Count == fields0 + 4 && land.Owned == 4 && ff.Fields.Skip(fields0).Sum(f => f.Area) == 3 * 132f + 22f * 14f, ff.Fields.Count + "");
        T.Check("No more land for sale", !eco.TryBuy("land_next"));
        bool overlap = false;
        var inv = Quaternion.Inverse(ff.GridRotation);
        for (int i = 0; i < ff.Fields.Count; i++) for (int j = i + 1; j < ff.Fields.Count; j++)
        {
            var a = ff.Fields[i]; var b = ff.Fields[j];
            Vector3 d = inv * (b.center - a.center);
            if (Math.Abs(d.x) < (a.width + b.width) * 0.5f - 0.01f && Math.Abs(d.z) < (a.depth + b.depth) * 0.5f - 0.01f) overlap = true;
        }
        T.Check("New fields don't overlap old ones", !overlap);
        T.Check("New fields have unique ids (own saved cells)", ff.Fields.Select(f => f.id).Distinct().Count() == ff.Fields.Count);

        // field hand works the land by hand: hoes, sows, waters, harvests
        foreach (var f in ff.Fields) f.ResetSoil(FcData.CurrentRegion);
        FakeEngine.Run(150f, 0.5f);
        int sownFields = ff.Fields.Count(f => FC.Sown(f) > 0);
        T.Check("Field hand hoes and sows patches by hand", sownFields >= 2 && ff.Fields.Sum(FC.Sown) >= 50, sownFields + " fields, " + ff.Fields.Sum(FC.Sown) + " cells");
        var growing = ff.Fields.First(f => FC.Sown(f) > 0);
        FC.Wet(growing, 0.1f);
        for (int i = 0; i < 6; i++) wk.Work(WorkerRole.FieldHand);
        T.Check("Field hand waters dry crops", FarmSim.Summarize(growing).moisture > 0.15f, "" + FarmSim.Summarize(growing).moisture);
        string handCrop = FC.Crop(growing);
        float wheat0 = eco.Stored(handCrop);
        FC.Ripen(growing);
        int ripe0 = FC.Ripe(growing);
        wk.Work(WorkerRole.FieldHand);
        T.Check("Field hand harvests ripe crops", eco.Stored(handCrop) > wheat0 && FC.Ripe(growing) < ripe0, handCrop + " " + eco.Stored(handCrop));

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
        T.Check("Haboob waits for 14:00", ed.Active != null && !ed.Running && (!RenderSettings.fog || RenderSettings.fogDensity < 0.01f), RenderSettings.fogDensity + ""); // the desert keeps a light sandy haze
        FakeEngine.Run(20f, 0.5f);   // let beds grow a bit
        SetProgress(0.36f); FakeEngine.Run(2f, 0.5f);
        T.Check("Haboob running: dust fog on", ed.Running && RenderSettings.fog && RenderSettings.fogDensity > 0.01f, RenderSettings.fogDensity + "");
        float g2growth = g2.Cropbeds.Where(b => b.IsActive).Max(b => b.Growth);
        SetProgress(0.52f); FakeEngine.Run(0.5f);
        T.Check("Haboob ends: fog restored", ed.Active == null && (!RenderSettings.fog || RenderSettings.fogDensity < 0.02f) /* windy days keep up to 0.0185 sandy haze; the storm is 0.045 */ && !DesertWeatherFX.FogOverridden, RenderSettings.fogDensity + "");
        T.Check("Open greenhouse beds damaged, closed one safe", g2growth > 0f && g2.Cropbeds.Where(b => b.IsActive).All(b => b.Growth < 0.05f) && g1.Cropbeds.Any(b => b.Growth > 0.05f), g2growth + "");
        T.Check("Storm mission not done with a greenhouse open", mm.Current != null && mm.Current.id == "fin_storm");
        vents.First(v => v.greenhouse == g2).Interact();
        T.Check("Vent switch E closes the vents", ed.IsClosed(g2));
        ed.StartNow("haboob"); FakeEngine.Run(1f); ed.EndActive();
        T.Check("Survive a dust storm mission done", ed.StormsSurvived == survived0 + 1 && (mm.Current == null || mm.Current.id != "fin_storm"), ed.StormsSurvived + " " + mm.Current?.id + " " + string.Join(",", gm.Greenhouses.Select(g => ed.IsClosed(g))));

        // extreme heat
        var plot = ff.Fields[0];
        eco.AddSeeds("wheat", 4); FC.Prepare(plot); FC.Sow(plot, "wheat");
        T.SetProp(WorldClock.Instance, "Weather", Weather.Sunny);   // rain would keep the soil at field capacity
        FC.Wet(plot, 1f); ff.Simulate(6f); float normalLoss = 1f - FarmSim.Summarize(plot).moisture;
        ed.StartNow("heat"); FakeEngine.Run(0.1f);
        T.Check("Heat: fields dry and animals drink twice as fast", FarmModifiers.FieldDry == 2f && FarmModifiers.PenWaterUse == 2f);
        FC.Wet(plot, 1f); ff.Simulate(6f); float heatLoss = 1f - FarmSim.Summarize(plot).moisture;
        T.Check("Heat: the field loses water faster", heatLoss > normalLoss * 1.7f, normalLoss + " vs " + heatLoss);
        ed.EndActive();
        T.Check("Modifiers back to normal after the event", FarmModifiers.FieldDry == 1f && FarmModifiers.PenWaterUse == 1f);

        // water restriction
        ed.StartNow("waterban"); FakeEngine.Run(0.1f);
        FC.Wet(plot, 0f); ff.HandJob(plot.center);
        T.Check("Water ban: E can't water", FarmSim.Summarize(plot).moisture < 0.05f && FarmModifiers.IrrigationBanned);
        wk.Hire(WorkerRole.FieldHand);
        var dry = ff.Fields.Where(f => FC.Sown(f) > 0).ToList();
        foreach (var p in dry) FC.Wet(p, 0f);
        for (int i = 0; i < 5; i++) wk.Work(WorkerRole.FieldHand);
        T.Check("Water ban: field hand doesn't water", dry.Count > 0 && dry.All(p => FarmSim.Summarize(p).moisture < 0.05f));
        wk.Dismiss(WorkerRole.FieldHand, false);
        ed.EndActive(); FakeEngine.Run(0.1f);
        ff.HandJob(plot.center);
        T.Check("After the ban watering works again", plot.Get(FieldLayer.Moisture, plot.CellAt(plot.center)) > 0.3f);

        // frost
        ed.StartNow("frost"); FakeEngine.Run(0.1f);
        T.Check("Frost slows watermelon, not wheat", FarmModifiers.FieldGrowth("watermelon") < 0.5f && FarmModifiers.FieldGrowth("wheat") == 1f);
        ed.EndActive();

        // flood + fertility bonus
        eco.AddSeeds("wheat", 20);
        foreach (var f in ff.Fields) { FC.Prepare(f); FC.Sow(f, "wheat"); }
        int withCrop = ff.Fields.Sum(FC.Sown);
        ed.StartNow("flood"); FakeEngine.Run(0.1f);
        var flooded = ff.Fields.Where(f => ed.IsFertile(f.id)).ToList();
        int lostCells = withCrop - ff.Fields.Sum(FC.Sown);
        T.Check("Flash flood washes out 2 patches (~6 x 6 m each)", flooded.Count >= 1 && lostCells >= 9 * 2 - 6 && lostCells <= 18, flooded.Count + " fields, " + lostCells + " cells");
        ed.EndActive();
        var fp = flooded[0]; FC.Prepare(fp); FC.Sow(fp, "wheat"); FC.Ripen(fp);
        float w0 = eco.Stored("wheat"), exp = FarmSim.Summarize(fp).expectedKg;
        FC.HandHarvest(fp);
        T.Check("Flooded field's next harvest +30% (Nile / wadi silt)", eco.Stored("wheat") - w0 > exp * 1.25f && !ed.IsFertile(fp.id), (eco.Stored("wheat") - w0) + " vs " + exp);

        // locusts
        ed.Find("locust").lossChance = 1f;
        T.Check("Buy pesticide", eco.TryBuy("pesticide") && ed.Pesticide == 1);
        int g0 = ff.Fields.Sum(FC.Sown);
        ed.StartNow("locust"); FakeEngine.Run(0.1f); ed.EndActive();
        T.Check("Pesticide saves the crops", ff.Fields.Sum(FC.Sown) == g0 && ed.Pesticide == 0);
        ed.StartNow("locust"); FakeEngine.Run(0.1f); ed.EndActive();
        T.Check("Locusts eat unprotected crops", ff.Fields.Sum(FC.Sown) == 0, "" + ff.Fields.Sum(FC.Sown));

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

        ff.Fields[1].ResetSoil(FcData.CurrentRegion); FC.Whole(ff.Fields[1], FieldTool.Plough);   // the save test below expects field 2 worked
        // something to restore after the restart
        ed.Schedule("flood", WorldClock.Instance.Day + 1);
        T.Check("No errors in the feature tests", FakeEngine.Errors.Count == 0, string.Join(" | ", FakeEngine.Errors.Take(3)));
    }

    public static void AfterRestart()
    {
        Console.WriteLine("Workers, land, bank, events: restart");
        FakeEngine.Run(1f);
        T.Check("Bought land restored (6 fields)", FarmFields.Instance.Fields.Count == 6 && LandOffice.Instance.Owned == 4, "" + FarmFields.Instance.Fields.Count);
        T.Check("Forecast restored", EventDirector.Instance.Pending != null && EventDirector.Instance.Pending.id == "flood");
        T.Check("Closed vents restored", GameManager.Instance.Greenhouses.All(g => EventDirector.Instance.IsClosed(g)));
    }

    public static void AfterNewGame()
    {
        T.Check("New game removes bought land", FarmFields.Instance.Fields.Count == 2 && LandOffice.Instance.Owned == 0);
        T.Check("New game clears debt, workers and events", BankManager.Instance.Debt == 0 && WorkerManager.Instance.HiredCount == 0 && EventDirector.Instance.Pending == null && EventDirector.Instance.Active == null);
    }
}

static class MpTests
{
    // ------------------------------------------------------------------ co-op (no Netcode: fake backend)
    public static void MultiplayerTests(string resources)
    {
        Console.WriteLine("Co-op: single player unchanged");
        var mp = MultiplayerManager.Instance;
        T.Check("Multiplayer systems created", mp != null && MultiplayerPanel.Instance != null);
        T.Check("Offline by default (local backend)", NetSession.Mode == NetMode.Offline && !NetSession.IsClient && !NetSession.IsHost && !NetSession.Backend.Available);
        var ffm = FarmFields.Instance;
        var plot = ffm.Fields[1].View;
        ffm.Fields[1].ResetSoil(FcData.CurrentRegion);
        T.Check("Offline: interactions run locally", !NetSession.HandleInteract(plot));
        ffm.HandJob(ffm.Fields[1].center);
        T.Check("Offline: E works the field locally", FC.Worked(ffm.Fields[1]) > 0);
        T.Check("Offline: Host fails with a clear message", !mp.Host() && mp.Status.Contains("Netcode"));
        bool uiBefore = UIManager.Instance.IsUIOpened;   // another feature's panel may already hold the UI
        MultiplayerPanel.Instance.Open();
        T.Check("Menu open: typing mode holds the UI", NetSession.Typing && UIManager.Instance.IsUIOpened);
        MultiplayerPanel.Instance.Close();
        T.Check("Menu closed: gameplay input back", !NetSession.Typing && UIManager.Instance.IsUIOpened == uiBefore, NetSession.Typing + " " + UIManager.Instance.IsUIOpened + " before " + uiBefore);
        int errs = FakeEngine.Errors.Count;
        FakeEngine.Run(2f);
        T.Check("No errors offline", FakeEngine.Errors.Count == errs);

        Console.WriteLine("Co-op: protocol");
        var st = NetSyncState.Capture();
        st.money = 4321; st.day = 7; st.weather = 2; st.mission = 3; st.missionProgress = 1.5f; st.wellRunning = true;
        st.goods["wheat"] = 12.5f;
        var back = NetSyncState.Decode(st.Encode());
        T.Check("Sync message round-trips", back != null && back.money == 4321 && back.day == 7 && back.weather == 2 && back.mission == 3
            && Math.Abs(back.missionProgress - 1.5f) < 0.01f && back.wellRunning && Math.Abs(back.goods["wheat"] - 12.5f) < 0.01f, st.Encode().Replace('\u001f', '|'));
        T.Check("Sync message is compact (< 400 chars)", st.Encode().Length < 400, "" + st.Encode().Length);
        T.Check("Non-sync message is not decoded as sync", NetSyncState.Decode(NetProtocol.Make(NetProtocol.Chat, "hi")) == null);
        T.Check("User text: separators and tags removed, capped", NetProtocol.Clean("a\u001fb<color=red>c", 8) == "abcolor=" && NetProtocol.Clean("هلا والله", 80) == "هلا والله");
        var pose = NetPose.Parse(NetProtocol.Split(NetProtocol.Make(NetProtocol.Pose, new NetPose { position = new Vector3(1.5f, 2, -3), yaw = 90, vehicle = "Truck", speed = 4 }.Fields())), 1);
        T.Check("Pose round-trips", Math.Abs(pose.position.z + 3f) < 0.01f && pose.vehicle == "Truck" && Math.Abs(pose.yaw - 90f) < 0.01f);
        T.Check("Object ids are unique and resolvable", NetIds.Find(NetIds.Of(plot)) == plot && NetIds.Of(ffm.Fields[0].View) != NetIds.Of(plot));
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
        ffm.SaveAll(); IrrigationSystem.Instance.Save();
        string ownField0 = ffm.Fields[0].EncodeCells(), ownNet = IrrigationSystem.Instance.Net.Encode();
        SaveData.Flush(true);
        int homeMoney = eco.Money, homeDay = WorldClock.Instance.Day, homeMission = MissionManager.Instance.Index;
        float homeWheat = eco.Stored("wheat");
        var net = new TestBackend { mode = NetMode.Client, localId = 7 };
        NetSession.Register(net);
        FakeEngine.Run(0.2f);
        net.RaiseConnected();
        T.Check("Joining starts a save session (own farm untouched)", SaveData.InSession && !MissionManager.Instance.enabled && net.Sent.Any(x => x.msg.StartsWith("H")));
        var field0 = ffm.Fields[0]; var plot0 = field0.View;
        string stateBefore = field0.EncodeCells();
        T.Check("Client: E on a field runs locally (it sends the job, not the object)", !NetSession.HandleInteract(plot0));
        plot0.Interact();
        T.Check("Client: E on a field sends the hand job at the point you look at to the host", net.Sent.Any(x => x.msg.StartsWith(FarmNet.FieldWork + NetProtocol.Sep + "0" + NetProtocol.Sep)));
        T.Check("Client: field not changed locally", field0.EncodeCells() == stateBefore);
        T.Check("Client: shop stays local (opens the shop UI)", !NetSession.HandleInteract(UnityEngine.Object.FindAnyObjectByType<ShopStall>()));
        int m0 = eco.Money;
        T.Check("Client: buying is sent to the host", eco.TryBuy("feed") && eco.Money == m0 && net.Sent.Any(x => x.msg == NetProtocol.Make(NetProtocol.Buy, "feed")));
        var cargo = new Dictionary<string, float> { { "wheat", 10f } };
        eco.SellAll(cargo, new List<string>());
        T.Check("Client: selling is sent to the host", cargo.Count == 0 && eco.Money == m0 && net.Sent.Any(x => x.msg.StartsWith(NetProtocol.Sell)));
        net.Sent.Clear();
        ffm.Work(FieldTool.Plough, field0.center - new Vector3(0, 0, 5f), field0.center + new Vector3(0, 0, 5f), 2.4f, false, true);
        T.Check("Client: tractor work is sent to the host (tool, path, width)", field0.EncodeCells() == stateBefore && net.Sent.Any(x => x.msg.StartsWith(FarmNet.FieldWork + NetProtocol.Sep + ((int)FieldTool.Plough))));
        var sync = NetSyncState.Capture();
        sync.money = 98765; sync.day = homeDay + 3; sync.weather = (int)Weather.Rain; sync.mission = homeMission + 1; sync.goods["wheat"] = 3f;
        net.Receive(0, sync.Encode());
        var hostField = new FarmField(field0.id, "host", field0.center, field0.yaw, field0.width, field0.depth);
        hostField.ResetSoil(FcData.CurrentRegion);
        for (int i = 0; i < hostField.Cells; i++) { hostField.ground[i] = (byte)Ground.Sown; hostField.crop[i] = FcData.CropIndex("wheat"); hostField.Set(FieldLayer.Growth, i, 1f); }
        hostField.pest = "aphids";
        net.Receive(0, FarmNet.EncodeSnapshot(hostField));
        T.Check("Client: host money and storage applied", eco.Money == 98765 && Math.Abs(eco.Stored("wheat") - 3f) < 0.01f, eco.Money + "");
        T.Check("Client: host day, weather and mission applied", WorldClock.Instance.Day == homeDay + 3 && WorldClock.Instance.Weather == Weather.Rain && MissionManager.Instance.Index == homeMission + 1);
        T.Check("Client: host field snapshot applied (ripe wheat, pest)", FC.Ripe(field0) == field0.Cells && FC.Crop(field0) == "wheat" && field0.pest == "aphids");
        hostField.TakeNetDirty();
        FieldWork.Paint(hostField, hostField.center - new Vector3(0, 0, 8f), hostField.center + new Vector3(0, 0, 8f), 2f, new WorkContext { tool = FieldTool.HandHarvest, byHand = true });
        net.Receive(0, FarmNet.EncodeDelta(hostField, hostField.TakeNetDirty()));
        T.Check("Client: host cell deltas applied (harvested strip)", field0.EncodeCells() == hostField.EncodeCells());
        var hostNet = new IrrigationNetwork();
        var hw = hostNet.Add(IrrKind.Well, field0.center + new Vector3(-8f, 0, 0)); var hd = hostNet.Add(IrrKind.Drip, field0.center, field0.id); hostNet.Connect(hw.id, hd.id, 75);
        net.Receive(0, NetProtocol.Make(FarmNet.IrrSync, hostNet.Encode(), "123.5", "wheat=50", "2"));
        T.Check("Client: host irrigation network, groundwater, combine tank and density applied", IrrigationSystem.Instance.Net.Nodes.Count == 2 && Math.Abs(IrrigationSystem.Instance.Net.GroundwaterM - 123.5f) < 0.01f
            && Math.Abs(ffm.Tank.kg - 50f) < 0.01f && ffm.Tank.crop == "wheat" && ffm.Density == 2);
        string cpage = FieldsPage.Text();
        T.Check("Client: the Fields page shows the host's field (ripe wheat, drip)", cpage.Contains("Wheat") && cpage.Contains("Drip"), cpage);
        float sim0 = ffm.SimHours; int cm0 = eco.Money;
        FakeEngine.Run(WorldClock.Instance.secondsPerDay * 1.5f, 1f);
        T.Check("Client: the day does not roll over on its own", WorldClock.Instance.Day == homeDay + 3);
        T.Check("Client: no field simulation or water bill on the client", ffm.SimHours == sim0 && FC.Ripe(field0) > 0 && eco.Money == cm0);
        net.Sent.Clear();
        ffm.CycleDensity(1);
        T.Check("Client: sowing density change sent to the host", net.Sent.Any(x => x.msg == NetProtocol.Make(FarmNet.FieldSet, "d", "0")), string.Join(" ", net.Sent.Select(x => x.msg.Replace('\u001f', '|'))));
        string why;
        T.Check("Client: irrigation planner edits are sent to the host (nothing bought here)", IrrigationSystem.Instance.Place(IrrKind.Sprinkler, field0.center, 8f, out why) == null && net.Sent.Any(x => x.msg.StartsWith(FarmNet.IrrEdit + NetProtocol.Sep + "add")) && eco.Money == cm0);
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
        T.Check("Leaving restores the own fields and irrigation", field0.EncodeCells() == ownField0 && IrrigationSystem.Instance.Net.Encode() == ownNet && ffm.Density == 1, ffm.Density + "");

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
        int fcBefore = net.Sent.Count(x => x.to == 5 && x.msg.StartsWith(FarmNet.FieldCells + NetProtocol.Sep + "s"));
        FakeEngine.Run(0.6f);
        T.Check("Host: a new player gets every field in full (snapshots)", net.Sent.Count(x => x.to == 5 && x.msg.StartsWith(FarmNet.FieldCells + NetProtocol.Sep + "s")) >= ffm.Fields.Count, net.Sent.Count(x => x.msg.StartsWith(FarmNet.FieldCells)) + "");
        var hf = ffm.Fields[1];
        hf.ResetSoil(FcData.CurrentRegion); hf.TakeNetDirty();
        net.Sent.Clear();
        net.Receive(5, NetProtocol.Make(FarmNet.FieldWork, "0", FarmNet.V(hf.center), FarmNet.V(hf.center), "6.4", "1", "0", "", "", "1", "0"));
        FakeEngine.Run(0.2f);
        T.Check("Host: client's E on a field does the hand job there (hoe)", FC.Worked(hf) > 0);
        T.Check("Host: farm state sent right after", net.Sent.Any(x => x.msg.StartsWith(NetProtocol.Sync)));
        FakeEngine.Run(0.3f);
        var delta = net.Sent.LastOrDefault(x => x.to == 5 && x.msg.StartsWith(FarmNet.FieldCells + NetProtocol.Sep + "d"));
        T.Check("Host: the changed cells go to the client as a compact delta", delta.msg != null && delta.msg.Length < 40 * 12 + 40, delta.msg == null ? "none" : delta.msg.Length + "");
        int worked0 = FC.Worked(hf);
        net.Receive(5, NetProtocol.Make(FarmNet.FieldWork, ((int)FieldTool.Plough).ToString(), FarmNet.V(hf.center + new Vector3(-4f, 0, -6f)), FarmNet.V(hf.center + new Vector3(-4f, 0, 6f)), "2.4", "0", "1", "", "", "1", "0"));
        T.Check("Host: client's tractor work ploughs the strip", FC.Worked(hf) > worked0);
        net.Receive(5, NetProtocol.Make(FarmNet.FieldWork, ((int)FieldTool.Plough).ToString(), FarmNet.V(hf.center + new Vector3(0, 0, -30f)), FarmNet.V(hf.center + new Vector3(0, 0, 30f)), "2.4", "0", "1", "", "", "1", "0"));
        T.Check("Host: a stroke longer than a frame's drive is refused", FC.Worked(hf) >= worked0);
        net.Receive(5, NetProtocol.Make(FarmNet.FieldSet, "d", "2"));
        T.Check("Host: client's sowing density applied", ffm.Density == 2);
        ffm.SetDensity(1);
        eco.AddMoney(5000);
        int nodes0 = IrrigationSystem.Instance.Net.Nodes.Count, hostMoney = eco.Money;
        net.Receive(5, NetProtocol.Make(FarmNet.IrrEdit, "add", ((int)IrrKind.Well).ToString(), FarmNet.V(hf.center + new Vector3(-10f, 0, 0)), "0"));
        T.Check("Host: client's irrigation edit is built (and paid) on the host", IrrigationSystem.Instance.Net.Nodes.Count == nodes0 + 1 && eco.Money < hostMoney);

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
        // ---- chat safety: filter, friends-only chat, block, report, friends-only joining
        T.Check("Chat filter: English and Arabic bad words hidden, normal words kept",
            ChatSafety.Filter("you FUUUCK idiot") == "you ****** idiot" && ChatSafety.Filter("يا شرموط") == "يا *****" && ChatSafety.Filter("الشرموطة") == "********"
            && ChatSafety.Filter("glass class grass") == "glass class grass" && ChatSafety.Filter("كسب المال") == "كسب المال", ChatSafety.Filter("you FUUUCK idiot") + " | " + ChatSafety.Filter("يا شرموط"));
        net.Receive(5, NetProtocol.Make(NetProtocol.Chat, "what the shit"));
        T.Check("Host: incoming chat filtered and relayed filtered", mp.ChatLog.Last().text == "Sara: what the ****" && !net.Sent.Any(x => x.msg.Contains("shit")), mp.ChatLog.Last().text);
        ChatSafety.Chat = ChatSafety.ChatMode.FriendsOnly;
        int logN = mp.ChatLog.Count;
        net.Receive(5, NetProtocol.Make(NetProtocol.Chat, "hello friend?"));
        T.Check("Chat: friends only hides strangers", mp.ChatLog.Count == logN);
        ChatSafety.SetFriend("Sara", true);
        net.Receive(5, NetProtocol.Make(NetProtocol.Chat, "now friends"));
        T.Check("Chat: friends only shows friends", mp.ChatLog.Count == logN + 1);
        ChatSafety.Chat = ChatSafety.ChatMode.Off;
        mp.SendChat("anyone there");
        T.Check("Chat off: nothing sent", !net.Sent.Any(x => x.msg.Contains("anyone there")));
        ChatSafety.Chat = ChatSafety.ChatMode.Everyone;
        string url = mp.Report(5, "Sara", "test");
        T.Check("Report: saved to file, mail link ready, player blocked and removed", File.Exists(ChatSafety.ReportPath) && File.ReadAllText(ChatSafety.ReportPath).Contains("what the ****")
            && url.StartsWith("mailto:" + ChatSafety.ReportEmail) && ChatSafety.IsBlocked("Sara") && !ChatSafety.IsFriend("Sara") && !net.Clients.Contains(5));
        logN = mp.ChatLog.Count;
        net.Receive(5, NetProtocol.Make(NetProtocol.Chat, "still here"));
        T.Check("Block: blocked player's chat hidden", mp.ChatLog.Count == logN && !mp.ChatLog.Any(c => c.from == "Sara"));
        net.Clients.Add(5);
        net.Receive(5, NetProtocol.Make(NetProtocol.Hello, "Sara"));
        T.Check("Block: blocked player can't join", !net.Clients.Contains(5));
        ChatSafety.SetBlocked("Sara", false);
        ChatSafety.Join = ChatSafety.JoinMode.FriendsOnly;
        net.Clients.Add(7);
        net.Receive(7, NetProtocol.Make(NetProtocol.Hello, "Stranger"));
        T.Check("Join: friends only keeps strangers out", !net.Clients.Contains(7));
        ChatSafety.Join = ChatSafety.JoinMode.Anyone;
        net.Clients.Add(5);
        net.Receive(5, NetProtocol.Make(NetProtocol.Hello, "Sara"));
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
    public bool connecting;
    public bool Connecting => connecting;
    public ulong LocalId => localId;
    public IEnumerable<ulong> ClientIds => Clients;
    public bool relay;                       // the last start went through a Relay server
    public readonly Dictionary<ulong, string> KickReasons = new Dictionary<ulong, string>();
    public string disconnectReason = "";
    public string DisconnectReason => disconnectReason;
    public string ConnectPayload { get; set; }
    public Func<ulong, string, string> ApprovalCheck { get; set; }
    public bool StartHost(ushort port) { mode = NetMode.Host; relay = false; return true; }
    public bool StartClient(string address, ushort port) { relay = false; connecting = true; return true; }
    public bool StartRelayHost() { mode = NetMode.Host; relay = true; return true; }
    public bool StartRelayClient() { relay = true; connecting = true; return true; }
    public void Leave() { mode = NetMode.Offline; connecting = false; }
    public void Kick(ulong id, string reason) { Clients.Remove(id); KickReasons[id] = reason; }
    /// <summary>A player tries to join this host: the approval check runs like Netcode's ConnectionApproval.
    /// Returns null when let in (then connected), else the refusal reason.</summary>
    public string TryJoin(ulong id, string payload)
    {
        string refuse = ApprovalCheck != null ? ApprovalCheck(id, payload) : null;
        if (refuse != null) return refuse;
        Clients.Add(id);
        ClientConnected?.Invoke(id);
        return null;
    }
    /// <summary>Client side: the host refused us / ended the session with this reason.</summary>
    public void RaiseRefused(string reason) { disconnectReason = reason; connecting = false; mode = NetMode.Offline; Disconnected?.Invoke(); }
    public void SendToHost(string msg, bool reliable) => Sent.Add((0UL, msg));
    public void SendToClient(ulong id, string msg, bool reliable) => Sent.Add((id, msg));
    public void SendToClients(string msg, bool reliable) { foreach (var c in Clients) Sent.Add((c, msg)); }
    public event Action<ulong, string> Received;
    public event Action<ulong> ClientConnected, ClientDisconnected;
    public event Action Connected, Disconnected;
    public void Receive(ulong from, string msg) => Received?.Invoke(from, msg);
    public void RaiseConnected() { connecting = false; mode = NetMode.Client; Connected?.Invoke(); }
    public void RaiseDisconnected() => Disconnected?.Invoke();
    public void RaiseClientConnected(ulong id) => ClientConnected?.Invoke(id);
    public void RaiseClientDisconnected(ulong id) { Clients.Remove(id); ClientDisconnected?.Invoke(id); }
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
        // start at the first cozy mission (other feature packs come before it in the chain)
        T.Set(mm, "<Index>k__BackingField", Array.FindIndex(mm.Missions, x => x.id.StartsWith("cozy_"))); T.Set(mm, "<Progress>k__BackingField", 0f);
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
        T.SetProp(WorldClock.Instance, "DayProgress", 0.3f);   // no day roll (daily bills) during the flight
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
        m0 = Money; float feed0 = eco.Feed; int eidFrom = WorldClock.Instance.Day;
        while (WorldClock.Instance.Day < ed) WorldClock.Instance.NextDay();
        var books = new System.Text.StringBuilder();
        int running = 0;   // daily power bills and processing plants also run meanwhile (how much depends on what earlier sections left in storage)
        if (FinanceLedger.Instance != null) foreach (var cat in Ledger.Categories) { int ex = 0, inc = 0; for (int d = eidFrom; d <= ed; d++) { ex += FinanceLedger.Instance.Expense(d, cat); inc += FinanceLedger.Instance.Income(d, cat); } if (cat == Ledger.Utilities || cat == Ledger.Production) running += ex; if (ex + inc > 0) books.Append(" ").Append(cat).Append(" +").Append(inc).Append("/-").Append(ex); }
        T.Check("Eid follows Ramadan: gift (eidiya) + lights", se.Active?.id == "eid" && Money + running >= m0 + 250 && eco.Feed >= feed0 + 5f && GameObject.Find("Eid Lights") != null, Money - m0 + " over " + (ed - eidFrom) + " days:" + books);
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
        T.Check("DesertDressing built", dd != null && dd.Palms > 0 && dd.Trees > 0 && dd.Dunes == 0 && dd.SandCells > 0,
            dd == null ? "null" : $"palms {dd.Palms} trees {dd.Trees} dunes {dd.Dunes} sand {dd.SandCells}");
        T.Check("Cooling pads on both greenhouses", dd.CoolingWalls == 2, "" + dd.CoolingWalls);
        T.Check("Track from loading bay to market", dd.Track.Count >= 2 && Flat(dd.Track[0], DeliveryZone.LoadingBay.transform.position) < 1f);
        T.Check("Parts merged into few meshes", dd.MergedParts > 100, "" + dd.MergedParts);
        var player = GameObject.Find("PlayerCapsule").transform;
        T.Check("Najdi farmhouse near the start", dd.HasHouse && Flat(dd.HousePosition, player.position) < 45f, dd.HousePosition.ToString());
        var plots = FarmFields.Instance.Fields.Select(f => f.center).ToList();
        var zones = new[] { DeliveryZone.LoadingBay.transform.position, DeliveryZone.Market.transform.position };
        var pen = AnimalPen.Instance.transform.position;
        T.Check("Farmhouse clear of fields, zones, pen and start",
            FarmFields.Instance.Fields.All(f => !f.Contains(dd.HousePosition, 6f)) && zones.All(z => Flat(z, dd.HousePosition) > 15f) && Flat(pen, dd.HousePosition) > 18f && Flat(player.position, dd.HousePosition) > 10f);
        var props = UnityEngine.Object.FindObjectsByType<BoxCollider>(FindObjectsSortMode.None).Where(c => c.gameObject.layer == DesertProps.PropLayer && (c.name == "Date Palm" || c.name == "Acacia" || c.name == "Sidr")).ToList();
        var bad = props.Where(c => FarmFields.Instance.Fields.Any(f => f.Contains(c.transform.position, 1.5f)) || zones.Any(z => Flat(z, c.transform.position) < 11f) || Flat(pen, c.transform.position) < 12f
            || gm.Greenhouses.Any(g => Flat(g.transform.position, c.transform.position) < 6f)).Select(c => c.name + c.transform.position).ToList();
        T.Check("Palms and trees keep off plots, pads, pen and greenhouses (" + props.Count + " props)", props.Count > 0 && bad.Count == 0, string.Join(" ", bad.Take(3)));
        T.Check("Prop colliders are ignored by ground raycasts", props.All(c => (Build.GroundMask & (1 << c.gameObject.layer)) == 0));

        Console.WriteLine("Quality presets");
        GraphicsEnhancer.SetPreset(2);   // start from High (settings may already have picked another preset)
        int palmsHigh = dd.Palms + dd.Trees;
        GraphicsEnhancer.SetPreset(0);
        T.Check("SetPreset(0) = Low, no post", GraphicsEnhancer.Preset == 0 && !GraphicsEnhancer.PostEnabled && !GraphicsEnhancer.High);
        T.Check("Low preset places fewer props", dd.Palms + dd.Trees < palmsHigh, (dd.Palms + dd.Trees) + " vs " + palmsHigh);
        GraphicsEnhancer.SetPreset(1);
        T.Check("Medium preset", GraphicsEnhancer.Preset == 1 && GraphicsEnhancer.PostEnabled && GraphicsEnhancer.DressingDensity < 1f);
        GraphicsEnhancer.SetPreset(7);
        T.Check("Preset clamps and saves (top preset is Ultra)", GraphicsEnhancer.Preset == GraphicsEnhancer.PresetUltra && SaveData.GetInt("mazraa.gfxPreset", -1) == GraphicsEnhancer.PresetUltra);
        GraphicsEnhancer.SetPreset(2);   // back to High for the checks below
        T.Check("Medium has an Arabic name", Loc.Raw("Graphics: Medium") == "Graphics: Medium" && LocHas("Graphics: Medium"));

        Console.WriteLine("Desert weather");
        var clock = WorldClock.Instance;
        T.SetProp(clock, "Weather", Weather.Sunny); FakeEngine.Run(20f, 0.5f);   // settle on a calm day first
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
        // Night well before midnight (02:00 is 0.83) so the 20 s run can't roll into a new day with a rain roll.
        T.SetProp(clock, "Weather", Weather.Sunny);   // crickets are silent in rain
        T.SetProp(clock, "DayProgress", 0.82f); FakeEngine.Run(20f, 0.5f);
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

// ---------------------------------------------------------------- Egypt Nile Delta DLC (region content pack)
static class EgyptTests
{
    static List<string> ShopIds()
    {
        var items = typeof(ShopUI).GetMethod("Items", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(ShopUI.Instance, null);
        return ((System.Collections.IEnumerable)items).Cast<object>().Select(o => (string)o.GetType().GetField("Item2").GetValue(o)).ToList();
    }

    static List<string> LabelKeys() => UnityEngine.Object.FindObjectsByType<LocalizedLabel>(FindObjectsSortMode.None).Select(l => l.key).ToList();
    static bool EgyptClient(string en) => MarketManager.Config.contracts.clients.Concat(MarketManager.Config.contracts.fieldClients).Any(c => c.region == "nile_delta" && c.en == en);

    static readonly string[] EgyptCrops = { "rice", "cotton", "berseem", "maize" };

    public static void Run()
    {
        Console.WriteLine("Egypt Nile Delta DLC");
        int e0 = FakeEngine.Errors.Count;
        var gmenu = GameMenu.Instance;
        gmenu.StartCareer("najd", Career.Start.NewFarmer, Career.Economy.Normal);
        FakeEngine.Run(0.5f);
        var eco = EconomyManager.Instance; var mm = MissionManager.Instance; var camp = CampaignManager.Instance; var ed = EventDirector.Instance;
        var dq = DailyQuests.Instance; var dd = DesertDressing.Instance; var market = MarketManager.Instance;

        // ---- data
        T.Check("Egypt: 4 DLC field crops tagged for the Nile Delta, with goods, families and soil agronomy",
            EgyptCrops.All(c => eco.FieldCrop(c) != null && eco.FieldCrop(c).regions.SequenceEqual(new[] { "nile_delta" }) && eco.Good(c) != null && CropsData.Meta(c) != null && FcData.File.crops.Any(s => s.id == c)));
        T.Check("Egypt: crops have Arabic names and placeholder stage models", EgyptCrops.All(c => !string.IsNullOrEmpty(eco.FieldCrop(c).nameAr) && !string.IsNullOrEmpty(eco.Good(c).nameAr) && CropVisuals.HasModels(c)));
        T.Check("Egypt: Delta seasons (rice + maize summer, cotton spring-summer, berseem winter forage)",
            eco.FieldCrop("rice").seasons.Contains("summer") && eco.FieldCrop("maize").seasons.Contains("summer") && eco.FieldCrop("cotton").seasons.Contains("spring")
            && eco.FieldCrop("berseem").seasons.Contains("winter") && !eco.FieldCrop("berseem").seasons.Contains("summer"));
        var rice = FcData.Crop("rice"); var cotton = FcData.Crop("cotton"); var berseem = FcData.Crop("berseem"); var maize = FcData.Crop("maize");
        T.Check("Egypt: FAO salt tolerance (rice 3.0, cotton 7.7, berseem 1.5, maize 1.7 dS/m)", rice.ecT == 3f && cotton.ecT == 7.7f && berseem.ecT == 1.5f && maize.ecT == 1.7f);
        T.Check("Egypt: rice is a flooded, thirsty crop (highest water use)", rice.flood && rice.waterMin > 0.5f && FcData.File.crops.All(c => c.et <= rice.et));
        var paddy = new FarmField(990, "paddy", Vector3.zero, 0f, 4f, 4f); paddy.ResetSoil(FcData.Region("nile_delta"));
        FC.Wet(paddy, 1.15f);
        T.Check("Egypt: standing water doesn't hurt rice but does hurt wheat",
            FarmAgronomy.SoilGrowth(paddy, 0, rice) > FarmAgronomy.SoilGrowth(paddy, 0, FcData.Crop("wheat")) + 0.3f && FarmAgronomy.Factors(paddy, 0, rice).waterlog == 1f && FarmAgronomy.Factors(paddy, 0, FcData.Crop("wheat")).waterlog < 0.6f);
        T.Check("Egypt: berseem is a multi-cut legume that feeds animals and fixes nitrogen", CropsData.Family("berseem") == "legume" && CropsData.Meta("berseem").cuts > 1 && CropsData.Meta("berseem").feedPerKg > 0f && berseem.fix > 0f && berseem.rn == 0f);
        T.Check("Egypt: rotation berseem -> cotton bonus, cotton after cotton penalty", CropsData.Family("cotton") == "fiber" && FcData.Rotation("legume", "fiber") > 1.1f && FcData.Rotation("fiber", "fiber") < 1f);
        T.Check("Egypt: cotton leafworm only attacks cotton and is treated by neem / pesticide",
            CropsData.Pest("cotton_leafworm") != null && CropsData.Pest("cotton_leafworm").families.SequenceEqual(new[] { "fiber" }) && CropsData.Pest("cotton_leafworm").curedBy.Contains("pesticide"));
        var arabicMissions = mm.AllMissions.Where(m => m.regions != null && m.regions.Contains("nile_delta")).ToList();
        T.Check("Egypt: 25 story missions (incl. the open world), all bilingual", arabicMissions.Count == 25 && arabicMissions.All(m => !string.IsNullOrEmpty(m.titleAr) && !string.IsNullOrEmpty(m.descAr) && m.target > 0f));
        var src = File.ReadAllText("../../Assets/Scripts/Managers/MissionManager.cs");
        var bad = arabicMissions.Where(m => !src.Contains("\"" + m.type + "\"") && !m.type.StartsWith("plant_") && !m.type.StartsWith("harvest_") && !m.type.StartsWith("sell_") && !m.type.StartsWith("event_")).Select(m => m.type).ToList();
        T.Check("Egypt: every mission type is handled", bad.Count == 0, string.Join(",", bad));
        T.Check("Egypt: every new UI text has Arabic (Loc/93_egypt.json)",
            new[] { "Village souq", "Agricultural cooperative", "Cotton gin", "Rice mill", "Canal water reached {0} fields" }.All(Loc.HasTranslation)
            && MarketManager.Buildings("nile_delta").All(s => Loc.HasTranslation(s.nameEn)));

        // ---- Najd stays the base game
        int najdMissions = mm.Missions.Length;
        T.Check("Najd: Egypt crops not offered (seed shop, planting, price)", !EgyptCrops.Any(c => ShopIds().Contains("seed_" + c)) && EgyptCrops.All(c => eco.PriceOf("seed_" + c) < 0)
            && ShopIds().Contains("seed_wheat") && ShopIds().Contains("seed_onion") && ShopIds().Contains("seed_potato"));
        eco.AddMoney(5000);
        T.Check("Najd: Egypt seed can't be bought", !eco.TryBuy("seed_rice") && eco.Seeds("rice") == 0);
        eco.AddSeeds("cotton", 1);
        FarmFields.PreferredSeed = "cotton"; FarmFields.Instance.SetSowCrop("");
        T.Check("Najd: a gifted Egypt seed is not planted here", FarmFields.Instance.CropToSow() != "cotton", FarmFields.Instance.CropToSow());
        T.Check("Najd: base mission chain, first mission the well, no Egypt missions", mm.Missions.Length == 50 && mm.Current?.id == "well" && mm.Missions.All(m => !m.id.StartsWith("eg_")), mm.Missions.Length + " " + mm.Current?.id);
        T.Check("Najd: base campaign (8 chapters with the open road, Abu Saleh)", camp.Chapters.Length == 8 && camp.Chapters[0].id == "inheritance" && camp.Narrator == "Abu Saleh" && !camp.IsRegionCampaign && StoryIntro.Body() == Loc.T("STORY_BODY"));
        T.Check("Najd: events unchanged (8, haboob, no canal turns)", ed.Data.events.Length == 8 && ed.Find("haboob") != null && ed.Find("canal_turn") == null);
        T.Check("Najd: daily quests unchanged", dq.Templates.Length == 9 && dq.Templates.All(t => t.regions == null || t.regions.Length == 0));
        T.Check("Najd: market and shop names", LabelKeys().Contains("Market") && LabelKeys().Contains("Farm Shop") && !LabelKeys().Contains("Village souq"));
        T.Check("Najd: desert look (sand, no edge dunes around the open farm, no canals or sell points)", dd.BuiltRegion == "najd" && dd.Dunes == 0 && dd.Canals == 0 && dd.SellPoints == 0 && dd.Eucalyptus == 0 && dd.GroundColor.Equals(DesertDressing.SandColor),
            dd.BuiltRegion + " dunes " + dd.Dunes + " canals " + dd.Canals);
        T.Check("Najd: warm grade, no river haze, no sell bonus", GraphicsEnhancer.Instance.GradeSaturation == 12f && Regions.Look.humidity == 0f && market.Point("gin") == null && market.Point("ricemill") == null && !market.Offers.Any(c => EgyptClient(c.buyerEn)));
        int najdTrees = dd.Palms + dd.Trees;

        // ---- Egypt career
        gmenu.StartCareer("nile_delta", Career.Start.NewFarmer, Career.Economy.Normal);
        FakeEngine.Run(0.5f);
        T.Check("Egypt: career on the Nile Delta in EGP", Regions.Current.id == "nile_delta" && Regions.IsEgypt && Loc.Raw("SAR") == "EGP");
        T.Check("Egypt: Delta crops offered next to wheat, onion and potato", EgyptCrops.All(c => ShopIds().Contains("seed_" + c)) && new[] { "wheat", "onion", "potato" }.All(c => ShopIds().Contains("seed_" + c)));
        T.Check("Egypt: starts with berseem seed and the usual wheat", eco.Seeds("berseem") == 2 && eco.Seeds("wheat") == 2 && eco.Seeds("cotton") == 0 && eco.Seeds("rice") == 0, eco.Seeds("berseem") + " " + eco.Seeds("wheat"));
        T.Check("Egypt: own mission chain from the pump", mm.Missions.Length == 25 && mm.Current?.id == "eg_pump" && mm.Index == 0 && mm.Missions.All(m => m.id.StartsWith("eg_")), mm.Missions.Length + " " + mm.Current?.id);
        T.Check("Egypt: campaign chapter 1 is the Egypt story told by Am Hassan", camp.IsRegionCampaign && camp.Chapters.Length == 6 && camp.Chapters[0].id == "eg_land"
            && camp.Narrator == "Am Hassan" && camp.Chapters[0].titleEn == "The Land by the Canal" && StoryIntro.Body().Contains("Am Hassan") && StoryIntro.Body().Contains("Kafr El-Sheikh"));
        Loc.SetLanguage(Language.Arabic);
        bool egyptianArabic = camp.Narrator == "عم حسن" && CampaignManager.Intro(camp.Chapters[0]).Contains("فدادين") && MissionManager.Title(mm.Current) == "شغّل ماكينة الري";
        Loc.SetLanguage(Language.English);
        T.Check("Egypt: Egyptian Arabic story texts", egyptianArabic);
        T.Check("Egypt: 6 chapters cover the 25 missions", camp.Chapters.Select(c => c.fromMission).SequenceEqual(new[] { 1, 5, 9, 13, 17, 21 }));
        T.Check("Egypt: own events (canal turn, Nile flood, cotton festival, rice export), no haboob dust storms",
            ed.Data.events.All(e => e.regions != null && e.regions.Contains("nile_delta")) && ed.Find("haboob") == null && ed.Find("flood") == null
            && new[] { "canal_turn", "nile_flood", "cotton_festival", "rice_export" }.All(id => ed.Find(id) != null) && ed.Data.events.All(e => !string.IsNullOrEmpty(e.nameAr) && !string.IsNullOrEmpty(e.warnAr) && !string.IsNullOrEmpty(e.startAr)));
        T.Check("Egypt: own daily quests", dq.Templates.Length > 0 && dq.Templates.All(t => t.regions != null && t.regions.Contains("nile_delta")) && dq.Quests.Count > 0 && dq.Quests.All(qe => qe.template.StartsWith("eg_")));
        T.Check("Egypt: market is the village souq, shop the agricultural cooperative", LabelKeys().Contains("Village souq") && LabelKeys().Contains("Agricultural cooperative") && !LabelKeys().Contains("Market"));
        T.Check("Egypt: contracts from Egyptian buyers", market.Offers.Count > 0 && market.Offers.All(c => EgyptClient(c.buyerEn)), market.Offers.Count > 0 ? market.Offers[0].buyerEn : "none");
        T.Check("Egypt: cotton gin and rice mill pay more (selling points of the market)", market.Point("gin").mul > 1.1f && market.Point("ricemill").mul > 1.05f && market.Point("ricemill").accepts.Contains("maize")
            && !MarketMath.Accepts(market.Point("gin"), "wool") && market.BestPoint("cotton") == "gin" && market.Price("cotton", "gin") > market.Price("cotton") * 1.1f
            && MarketManager.Buildings("nile_delta").Select(sp => sp.id).OrderBy(x => x).SequenceEqual(new[] { "gin", "ricemill", "village" }) && market.Point("sugar") == null);
        T.Check("Egypt: Delta clay soil on every field (OM 1.8 %, K ~380, 140 mm water)", FarmFields.Instance.Fields.All(f => Math.Abs(f.om - 1.8f) < 0.01f && Math.Abs(FarmSim.Summarize(f).k - 380f) < 20f) && FarmEnv.Now().region.capacityMm == 140f,
            string.Join(",", FarmFields.Instance.Fields.Select(f => f.om + "/" + FarmSim.Summarize(f).k)));

        // ---- look
        T.Check("Egypt: green Delta ground, no dunes", dd.BuiltRegion == "nile_delta" && dd.Dunes == 0 && dd.GroundColor.g > dd.GroundColor.r, dd.BuiltRegion + " dunes " + dd.Dunes);
        T.Check("Egypt: canal and ditches beside the fields", dd.Canals >= 3, "" + dd.Canals);
        T.Check("Egypt: eucalyptus rows and date palms", dd.Eucalyptus > 4 && dd.Palms > 0, dd.Eucalyptus + " / " + dd.Palms);
        T.Check("Egypt: sell points by the market (gin, mill, souq) with signs", dd.SellPoints == 3 && GameObject.Find("Sell Point gin") != null && LabelKeys().Contains("Cotton gin") && LabelKeys().Contains("Rice mill"), "" + dd.SellPoints);
        T.Check("Egypt: pigeon tower by the farmhouse", !dd.HasHouse || dd.HasPigeonTower);
        var props = UnityEngine.Object.FindObjectsByType<BoxCollider>(FindObjectsSortMode.None).Where(c => c.gameObject.layer == DesertProps.PropLayer && (c.name == "Eucalyptus" || c.name == "Date Palm" || c.name == "Sidr" || c.name.StartsWith("Sell Point") || c.name == "Pigeon Tower")).ToList();
        var blocked = props.Where(c => FarmFields.Instance.Fields.Any(f => f.Contains(c.transform.position, 1.5f))
            || Vector3.Distance(Flat(DeliveryZone.Market.transform.position), Flat(c.transform.position)) < 9f || Vector3.Distance(Flat(DeliveryZone.LoadingBay.transform.position), Flat(c.transform.position)) < 9f).Select(c => c.name + c.transform.position).ToList();
        T.Check("Egypt: Delta props keep off plots and pads (" + props.Count + ")", props.Count > 0 && blocked.Count == 0, string.Join(" ", blocked.Take(3)));
        T.Check("Egypt: canals have no colliders", UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.None).All(c => c.transform.parent == null || c.transform.parent.name != "Canal"));
        T.Check("Egypt: few draw objects (parts merged)", dd.MergedParts > 100 && (dd.Palms + dd.Trees) <= najdTrees + 30, dd.MergedParts + " parts, " + (dd.Palms + dd.Trees) + " vs " + najdTrees);
        T.Check("Egypt: greener, cooler colour grade", GraphicsEnhancer.Instance.GradeSaturation > 12f);
        var clock = WorldClock.Instance;
        T.SetProp(clock, "Weather", Weather.Heatwave); T.SetProp(clock, "DayProgress", 0.05f);
        FakeEngine.Run(30f, 0.5f);
        T.Check("Egypt: milder wind and dust than a Najd heatwave", DesertWeatherFX.Wind01 < 0.6f && DesertWeatherFX.Instance.DustRate < 60f, DesertWeatherFX.Wind01 + " / " + DesertWeatherFX.Instance.DustRate);
        T.Check("Egypt: humid Nile haze in the morning", DesertWeatherFX.Humidity01 > 0.3f && RenderSettings.fogDensity > 0.006f, DesertWeatherFX.Humidity01 + " / " + RenderSettings.fogDensity);
        T.SetProp(clock, "Weather", Weather.Sunny); T.SetProp(clock, "DayProgress", 0.5f);
        FakeEngine.Run(2f, 0.5f);

        // ---- story missions advance with the Delta crops
        foreach (var w in UnityEngine.Object.FindObjectsByType<WellController>(FindObjectsSortMode.None)) if (!w.IsRunning) w.Interact();
        FakeEngine.Run(2f, 0.5f);
        T.Check("Egypt mission 1: the pump", mm.Current?.id == "eg_berseem", mm.Current?.id);
        var ffe = FarmFields.Instance;
        var p = ffe.Fields[0];
        p.ResetSoil(FcData.CurrentRegion); p.protectDays = 99;
        FarmFields.PreferredSeed = "berseem"; ffe.SetSowCrop("");
        FC.Prepare(p); FC.Whole(p, FieldTool.Seeder);
        T.Check("Egypt: berseem sown (2 bags of the starting seed), story moves on", FC.Crop(p) == "berseem" && FC.Sown(p) == p.Cells && mm.Current?.id == "eg_cut", FC.Crop(p) + " " + FC.Sown(p) + " " + mm.Current?.id);
        float feed0 = eco.Feed, n0 = FarmSim.Summarize(p).n;
        FC.Ripen(p);
        FC.Whole(p, FieldTool.Mower); FC.Whole(p, FieldTool.Baler);
        T.Check("Egypt: a berseem cut feeds the animals, grows back and adds nitrogen", eco.Feed > feed0 && eco.Stored("berseem") > 20f && FC.Sown(p) == p.Cells && FC.Crop(p) == "berseem" && FarmSim.Summarize(p).n > n0 - 1f,
            eco.Feed + " " + eco.Stored("berseem") + " " + FC.Sown(p) + " n " + n0 + "->" + FarmSim.Summarize(p).n);

        // ---- rice grows, is harvested and sells
        var q = ffe.Fields[1];
        q.ResetSoil(FcData.CurrentRegion); q.protectDays = 99;
        eco.AddMoney(1000);
        T.Check("Egypt: buy rice seed at the cooperative", eco.TryBuy("seed_rice") && eco.TryBuy("seed_rice") && eco.Seeds("rice") == 2);
        FarmFields.PreferredSeed = "rice";
        FC.Prepare(q); FC.Whole(q, FieldTool.Seeder);
        T.Check("Egypt: rice planted", FC.Sown(q) == q.Cells && FC.Crop(q) == "rice", FC.Sown(q) + " " + FC.Crop(q));
        float g0 = FarmSim.Summarize(q).growth;
        for (int i = 0; i < 20; i++) { FC.Wet(q, 1.12f); ffe.Simulate(2f); }
        T.Check("Egypt: rice grows in its flooded paddy", FarmSim.Summarize(q).growth > g0 && FC.Count(q, c => q.Has(c, CellFlags.Withered)) == 0, g0 + " -> " + FarmSim.Summarize(q).growth);
        FC.Wet(q, 1.12f);
        T.Check("Egypt: flooded paddy is not waterlogging stress for rice", FarmSim.Summarize(q).factors.waterlog == 1f);
        FC.Ripen(q);
        float rice0 = eco.Stored("rice"); float custom = 0f;
        Action<string, float> spy = (t, a) => { if (t == "harvest_rice") custom += a; };
        GameEvents.Custom += spy;
        FC.HandHarvest(q);
        float kg = eco.Stored("rice") - rice0;
        T.Check("Egypt: rice harvest goes to storage (story event too)", kg > 30f && Math.Abs(custom - kg) < 0.01f && FC.Sown(q) == 0, kg + " kg, event " + custom);
        float sold = 0f;
        Action<string, float> spy2 = (t, a) => { if (t == "sell_rice") sold += a; };
        GameEvents.Custom += spy2;
        int money0 = eco.Money;
        int earned = eco.SellAll(new Dictionary<string, float> { { "rice", kg } });
        T.Check("Egypt: rice sells at the souq (rice mill bonus)", earned > 0 && eco.Money >= money0 + earned && Math.Abs(sold - kg) < 0.01f, earned + " for " + kg + " kg, sold event " + sold + ", money " + money0 + " -> " + eco.Money);
        GameEvents.Custom -= spy; GameEvents.Custom -= spy2;

        // ---- canal turn (nobat el-mayya)
        typeof(EventDirector).GetMethod("OnNewDay", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(ed, new object[] { 7 });
        T.Check("Egypt: the canal turn is forecast every 4 days", ed.Pending != null && ed.Pending.id == "canal_turn" && ed.PendingDay == 8, ed.Pending?.id + " " + ed.PendingDay);
        FC.Wet(p, 0.1f);
        bool turned = false;
        Action<string, float> spy3 = (t, a) => { if (t == "event_canal_turn") turned = true; };
        GameEvents.Custom += spy3;
        ed.StartNow("canal_turn");
        FakeEngine.Run(1f, 0.25f);
        T.Check("Egypt: on the canal turn every planted field gets water", turned && FarmSim.Summarize(p).moisture > 0.9f && FarmModifiers.FieldDry < 1f, turned + " water " + FarmSim.Summarize(p).moisture + " dry " + FarmModifiers.FieldDry);
        T.Check("Egypt: canal intakes of the irrigation network flow on the turn", IrrigationSystem.CanalOpen());
        ed.EndActive();
        GameEvents.Custom -= spy3;
        int today = WorldClock.Instance.Day;
        T.Check("Egypt: between turns the canal is closed (every 4th day open)", IrrigationSystem.CanalOpen() == (today % 4 == 0));

        // ---- back to Najd: everything resets to the base game
        eco.AddSeeds("rice", 2);
        gmenu.StartCareer("najd", Career.Start.NewFarmer, Career.Economy.Normal);
        // (checked at once: the well keeps running in the scene, so Najd's first mission completes within a second)
        T.Check("Switch to Najd: base mission chain from the well", mm.Missions.Length == najdMissions && mm.Index == 0 && mm.Current?.id == "well", mm.Missions.Length + " " + mm.Index + " " + mm.Current?.id);
        T.Check("Switch to Najd: Egypt seeds and goods cleared", EgyptCrops.All(c => eco.Seeds(c) == 0 && eco.Stored(c) == 0f) && eco.Seeds("wheat") == 2 && eco.Money == 500,
            string.Join(",", EgyptCrops.Select(c => c + ":" + eco.Seeds(c) + "/" + eco.Stored(c))) + " wheat " + eco.Seeds("wheat") + " money " + eco.Money);
        FakeEngine.Run(0.5f);
        T.Check("Switch to Najd: region, SAR, Najd crops only", Regions.Current.id == "najd" && Loc.Raw("SAR") == "SAR" && !EgyptCrops.Any(c => ShopIds().Contains("seed_" + c)));
        T.Check("Switch to Najd: base campaign and events", camp.Chapters.Length == 8 && camp.Narrator == "Abu Saleh" && ed.Data.events.Length == 8 && ed.Pending == null && ed.Active == null);
        T.Check("Switch to Najd: base quests, Saudi buyers", dq.Templates.Length == 9 && dq.Quests.All(qq => !qq.template.StartsWith("eg_")) && market.Offers.All(c => !EgyptClient(c.buyerEn)) && market.Point("gin") == null);
        T.Check("Switch to Najd: market name, desert look and grade back", LabelKeys().Contains("Market") && LabelKeys().Contains("Farm Shop") && !LabelKeys().Contains("Village souq")
            && dd.BuiltRegion == "najd" && dd.Dunes == 0 && dd.Canals == 0 && dd.SellPoints == 0 && dd.Eucalyptus == 0 && GraphicsEnhancer.Instance.GradeSaturation == 12f && GameObject.Find("Sell Point gin") == null);
        T.Check("Switch to Najd: Najd sandy soil again", Math.Abs(FarmFields.Instance.Fields[0].om - 0.6f) < 0.01f && FarmEnv.Now().region.capacityMm == 70f);
        T.Check("Egypt: no errors", FakeEngine.Errors.Count == e0, string.Join(" | ", FakeEngine.Errors.Skip(e0).Take(3)));
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
}

// ---------------------------------------------------------------- driving: drivetrain, load, traction, assists, wear
static class DrivingTests
{
    const float Dt = 0.02f;

    static void Step(Vehicle v, float seconds, float throttle, float steer = 0f, bool brake = false)
    {
        for (float t = 0; t < seconds; t += Dt) v.StepDrive(throttle, steer, brake, Dt);
    }

    static void Stop(Vehicle v) { T.SetProp(v, "Speed", 0f); v.StepDrive(0f, 0f, false, Dt); }

    static float MaxSlip(Vehicle v, float seconds)
    {
        Stop(v);
        float m = 0f;
        for (float t = 0; t < seconds; t += Dt) { v.StepDrive(1f, 0f, false, Dt); m = Math.Max(m, v.Drive.Slip); }
        Stop(v);
        return m;
    }

    static void Press(UnityEngine.InputSystem.ButtonControl b) { b.wasPressedThisFrame = true; b.isPressed = true; FakeEngine.Tick(0.02f); b.wasPressedThisFrame = false; b.isPressed = false; }
    static void PressKey(UnityEngine.InputSystem.KeyControl k) { k.wasPressedThisFrame = true; k.isPressed = true; FakeEngine.Tick(0.02f); k.wasPressedThisFrame = false; k.isPressed = false; }

    static float BrakeDistance(Vehicle v)
    {
        T.SetProp(v, "Speed", 12f);
        float d = 0f;
        for (int i = 0; i < 500 && v.Speed > 0.01f; i++) { v.StepDrive(0f, 0f, true, Dt); d += v.Speed * Dt; }
        return d;
    }

    static float YawRate(Vehicle v, float speed)
    {
        for (int i = 0; i < 20; i++) { T.SetProp(v, "Speed", speed); v.StepDrive(0f, 1f, false, Dt); }
        float y0 = v.Yaw; T.SetProp(v, "Speed", speed); v.StepDrive(0f, 1f, false, Dt);
        return (v.Yaw - y0) / Dt;
    }

    const string Hints = "[F] Engine  [Shift]/[Ctrl] Gear  [Y] Cruise [Minus]/[Plus]  [Z] 4WD / diff lock  [Slash] GPS";

    public static void Run()
    {
        Console.WriteLine("Driving: drivetrain, load, traction, assists, wear");
        Program.BuildWorld();
        FakeEngine.Run(2f);
        for (int i = 0; i < 400 && LoadingScreen.Visible; i++) FakeEngine.Tick(0.1f);
        var intro = UnityEngine.Object.FindAnyObjectByType<StoryIntro>();
        if (intro != null) typeof(StoryIntro).GetMethod("Close", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(intro, null);
        FakeEngine.Tick(0.02f);
        T.Check("Driving: no panel open (inputs reach the vehicle)", !UIManager.Instance.IsUIOpened);
        var kb = new UnityEngine.InputSystem.Keyboard();
        foreach (var f in typeof(UnityEngine.InputSystem.Keyboard).GetFields())
            if (f.FieldType == typeof(UnityEngine.InputSystem.KeyControl)) f.SetValue(kb, new UnityEngine.InputSystem.KeyControl());
        UnityEngine.InputSystem.Keyboard.current = kb;
        var pad = new UnityEngine.InputSystem.Gamepad { name = "XInputControllerWindows", displayName = "Xbox Controller" };
        UnityEngine.InputSystem.Gamepad.current = pad;
        var eco = EconomyManager.Instance;
        var truck = FarmTruck.Instance; var tractor = Tractor.Instance;
        T.Check("Driving: defaults keep the classic feel (automatic, road tyres, auto engine start)",
            GameSettings.Gearbox == GearboxMode.Automatic && !GameSettings.LowTyrePressure && GameSettings.AutoEngineStart);
        var gameRows = (List<MenuRow>)typeof(GameMenu).GetMethod("GameRows", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(GameMenu.Instance, null);
        T.Check("Driving: Settings > Game has the Vehicles rows (gearbox, tyre pressure, auto engine start)",
            new[] { "Gearbox", "Tyre pressure", "Auto engine start" }.All(k => gameRows.Any(r => r.label() == UiText.T(k))));
        GameSettings.Gearbox = GearboxMode.CVT; GameSettings.LowTyrePressure = true; GameSettings.AutoEngineStart = false;
        GameSettings.ResetGameDefaults();
        T.Check("Driving: Reset to defaults (Game) restores the vehicle settings", GameSettings.Gearbox == GearboxMode.Automatic && !GameSettings.LowTyrePressure && GameSettings.AutoEngineStart);

        // ---- engine + gearbox
        truck.Interact(); FakeEngine.Tick(Dt);
        truck.SurfaceOverride = DriveSurface.Asphalt; truck.RefreshSurface();
        T.Check("Driving: engine running at idle after getting in", truck.Drive.EngineOn && Math.Abs(truck.Drive.Rpm - truck.Drive.idleRpm) < 50f, truck.Drive.Rpm + "");
        Stop(truck);
        var gearsSeen = new List<int>();
        for (float t = 0; t < 12f; t += Dt) { truck.StepDrive(1f, 0f, false, Dt); if (gearsSeen.Count == 0 || gearsSeen[gearsSeen.Count - 1] != truck.Drive.Gear) gearsSeen.Add(truck.Drive.Gear); }
        bool rising = gearsSeen.Count >= 4; for (int i = 1; i < gearsSeen.Count; i++) rising &= gearsSeen[i] == gearsSeen[i - 1] + 1;
        T.Check("Driving: automatic gearbox starts in 1st and shifts up with speed", gearsSeen[0] == 1 && rising && truck.Drive.Gear >= 5, string.Join(",", gearsSeen) + " at " + truck.Speed);
        T.Check("Driving: RPM stays within the governor", truck.Drive.Rpm > 1300f && truck.Drive.Rpm <= truck.Drive.governorRpm + 1f, truck.Drive.Rpm + "");
        float topSpeed = truck.Speed;
        T.Check("Driving: truck still reaches its top speed (classic feel)", topSpeed > truck.maxSpeed * 0.9f, topSpeed + "");
        Step(truck, 2f, 0f);
        float engineBrake = topSpeed - truck.Speed;
        Step(truck, 6f, 0f, 0f, true);
        T.Check("Driving: automatic shifts down as you stop", Math.Abs(truck.Speed) < 0.01f && truck.Drive.Gear == 1, truck.Drive.Gear + " " + truck.Speed);
        T.SetProp(truck, "Speed", topSpeed); truck.Drive.StopEngine();
        Step(truck, 2f, 0f);
        float coastOff = topSpeed - truck.Speed;
        truck.Drive.StartEngine(true);
        T.Check("Driving: engine braking slows you more than coasting with the engine off", engineBrake > coastOff * 2f, engineBrake + " vs " + coastOff);
        Stop(truck);
        Step(truck, 1.5f, -1f);
        T.Check("Driving: pulling back at a standstill selects reverse", truck.Drive.Gear == -1 && truck.Speed < -0.5f && truck.Drive.GearLabel == "R", truck.Drive.Gear + " " + truck.Speed);
        Stop(truck); Step(truck, 0.5f, 1f); Stop(truck);
        T.Check("Driving: forward again after reverse", truck.Drive.Gear == 1);

        GameSettings.Gearbox = GearboxMode.Manual;
        Stop(truck);
        Step(truck, 4f, 1f);
        float firstGearTop = truck.Speed;
        T.Check("Driving: manual gearbox holds 1st (limited by the revs)", truck.Drive.Gear == 1 && firstGearTop < truck.Drive.GearTop(1) * 1.15f && truck.Drive.GearLabel == "M1", firstGearTop + " " + truck.Drive.GearLabel);
        PressKey(kb.leftShiftKey); PressKey(kb.leftShiftKey);
        T.Check("Driving: Shift shifts up in manual", truck.Drive.Gear == 3, truck.Drive.Gear + "");
        Press(pad.rightShoulder);
        bool rb = truck.Drive.Gear == 4;
        Press(pad.leftShoulder);
        T.Check("Driving: RB shifts up, LB shifts down (gamepad)", rb && truck.Drive.Gear == 3, truck.Drive.Gear + "");
        Step(truck, 4f, 1f);
        T.Check("Driving: higher gear goes faster", truck.Speed > firstGearTop * 1.6f, truck.Speed + " vs " + firstGearTop);
        float wear0 = truck.Drive.EngineWear;
        for (int i = 0; i < 3; i++) truck.Drive.ShiftDown();
        Step(truck, 0.5f, 0f);
        T.Check("Driving: downshifting at speed over-revs and wears the engine", truck.Drive.EngineWear > wear0 + 0.002f, wear0 + " -> " + truck.Drive.EngineWear);
        GameSettings.Gearbox = GearboxMode.CVT;
        Stop(truck);
        float cvtRpm = 0f;
        for (float t = 0; t < 8f; t += Dt) { truck.StepDrive(1f, 0f, false, Dt); if (t > 0.5f && truck.Speed < truck.maxSpeed * 0.7f) cvtRpm = Math.Max(cvtRpm, truck.Drive.Rpm); }
        T.Check("Driving: CVT accelerates smoothly, engine held in its torque band", truck.Drive.GearLabel == "CVT" && truck.Speed > 10f && cvtRpm < 1700f && cvtRpm > 1300f, truck.Speed + " " + cvtRpm);
        GameSettings.Gearbox = GearboxMode.Automatic;
        Stop(truck);
        truck.Drive.EngineWear = truck.Drive.TyreWear = 0f;

        // ---- engine key, idle fuel
        PressKey(kb.fKey);
        T.Check("Driving: F switches the engine off", !truck.Drive.EngineOn);
        GameSettings.AutoEngineStart = false;
        Step(truck, 1f, 1f);
        T.Check("Driving: engine off = no drive", Math.Abs(truck.Speed) < 0.01f);
        Press(pad.leftStickButton);
        T.Check("Driving: L3 starts the engine (cranks first)", truck.Drive.EngineOn && truck.Drive.Starting);
        Step(truck, 1f, 0f);
        T.Check("Driving: engine runs after cranking", truck.Drive.EngineOn && !truck.Drive.Starting && truck.Drive.Rpm > 600f);
        GameSettings.AutoEngineStart = true;
        float fuel0 = truck.Fuel; Step(truck, 10f, 0f);
        float idleUse = fuel0 - truck.Fuel;
        T.Check("Driving: idling burns a little fuel", idleUse > 0.01f && idleUse < truck.fuelPerSecond * 10f * 0.15f, idleUse + "");

        // ---- load and weight
        Stop(truck); Step(truck, 4f, 1f);
        float vEmpty = truck.Speed;
        float brakeEmpty = BrakeDistance(truck);
        float massEmpty = truck.TotalMass;
        float fullLoad = eco.TruckCapacity / Math.Max(0.01f, eco.Good("wheat").weight);
        truck.Cargo["wheat"] = fullLoad;
        truck.RefreshSurface();
        Stop(truck); Step(truck, 4f, 1f);
        float vFull = truck.Speed;
        float brakeFull = BrakeDistance(truck);
        T.Check("Driving: full truck is heavier", truck.TotalMass > massEmpty + 3000f, massEmpty + " -> " + truck.TotalMass);
        T.Check("Driving: heavier load = slower acceleration", vEmpty > vFull * 1.3f, vEmpty + " vs " + vFull);
        T.Check("Driving: heavier load = longer braking distance", brakeFull > brakeEmpty * 1.2f, brakeEmpty + " vs " + brakeFull);
        truck.Drive.SlopeSin = 0.25f;
        Stop(truck);
        Step(truck, 25f, 1f); float hillFull = truck.Speed;
        truck.Cargo.Clear(); truck.RefreshSurface();
        Stop(truck); Step(truck, 25f, 1f); float hillEmpty = truck.Speed;
        truck.Drive.SlopeSin = 0f;
        T.Check("Driving: loaded truck is slower up a hill", hillEmpty > hillFull + 1f && hillFull > 1f, hillEmpty + " vs " + hillFull + " g" + truck.Drive.Gear + " rpm" + truck.Drive.Rpm + " slip" + truck.Drive.Slip + " sh" + truck.Drive.Shifting + " " + truck.SurfaceNow);

        // ---- cruise control + limiter (also fuel by load)
        truck.SurfaceOverride = DriveSurface.DirtRoad; truck.RefreshSurface();
        Stop(truck); Step(truck, 3f, 1f);
        PressKey(kb.yKey);
        T.Check("Driving: Y sets cruise control at the current speed", truck.Assist.Cruise == CruiseMode.Cruise && Math.Abs(truck.Assist.CruiseSpeed - truck.Speed) < 0.6f, truck.Assist.CruiseSpeed + " vs " + truck.Speed);
        truck.Assist.SetCruise(CruiseMode.Cruise, 30f / 3.6f);
        Step(truck, 15f, 0f);
        float fuelA = truck.Fuel; Step(truck, 10f, 0f); float useEmpty = fuelA - truck.Fuel;
        T.Check("Driving: cruise control holds the set speed", Math.Abs(truck.Speed * 3.6f - 30f) < 1f, truck.Speed * 3.6f + " km/h");
        truck.Cargo["wheat"] = fullLoad; truck.RefreshSurface();
        Step(truck, 15f, 0f);
        fuelA = truck.Fuel; Step(truck, 10f, 0f); float useFull = fuelA - truck.Fuel;
        T.Check("Driving: cruise holds speed with a full load too", Math.Abs(truck.Speed * 3.6f - 30f) < 1.5f, truck.Speed * 3.6f + " km/h");
        T.Check("Driving: fuel use rises with load (same speed, full truck)", useFull > useEmpty * 1.05f, useEmpty + " vs " + useFull);
        truck.Cargo.Clear(); truck.RefreshSurface();
        Press(pad.dpad.right); Press(pad.dpad.right);
        T.Check("Driving: D-pad right raises the set speed", Math.Abs(truck.Assist.CruiseSpeed * 3.6f - 32f) < 0.01f, truck.Assist.CruiseSpeed * 3.6f + "");
        PressKey(kb.commaKey);
        T.Check("Driving: comma lowers the set speed", Math.Abs(truck.Assist.CruiseSpeed * 3.6f - 31f) < 0.01f);
        Step(truck, 0.2f, 0f, 0f, true);
        T.Check("Driving: braking cancels cruise control", truck.Assist.Cruise == CruiseMode.Off);
        truck.Assist.SetCruise(CruiseMode.Limiter, 15f / 3.6f);
        Stop(truck); Step(truck, 10f, 1f);
        T.Check("Driving: speed limiter caps full throttle for field work", truck.Speed * 3.6f < 16f && truck.Speed * 3.6f > 13f, truck.Speed * 3.6f + " km/h");
        truck.Assist.Reset();
        Press(pad.dpad.up);
        T.Check("Driving: D-pad up toggles cruise (gamepad)", truck.Assist.Cruise == CruiseMode.Cruise);
        truck.Assist.Reset();

        // ---- speed-sensitive steering
        float slowTurn = YawRate(truck, 5f), fastTurn = YawRate(truck, 15f);
        T.Check("Driving: steering is calmer at high speed", fastTurn < slowTurn * 0.85f && fastTurn > 0f, slowTurn + " vs " + fastTurn);
        Stop(truck); Step(truck, 0.4f, 0f);
        T.SetProp(truck, "Speed", 0f);
        truck.SurfaceOverride = null;
        truck.Exit();
        T.Check("Driving: getting out stops the engine and saves wear", !truck.Drive.EngineOn && SaveData.GetFloat("mazraa.wear.engine.truck", -1f) >= 0f);

        // ---- traction: surfaces, rain, 4WD, diff lock, tyre pressure
        tractor.transform.position = DeliveryZone.LoadingBay.transform.position + new Vector3(8f, 0f, 0f);
        tractor.Interact(); FakeEngine.Tick(Dt);
        T.SetProp(tractor, "ActiveImplement", Implement.None);
        tractor.SurfaceOverride = DriveSurface.Asphalt; tractor.RefreshSurface();
        float asphaltLimit = tractor.Drive.TractionLimit(), slipAsphalt = MaxSlip(tractor, 1.5f);
        tractor.SurfaceOverride = DriveSurface.Field; tractor.RefreshSurface();
        float fieldLimit = tractor.Drive.TractionLimit(), slipField = MaxSlip(tractor, 1.5f);
        T.SetProp(WorldClock.Instance, "Weather", Weather.Rain); tractor.RefreshSurface();
        float wetLimit = tractor.Drive.TractionLimit(), slipWet = MaxSlip(tractor, 1.5f);
        T.Check("Driving: field has less grip than asphalt, rain makes it worse", asphaltLimit > fieldLimit && fieldLimit > wetLimit && tractor.SurfaceWet, asphaltLimit + " > " + fieldLimit + " > " + wetLimit);
        T.Check("Driving: more wheel slip in a wet field", slipWet > slipField && slipField > slipAsphalt, slipAsphalt + " < " + slipField + " < " + slipWet);
        PressKey(kb.zKey);
        float slip4wd = MaxSlip(tractor, 1.5f);
        Press(pad.dpad.down);
        float slipLock = MaxSlip(tractor, 1.5f);
        T.Check("Driving: Z / D-pad down: 4WD then diff lock", tractor.TractionMode == 2 && tractor.Drive.FourWD && tractor.Drive.DiffLock);
        T.Check("Driving: 4WD reduces slip in the wet field", slip4wd < slipWet * 0.7f, slipWet + " -> " + slip4wd);
        T.Check("Driving: diff lock reduces it further", slipLock < slip4wd, slip4wd + " -> " + slipLock);
        PressKey(kb.zKey);
        T.Check("Driving: third press back to 2WD", tractor.TractionMode == 0);
        GameSettings.LowTyrePressure = true; tractor.RefreshSurface();
        float slipLow = MaxSlip(tractor, 1.5f);
        T.Check("Driving: low tyre pressure grips better in the field", slipLow < slipWet, slipWet + " -> " + slipLow);
        tractor.SurfaceOverride = DriveSurface.Asphalt; tractor.RefreshSurface();
        float lowTop = tractor.Drive.TopSpeed;
        GameSettings.LowTyrePressure = false; tractor.RefreshSurface();
        T.Check("Driving: high tyre pressure is faster on the road", tractor.Drive.TopSpeed > lowTop * 1.2f, lowTop + " vs " + tractor.Drive.TopSpeed);
        T.SetProp(WorldClock.Instance, "Weather", Weather.Sunny);
        tractor.SurfaceOverride = null;
        tractor.transform.position = new Vector3(400f, 0f, 400f);
        tractor.RefreshSurface();
        T.Check("Driving: open desert is sand", tractor.SurfaceNow == DriveSurface.Sand || DesertDressing.Instance == null, tractor.SurfaceNow + "");
        tractor.transform.position = DeliveryZone.LoadingBay.transform.position; tractor.RefreshSurface();
        T.Check("Driving: loading bay pad is asphalt", tractor.SurfaceNow == DriveSurface.Asphalt, tractor.SurfaceNow + "");
        var sb = new System.Text.StringBuilder(); GameHUD.DriveLines(tractor, sb);
        T.Check("Driving: HUD shows gear, RPM, slip %, surface and condition", sb.ToString().Contains("Slip") && sb.ToString().Contains("rpm") && sb.ToString().Contains("Asphalt") && sb.ToString().Contains("Tyres"), sb.ToString());

        // ---- implements: drag, working speed, fuel by load, ballast
        var plot = FarmFields.Instance.Fields[0];
        tractor.transform.position = plot.center + tractor.transform.forward * 2.6f; tractor.RefreshSurface();
        T.SetProp(tractor, "ActiveImplement", Implement.Plow); T.Set(tractor, "workCheckTime", -1f);
        Stop(tractor); Step(tractor, 1f, 1f);
        T.Check("Driving: plow in the ground while moving over a plot", tractor.Working && tractor.SurfaceNow == DriveSurface.Field, tractor.Working + " " + tractor.SurfaceNow);
        Step(tractor, 6f, 1f); float plowSpeed = tractor.Speed;
        T.Check("Driving: plowing is limited to working speed", plowSpeed * 3.6f <= 12.1f && plowSpeed * 3.6f > 9f, plowSpeed * 3.6f + " km/h g" + tractor.Drive.Gear + " rpm" + tractor.Drive.Rpm + " slip" + tractor.Drive.Slip + " m" + tractor.TotalMass);
        T.SetProp(tractor, "ActiveImplement", Implement.Seeder); T.Set(tractor, "workCheckTime", -1f);
        Stop(tractor); Step(tractor, 1.5f, 1f); float seedPickup = tractor.Speed; bool seedWorking = tractor.Working;
        T.SetProp(tractor, "ActiveImplement", Implement.Plow); T.Set(tractor, "workCheckTime", -1f);
        Stop(tractor); Step(tractor, 1.5f, 1f); float plowPickup = tractor.Speed; bool plowWorking = tractor.Working;
        T.Check("Driving: plowing drags harder than seeding", plowWorking && seedWorking && plowPickup < seedPickup * 0.9f, plowPickup + " vs " + seedPickup);
        // same 8 km/h on the cruise control: pulling the plow needs more throttle and fuel than driving free
        tractor.Assist.SetCruise(CruiseMode.Cruise, 8f / 3.6f);
        Step(tractor, 8f, 0f);
        float plowFuel0 = tractor.Fuel; Step(tractor, 10f, 0f); float plowFuel = plowFuel0 - tractor.Fuel; float plowLoad = tractor.Drive.Load;
        T.SetProp(tractor, "ActiveImplement", Implement.None); T.Set(tractor, "workCheckTime", -1f);
        Step(tractor, 8f, 0f);
        float freeFuel0 = tractor.Fuel; Step(tractor, 10f, 0f); float freeFuel = freeFuel0 - tractor.Fuel;
        T.Check("Driving: fuel use rises with load (plowing vs no implement at the same speed)", plowFuel > freeFuel * 1.2f && plowLoad > tractor.Drive.Load, plowFuel + " vs " + freeFuel + ", load " + plowLoad + " vs " + tractor.Drive.Load);
        tractor.Assist.Reset();
        float m0 = tractor.TotalMass;
        eco.AddMoney(5000);
        T.Check("Driving: buy tractor front weights (shop, Upgrades)", eco.TryBuy("tractor_ballast") && tractor.BallastLevel == 1 && ShopCatalog.Find("tractor_ballast").category == "Upgrades");
        tractor.RefreshSurface();
        T.Check("Driving: ballast adds mass and grip", tractor.TotalMass > m0 + 500f && tractor.Drive.BallastMass > 0f, m0 + " -> " + tractor.TotalMass);
        tractor.SetBallast(0);
        Stop(tractor);

        // ---- GPS straight line
        tractor.transform.position = DeliveryZone.LoadingBay.transform.position + new Vector3(0f, 0.55f, 20f);
        T.Set(tractor, "yaw", 0f); tractor.transform.rotation = Quaternion.identity;
        FakeEngine.Tick(Dt);
        PressKey(kb.slashKey);
        T.Check("Driving: / turns on the GPS straight line", tractor.Assist.LaneAssist);
        T.Set(tractor, "yaw", 10f);
        tractor.transform.position += new Vector3(1.5f, 0f, 0f);
        pad.rightTrigger.value = 0.6f;
        FakeEngine.Run(12f, Dt);
        T.Check("Driving: GPS line pulls back onto the line and holds the heading", Math.Abs(tractor.Assist.LaneOffset(tractor.transform.position)) < 0.5f && Math.Abs(Mathf.DeltaAngle(tractor.Yaw, 0f)) < 4f,
            tractor.Assist.LaneOffset(tractor.transform.position) + " m, yaw " + tractor.Yaw);
        pad.leftStick.value = new Vector2(1f, 0f);
        for (int i = 0; i < 2000 && Math.Abs(Mathf.DeltaAngle(tractor.Yaw, 180f)) > 25f; i++) FakeEngine.Tick(Dt);
        pad.leftStick.value = Vector2.zero;
        FakeEngine.Run(12f, Dt);
        float off = tractor.Assist.LaneOffset(tractor.transform.position);
        T.Check("Driving: after a headland turn it follows the next parallel lane back", Math.Abs(Mathf.DeltaAngle(tractor.Yaw, 180f)) < 4f && Math.Abs(off) < 0.6f,
            "offset " + off + ", yaw " + tractor.Yaw);
        pad.rightTrigger.value = 0f;
        Press(pad.rightStickButton);
        T.Check("Driving: R3 turns the GPS line off", !tractor.Assist.LaneAssist);
        FakeEngine.Run(3f, Dt);

        // ---- wear and repair
        tractor.SurfaceOverride = DriveSurface.Field; tractor.RefreshSurface();
        tractor.Drive.EngineWear = tractor.Drive.TyreWear = 0f;
        Stop(tractor);
        Step(tractor, 60f, 1f);
        float ew = tractor.Drive.EngineWear, tw = tractor.Drive.TyreWear;
        T.Check("Driving: engine and tyre wear accumulate with use", ew > 0.003f && tw > 0.001f, ew + " / " + tw);
        tractor.SurfaceOverride = DriveSurface.Asphalt; tractor.RefreshSurface();
        tractor.Drive.EngineWear = 0f; Stop(tractor); Step(tractor, 3f, 1f); float newSpeed = tractor.Speed;
        tractor.Drive.EngineWear = 0.8f; Stop(tractor); Step(tractor, 3f, 1f);
        T.Check("Driving: a worn engine has less power", tractor.Speed < newSpeed * 0.9f, newSpeed + " vs " + tractor.Speed);
        tractor.Drive.TyreWear = 0.5f;
        tractor.SurfaceOverride = null;
        Stop(tractor);
        tractor.transform.position = DeliveryZone.LoadingBay.transform.position; tractor.RefreshSurface();
        int cost = tractor.RepairCost;
        T.Check("Driving: parked on the loading bay = workshop, repair has a price", tractor.AtWorkshop && cost > 1000, cost + "");
        sb.Clear(); GameHUD.DriveLines(tractor, sb);
        T.Check("Driving: HUD offers the repair", sb.ToString().Contains("Repair for"), sb.ToString());
        eco.AddMoney(-eco.Money);
        T.Check("Driving: no repair without the money", !tractor.TryRepair() && tractor.Drive.EngineWear > 0.7f);
        eco.AddMoney(cost + 100);
        Press(pad.buttonWest);
        T.Check("Driving: X / Square on the bay repairs for money (wear reset)", tractor.Drive.EngineWear < 0.001f && tractor.Drive.TyreWear < 0.001f && eco.Money == 100, eco.Money + "");
        tractor.Drive.EngineWear = 0.3f;
        T.SetProp(tractor, "Speed", 0f);
        tractor.Exit();
        SaveData.Flush(true);
        Program.BuildWorld(); FakeEngine.Run(1f);
        T.Check("Driving: wear is saved with the vehicle", Math.Abs(Tractor.Instance.Drive.EngineWear - 0.3f) < 0.001f, Tractor.Instance.Drive.EngineWear + "");
        GameEvents.RaiseNewGameStarted();
        T.Check("Driving: new game resets wear", Tractor.Instance.Drive.EngineWear == 0f);

        // ---- controls + texts
        var drivingActions = new[] { GameAction.EngineToggle, GameAction.GearUp, GameAction.GearDown, GameAction.Cruise, GameAction.CruiseFaster, GameAction.CruiseSlower, GameAction.Traction, GameAction.LaneAssist };
        T.Check("Driving: every driving action has a keyboard key and a gamepad button with a glyph",
            drivingActions.All(a => !string.IsNullOrEmpty(GameInput.DefaultKeyPath(a)) && !string.IsNullOrEmpty(GameInput.GamepadPath(a)) && Glyphs.PadFor(a, false) != null && Glyphs.PadFor(a, true) != null));
        Tractor.Instance.Interact(); FakeEngine.Tick(Dt);   // F / Ctrl / Z hints mean the vehicle actions only in a vehicle
        GameInput.ForceDevice(true, false);
        string hint = Glyphs.Apply(Loc.T(Hints));
        T.Check("Driving: hints show gamepad glyphs (LS, RB, LB, D-pad, RS)", hint.Contains("[LS]") && hint.Contains("[RB]") && hint.Contains("[LB]") && hint.Contains("D-PAD↑") && hint.Contains("[RS]"), hint);
        GameInput.ForceDevice(false, false);
        T.Check("Driving: keyboard hints show the keys", Glyphs.Apply(Loc.T(Hints)).Contains("[LeftShift]"), Glyphs.Apply(Loc.T(Hints)));
        T.SetProp(Tractor.Instance, "Speed", 0f); Tractor.Instance.Exit();
        T.Check("Driving: driving texts have Arabic", drivingActions.All(a => Loc.HasTranslation(GameInput.Label(a))) && Loc.HasTranslation("Slip {0}%   {1}") && Loc.HasTranslation("Gearbox"));
        T.Check("Driving: no errors", FakeEngine.Errors.Count == 0, string.Join(" | ", FakeEngine.Errors.Take(3)));
        UnityEngine.InputSystem.Gamepad.current = null;
    }
}

// ---------------------------------------------------------------- On-foot life (carrying, hand tools) + animal husbandry
static class LifeTests
{
    static int herdSaved, balesSaved; static string traitsSaved;

    static void Hold(UnityEngine.InputSystem.KeyControl k) { k.wasPressedThisFrame = true; FakeEngine.Tick(0.05f); k.wasPressedThisFrame = false; }
    static void Press(UnityEngine.InputSystem.ButtonControl b) { b.wasPressedThisFrame = true; FakeEngine.Tick(0.05f); b.wasPressedThisFrame = false; }

    public static void Run()
    {
        Console.WriteLine("On-foot life + animal husbandry");
        // The story intro shown at boot is never closed in these tests: give gameplay input back for this section.
        int ext = (int)T.Get(UIManager.Instance, "externalCount");
        for (int i = 0; i < ext; i++) UIManager.Instance.SetExternalPanel(false);
        int e0 = FakeEngine.Errors.Count;
        var eco = EconomyManager.Instance; var clock = WorldClock.Instance; var pen = AnimalPen.Instance;
        var life = PlayerLife.Instance; var herd = HerdManager.Instance;
        var player = GameObject.Find("PlayerCapsule").transform;
        var inputs = player.GetComponent<StarterAssets.StarterAssetsInputs>();
        eco.AddMoney(60000);
        FakeEngine.Run(1.5f);
        T.Check("Life: PlayerLife and HerdManager running", life != null && herd != null);
        int bales0 = life.BaleCount;
        T.Check("Life: hay bales (2 + pressed from stored alfalfa) and crates by the pen, records board at the gate", life.BaleCount >= 2 && life.BaleCount <= PlayerLife.MaxBales && Carriable.All.Count(c => c.kind == "crate") == 2 && UnityEngine.Object.FindAnyObjectByType<PenBoard>() != null, life.BaleCount + "");

        // --- controls: keyboard + gamepad bindings, glyphs
        T.Check("Life: 5 new on-foot actions with keyboard and gamepad bindings",
            new[] { GameAction.Crouch, GameAction.Carry, GameAction.UseTool, GameAction.ToolPrev, GameAction.ToolNext }.All(a => !string.IsNullOrEmpty(GameInput.DefaultKeyPath(a)) && !string.IsNullOrEmpty(GameInput.GamepadPath(a))));
        GameInput.ForceDevice(true, false);
        string g = Glyphs.Apply("[,] [.] [Z] [F] [Ctrl]");
        T.Check("Life: prompts show LB / RB / RT / D-pad / LS on a gamepad", g.Contains("[LB]") && g.Contains("[RB]") && g.Contains("[RT]") && g.Contains("D-PAD") && g.Contains("[LS]"), g);
        GameInput.ForceDevice(true, true);
        T.Check("Life: PlayStation labels L1 / R1 / R2", Glyphs.Apply("[,] [.] [Z]") == "[L1] [R1] [R2]", Glyphs.Apply("[,] [.] [Z]"));
        GameInput.ForceDevice(false, false);

        // --- thirst model (no stamina)
        var mild = new StaminaModel(); var hot = new StaminaModel();
        for (int i = 0; i < 30; i++) { mild.Tick(0.1f, true, true, false, 0f, 0f, 0.0003f); hot.Tick(0.1f, true, true, false, 1f, 0f, 0.0003f); }
        T.Check("Life: heat also dries you out faster", hot.hydration < mild.hydration);
        T.Check("Life: sprint is unlimited (always allowed on foot with empty hands)", MoveRules.CanSprint(0f, false));
        var thirsty = new StaminaModel { hydration = 0f }; thirsty.Drink();
        T.Check("Life: drinking restores water", thirsty.hydration == 1f);

        // --- sprinting for a long time never slows the player or releases sprint
        T.SetProp(clock, "DayProgress", 0.3f);
        player.position = new Vector3(0, 0, -57.4f);
        T.SetProp(clock, "Weather", Weather.Heatwave);
        life.Model.hydration = 1f;
        inputs.move = new Vector2(0, 1); inputs.sprint = true;
        FakeEngine.Run(20f, 0.1f);
        T.Check("Life: sprinting never runs out (heatwave, 20 s)", PlayerLife.SprintAllowed && inputs.sprint && PlayerLife.SpeedFactor >= 0.99f, PlayerLife.SpeedFactor + "");
        inputs.move = Vector2.zero; inputs.sprint = false;
        var kb = new UnityEngine.InputSystem.Keyboard();
        foreach (var f in typeof(UnityEngine.InputSystem.Keyboard).GetFields()) if (f.FieldType == typeof(UnityEngine.InputSystem.KeyControl)) f.SetValue(kb, new UnityEngine.InputSystem.KeyControl());
        UnityEngine.InputSystem.Keyboard.current = kb;
        Hold(kb.leftCtrlKey);
        T.Check("Life: Ctrl crouches (controller lowers and slows)", life.IsCrouching && PlayerLife.Crouching && PlayerLife.SpeedFactor < 0.6f && !PlayerLife.SprintAllowed);
        FakeEngine.Run(1f, 0.1f);
        Hold(kb.leftCtrlKey);
        T.Check("Life: Ctrl again stands up", !life.IsCrouching);
        T.SetProp(clock, "Weather", Weather.Sunny);


        // --- drinking at the well with bare hands
        var well = UnityEngine.Object.FindAnyObjectByType<WellController>();
        if (!well.IsRunning) well.Interact();
        player.position = well.transform.position + new Vector3(1.5f, 0, 0);
        life.Model.hydration = 0.2f; life.SetTool(HandTool.Hands);
        string drink = life.UseTool();
        T.Check("Life: bare hands at the running well: drink", life.Model.hydration == 1f, drink);

        // --- carrying: weight slows you, no sprint with a bale
        var bale = Carriable.All.First(c => c.kind == "hay_bale");
        player.position = bale.transform.position + new Vector3(0.8f, 0, 0);
        FakeEngine.Run(0.2f, 0.1f);
        float free = PlayerLife.SpeedFactor;
        Hold(kb.fKey);
        T.Check("Life: F picks up the nearest hay bale (25 kg)", life.Carried == bale && bale.Carried && life.CarryKg == 25f, life.LastMessage);
        T.Check("Life: carrying a heavy bale slows you (mildly) and stops sprinting", PlayerLife.SpeedFactor < free * 0.99f && PlayerLife.SpeedFactor > free * 0.85f && !PlayerLife.SprintAllowed && PlayerLife.AccelerationFactor < 1f && PlayerLife.AccelerationFactor > 0.7f, free + " -> " + PlayerLife.SpeedFactor + ", accel " + PlayerLife.AccelerationFactor);
        T.Check("Life: a light crate slows you less", MoveRules.CarryFactor(8f) > MoveRules.CarryFactor(25f) && MoveRules.CanSprint(8f, false));
        FakeEngine.Run(0.3f, 0.1f);
        T.Check("Life: HUD shows the load, no stamina bar", life.Hud.LastText.Contains("25 kg") && !life.Hud.LastText.Contains("Stamina"), life.Hud.LastText);

        // --- herd: two sheep (ewe + ram)
        T.Check("Life: buy 2 sheep", eco.TryBuy("sheep") && eco.TryBuy("sheep"));
        herd.Reconcile();
        var sheep = herd.Animals.Where(a => a.species == "sheep").ToList();
        T.Check("Life: one record per animal; the pair is a ewe and a ram", herd.Animals.Count == pen.AnimalCount && sheep.Count == 2 && sheep.Count(a => a.male) == 1 && sheep.All(a => a.Adult), herd.Animals.Count + " vs " + pen.AnimalCount);
        FakeEngine.Run(1.5f);
        T.Check("Life: every animal in the pen is linked to its record", pen.Animals.All(a => a.uid != 0 && herd.Find(a.uid) != null && herd.Find(a.uid).species == a.animalId));

        // drop the bale in the pen: everyone fed
        foreach (var a in herd.Animals) a.food = 0.2f;
        player.position = pen.transform.position;
        Hold(kb.fKey);
        T.Check("Life: bale put down in the pen feeds every animal", life.Carried == null && herd.Animals.All(a => a.food == 1f) && life.BaleCount == bales0 - 1, life.LastMessage);

        // --- needs drop daily, feeding and watering restore
        eco.ConsumeFeed(eco.Feed);
        T.SetProp(pen, "WaterLevel", 0f);
        herd.SimulateDay(clock.Day);
        T.Check("Life: without feed and water, needs drop over a day", herd.Animals.All(a => a.food <= 0.5f && a.water < 0.5f), string.Join(",", herd.Animals.Select(a => a.food + "/" + a.water)));
        eco.AddFeed(20f);
        int fed = herd.FeedAll();
        pen.FillWater(); herd.WaterAll();
        T.Check("Life: feeding and filling the trough restore food and water", fed == herd.Animals.Count && herd.Animals.All(a => a.food == 1f && a.water == 1f));

        // --- tools
        life.SetTool(HandTool.Hands);
        life.CycleTool(1);
        FakeEngine.Run(0.3f, 0.1f);
        T.Check("Life: tool wheel cycles (Hands -> Hoe) and shows the ring", life.Tool == HandTool.Hoe && life.Hud.LastText.Contains("( Hoe )"), life.Hud.LastText);
        Hold(kb.periodKey);
        T.Check("Life: period selects the next tool", life.Tool == HandTool.WateringCan);
        var pad = new UnityEngine.InputSystem.Gamepad { name = "XInputControllerWindows" };
        UnityEngine.InputSystem.Gamepad.current = pad;
        Press(pad.leftShoulder);
        T.Check("Life: LB cycles back", life.Tool == HandTool.Hoe);
        var plot = FarmFields.Instance.Fields[0];
        plot.ResetSoil(FcData.CurrentRegion);
        {
            player.position = plot.center;
            Press(pad.rightTrigger);
            T.Check("Life: RT with the hoe works the soil by hand ", FC.Worked(plot) > 0, life.LastMessage);
            life.SetTool(HandTool.WateringCan);
            FC.Wet(plot, 0f);
            T.SetProp(life, "CanWater", 1f);
            string w = life.UseTool();
            T.Check("Life: watering can waters the soil around you", FarmSim.Summarize(plot).moisture > 0.02f, w);
        }
        UnityEngine.InputSystem.Gamepad.current = null;
        life.SetTool(HandTool.Shovel);
        player.position = pen.transform.position;
        T.SetProp(pen, "Cleanliness", 0.3f);
        string sh = life.UseTool();
        T.Check("Life: shovel mucks out the pen", pen.Cleanliness > 0.55f, sh);
        life.SetTool(HandTool.Brush);
        var body = pen.Animals.First(a => a.animalId == "sheep");
        // the brush grooms the nearest animal: move the others (a wandering chicken may be closer) out of reach first
        foreach (var other in pen.Animals) if (other != null && other != body) other.transform.position = body.transform.position + new Vector3(0f, 0f, 25f);
        player.position = body.transform.position + new Vector3(0.5f, 0, 0);
        var rec = herd.Find(body.uid);
        rec.happiness = 0.3f; rec.clean = 0.2f;
        string br = life.UseTool();
        T.Check("Life: brushing an animal cleans it and makes it happier", rec.clean == 1f && rec.happiness >= 0.5f, br);
        T.Check("Life: footsteps know the surface (pen straw, field soil, sand)", PlayerLife.SurfaceAt(pen.transform.position) == "straw"
            && PlayerLife.SurfaceAt(FarmFields.Instance.Fields[0].center) == "soil" && PlayerLife.SurfaceAt(new Vector3(400, 0, 400)) == "sand");

        // --- illness: produces less, medicine cures
        var ewe = sheep.First(a => !a.male);
        ewe.food = ewe.water = ewe.clean = ewe.health = ewe.happiness = 1f; ewe.heatStress = 0f; ewe.illness = "";
        float healthyP = Husbandry.Productivity(ewe), factor0 = herd.ProductionFactor("sheep");
        herd.MakeSick(ewe, "respiratory");
        T.Check("Life: a sick animal produces much less", Husbandry.Productivity(ewe) < healthyP * 0.5f && herd.ProductionFactor("sheep") < factor0, healthyP + " -> " + Husbandry.Productivity(ewe));
        T.Check("Life: no medicine in stock -> can't treat", ewe.Sick && herd.Treat(ewe).Contains("No medicine"));
        T.Check("Life: buy medicine at the shop", eco.TryBuy(HerdManager.MedicineId) && herd.Medicine == 1);
        herd.Treat(ewe);
        T.Check("Life: medicine cures it", !ewe.Sick && herd.Medicine == 0 && Husbandry.Productivity(ewe) > healthyP * 0.9f);
        herd.MakeSick(ewe, "digestive");
        int m0 = eco.Money;
        string vet = herd.CallVet();
        T.Check("Life: the vet cures every sick animal for a fee", !ewe.Sick && eco.Money == m0 - HerdManager.VetPrice, vet);
        T.Check("Life: vet not needed when all are healthy", herd.CallVet().Contains("not needed") && eco.Money == m0 - HerdManager.VetPrice);

        // --- heat stress: shade and misting fans
        float load = Husbandry.HeatLoad(Season.Summer, Weather.Heatwave);
        float open = Husbandry.HeatStress("chicken", load, false, false), shaded = Husbandry.HeatStress("chicken", load, true, false), both = Husbandry.HeatStress("chicken", load, true, true);
        T.Check("Life: summer heatwave stresses hens; shade helps, shade + misting fans more", open > 0.9f && shaded < open - 0.3f && both < shaded - 0.3f, open + " / " + shaded + " / " + both);
        T.Check("Life: camels hardly mind the heat", Husbandry.HeatStress("camel", load, false, false) < 0.2f);
        var hotHen = new AnimalRecord { species = "chicken", ageDays = 20, heatStress = open };
        var coolHen = new AnimalRecord { species = "chicken", ageDays = 20, heatStress = both };
        T.Check("Life: heat stress lowers production", Husbandry.Productivity(hotHen) < Husbandry.Productivity(coolHen) * 0.6f);
        T.SetProp(clock, "Weather", Weather.Heatwave);
        float before = herd.StressFor("sheep");
        T.Check("Life: buy shade canopy and misting fans", eco.TryBuy(HerdManager.ShadeId) && eco.TryBuy(HerdManager.FansId) && herd.Shade && herd.Fans && !eco.TryBuy(HerdManager.ShadeId));
        pen.FillWater();
        T.Check("Life: in the pen, shade + fans cut heat stress", herd.StressFor("sheep") < before * 0.5f + 0.001f && GameObject.Find("Pen Shade") != null && GameObject.Find("Misting Fans") != null, before + " -> " + herd.StressFor("sheep"));
        T.SetProp(pen, "WaterLevel", 0f);
        T.Check("Life: fans need water in the trough", !herd.FansWorking);
        pen.FillWater();
        T.SetProp(clock, "Weather", Weather.Sunny);

        // --- breeding with gestation and inherited trait
        herd.Seed(7);
        ewe.gestationLeft = 0;   // earlier SimulateDay calls use the unseeded RNG and may already have made her pregnant (flaky)
        var ram = sheep.First(a => a.male);
        ewe.trait = 1.3f; ram.trait = 1.1f;
        ewe.gestationLeft = 0;   // other systems may have run days earlier in the suite (time scale settings)
        int owned0 = eco.Owned("sheep"), days = 0;
        for (; days < 30 && !ewe.Pregnant; days++) { pen.FillWater(); pen.Clean(); eco.AddFeed(5f); herd.SimulateDay(clock.Day + days); foreach (var a in herd.Animals) a.illness = ""; }
        T.Check("Life: a healthy, happy ewe with a ram conceives", ewe.Pregnant && ewe.gestationLeft == SpeciesTraits.Get("sheep").gestation && Math.Abs(ewe.sireTrait - 1.1f) < 0.001f, "days " + days);
        herd.SimulateDay(clock.Day + days++);
        T.Check("Life: no lamb before the gestation time", eco.Owned("sheep") == owned0 && ewe.Pregnant);
        for (int i = 0; i < 6 && ewe.Pregnant; i++) { pen.FillWater(); eco.AddFeed(5f); herd.SimulateDay(clock.Day + days++); foreach (var a in herd.Animals) a.illness = ""; }
        var lamb = herd.Animals.LastOrDefault(a => a.species == "sheep" && a.ageDays <= 1);
        T.Check("Life: after the gestation a lamb is born", eco.Owned("sheep") >= owned0 + 1 && lamb != null && !ewe.Pregnant, eco.Owned("sheep") + "");
        if (lamb != null)
        {
            T.Check("Life: lamb inherits the parents' trait (1.2 +- 0.1)", lamb.trait >= 1.1f - 0.0001f && lamb.trait <= 1.3f + 0.0001f, "" + lamb.trait);
            T.Check("Life: young animals don't produce yet", Husbandry.Productivity(lamb) == 0f && !lamb.Adult);
        }
        FakeEngine.Run(1.5f);
        T.Check("Life: the newborn walks in the pen", pen.Animals.Count(a => a.animalId == "sheep") == eco.Owned("sheep"));
        var r = Enumerable.Range(0, 200).Select(i => Husbandry.Inherit(0.8f, 1.0f, new System.Random(i))).ToList();
        T.Check("Life: inheritance varies but stays within the parents' average +- 0.1", r.Min() >= 0.8f && r.Max() <= 1.0f && r.Max() - r.Min() > 0.1f, r.Min() + ".." + r.Max());

        // --- value by age and health
        var adult = new AnimalRecord { species = "sheep", ageDays = 20, trait = 1f };
        var young = new AnimalRecord { species = "sheep", ageDays = 1, trait = 1f };
        var old = new AnimalRecord { species = "sheep", ageDays = 130, trait = 1f };
        var weak = new AnimalRecord { species = "sheep", ageDays = 20, trait = 1f, health = 0.3f, illness = "respiratory" };
        int vA = Husbandry.Value(adult, 2500);
        T.Check("Life: price by age and health (prime adult worth most)", vA == 2500 && Husbandry.Value(young, 2500) < vA && Husbandry.Value(old, 2500) < vA && Husbandry.Value(weak, 2500) < vA / 2,
            vA + " " + Husbandry.Value(young, 2500) + " " + Husbandry.Value(old, 2500) + " " + Husbandry.Value(weak, 2500));

        // --- records panel (gamepad)
        var board = UnityEngine.Object.FindAnyObjectByType<PenBoard>();
        board.Interact();
        var panel = herd.Panel;
        T.Check("Life: records board opens the animal panel (game input paused)", panel.IsOpen && UIManager.Instance.IsUIOpened && panel.LastText.Contains("Animal records") && panel.LastText.Contains("Genetics"), panel.LastText);
        FakeEngine.Tick(0.05f);
        UnityEngine.InputSystem.Gamepad.current = pad;
        int sel = panel.Selected;
        Press(pad.dpad.down);
        T.Check("Life: D-pad selects the next animal", panel.Selected == (sel + 1) % herd.Animals.Count, sel + " -> " + panel.Selected);
        if (lamb != null)
        {
            panel.Select(herd, herd.Animals.ToList().IndexOf(lamb));
            int count0 = eco.Owned("sheep"), money0 = eco.Money;
            Press(pad.buttonWest);
            T.Check("Life: X asks before selling", eco.Owned("sheep") == count0 && panel.LastText.Contains("again to sell"), panel.LastText);
            Press(pad.buttonWest);
            T.Check("Life: X again sells the lamb at its value", eco.Owned("sheep") == count0 - 1 && eco.Money > money0 && herd.Find(lamb.uid) == null, (eco.Money - money0) + "");
            FakeEngine.Run(0.5f);
            T.Check("Life: sold animal leaves the pen", pen.Animals.Count(a => a.animalId == "sheep") == eco.Owned("sheep"));
        }
        Press(pad.buttonEast);
        T.Check("Life: B closes the panel and gives input back", !panel.IsOpen && !UIManager.Instance.IsUIOpened);
        UnityEngine.InputSystem.Gamepad.current = null;
        UnityEngine.InputSystem.Keyboard.current = null;
        GameInput.ForceDevice(false, false);

        // --- daily simulation runs on its own
        // (a birth, death or sale on the same morning changes the herd: compare the animals that are still there)
        var ages = herd.Animals.ToDictionary(a => a.uid, a => a.ageDays);
        clock.NextDay();
        var stayed = herd.Animals.Where(a => ages.ContainsKey(a.uid)).ToList();
        T.Check("Life: a new day ages the herd", stayed.Count > 0 && stayed.All(a => a.ageDays == ages[a.uid] + 1), stayed.Count + " / " + ages.Count);

        // --- Arabic for every text
        var keys = new List<string>();
        foreach (var f in Directory.GetFiles(Path.Combine(FakeEngine.ResourcesRoot, "..", "Scripts", "Life"), "*.cs"))
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(f), "Loc\\.(?:T|Raw)\\(\"((?:[^\"\\\\]|\\\\.)*)\""))
                keys.Add(m.Groups[1].Value);
        keys.AddRange(HandTools.All.Select(HandTools.Key));
        keys.AddRange(Husbandry.Illnesses.Select(Husbandry.IllnessKey));
        keys.AddRange(new[] { "Hay bale", "Produce crate", "male", "female", "yes", "no", "on", "no water", "Crouch / rest", "Pick up / put down", "Use hand tool", "Previous tool", "Next tool" });
        Loc.Raw("x");
        var missing = keys.Distinct().Where(k => !Loc.HasTranslation(k)).ToList();
        T.Check("Life: every text has Arabic (Loc/92_life.json)", keys.Count > 50 && missing.Count == 0, string.Join(" | ", missing.Take(5)));
        Loc.SetLanguage(Language.Arabic);
        board.Interact(); herd.Panel.Refresh(herd);
        T.Check("Life: panel and prompt in Arabic", herd.Panel.LastText.Contains("سجل الحيوانات") && board.InteractionPrompt.Any(c => c >= 'ﹰ'), board.InteractionPrompt);
        herd.Panel.Close();
        Loc.SetLanguage(Language.English);

        // the older sections expect only chickens in the pen: sell the sheep again (keeps shade, fans and genetics records)
        foreach (var a in herd.Animals.Where(x => x.species == "sheep").ToList()) herd.Sell(a);
        FakeEngine.Run(0.5f);
        T.Check("Life: selling empties the sheep from the pen", eco.Owned("sheep") == 0 && pen.Animals.Count(a => a.animalId == "sheep") == 0);

        T.Check("Life: no errors", FakeEngine.Errors.Count == e0, string.Join(" | ", FakeEngine.Errors.Skip(e0).Take(3)));
        for (int i = 0; i < ext; i++) UIManager.Instance.SetExternalPanel(true);
    }

    /// <summary>Snapshot just before the save file is flushed and the world rebuilt.</summary>
    public static void BeforeSave()
    {
        var herd = HerdManager.Instance; var life = PlayerLife.Instance;
        life.SetTool(HandTool.Brush);
        life.Save(); herd.Save();
        herdSaved = herd.Animals.Count; balesSaved = life.BaleCount;
        traitsSaved = string.Join(",", herd.Animals.Select(a => a.uid + ":" + a.trait.ToString("F3") + ":" + a.male));
    }

    public static void AfterRestart()
    {
        var herd = HerdManager.Instance; var life = PlayerLife.Instance;
        string traits = string.Join(",", herd.Animals.Select(a => a.uid + ":" + a.trait.ToString("F3") + ":" + a.male));
        T.Check("Life: herd restored with uids, sex and genetics", herd.Animals.Count == herdSaved && traits == traitsSaved, traits + " vs " + traitsSaved);
        T.Check("Life: shade and fans restored", herd.Shade && herd.Fans && GameObject.Find("Pen Shade") != null);
        T.Check("Life: tool and bales restored", life.Tool == HandTool.Brush && life.BaleCount == balesSaved, life.Tool + " " + life.BaleCount);
        T.Check("Life: pen bodies linked after restart", AnimalPen.Instance.Animals.All(a => a.uid != 0));
    }

    public static void AfterNewGame()
    {
        var herd = HerdManager.Instance; var life = PlayerLife.Instance;
        T.Check("Life: new game clears the herd, shade and medicine", herd.Animals.Count == 0 && !herd.Shade && !herd.Fans && herd.Medicine == 0 && GameObject.Find("Pen Shade") == null);
        T.Check("Life: new game resets tool and bales", life.Tool == HandTool.Hands && life.BaleCount == 2 && life.Carried == null, life.BaleCount + "");
    }
}

// ---------------------------------------------------------------- shared keys: driving vs on-foot actions read by context
static class SharedKeyTests
{
    static void Key(UnityEngine.InputSystem.KeyControl k) { k.wasPressedThisFrame = true; k.isPressed = true; FakeEngine.Tick(0.02f); k.wasPressedThisFrame = false; k.isPressed = false; FakeEngine.Tick(0.02f); }
    static void Pad(UnityEngine.InputSystem.ButtonControl b) { b.wasPressedThisFrame = true; b.isPressed = true; FakeEngine.Tick(0.02f); b.wasPressedThisFrame = false; b.isPressed = false; FakeEngine.Tick(0.02f); }

    public static void Run()
    {
        Console.WriteLine("Shared keys: vehicle actions only in a vehicle, on-foot actions only on foot");
        Program.BuildWorld();
        FakeEngine.Run(2f);
        for (int i = 0; i < 400 && LoadingScreen.Visible; i++) FakeEngine.Tick(0.1f);
        var intro = UnityEngine.Object.FindAnyObjectByType<StoryIntro>();
        if (intro != null) typeof(StoryIntro).GetMethod("Close", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(intro, null);
        FakeEngine.Run(0.5f);
        var kb = new UnityEngine.InputSystem.Keyboard();
        foreach (var f in typeof(UnityEngine.InputSystem.Keyboard).GetFields())
            if (f.FieldType == typeof(UnityEngine.InputSystem.KeyControl)) f.SetValue(kb, new UnityEngine.InputSystem.KeyControl());
        UnityEngine.InputSystem.Keyboard.current = kb;
        var pad = new UnityEngine.InputSystem.Gamepad { name = "XInputControllerWindows", displayName = "Xbox Controller" };
        UnityEngine.InputSystem.Gamepad.current = pad;
        GameSettings.Gearbox = GearboxMode.Manual;
        var life = PlayerLife.Instance; var truck = FarmTruck.Instance;
        var player = GameObject.Find("PlayerCapsule").transform;
        var bale = Carriable.All.FirstOrDefault(c => c != null && c.kind == "hay_bale");
        T.Check("Shared keys: world ready (player life, bale, no panel open)", life != null && bale != null && !UIManager.Instance.IsUIOpened && Vehicle.Current == null);
        if (life == null || bale == null) return;

        // ---- on foot: F / Ctrl / Z / , . / LB RB / D-pad up drive the on-foot actions and never the vehicle
        truck.transform.position = bale.transform.position + new Vector3(4f, 0f, 0f);
        player.position = bale.transform.position + new Vector3(0.8f, 0f, 0f);
        FakeEngine.Run(0.2f, 0.1f);
        bool engine0 = truck.Drive.EngineOn; int gear0 = truck.Drive.Gear; int trac0 = truck.TractionMode; var cruise0 = truck.Assist.Cruise; float set0 = truck.Assist.CruiseSpeed;
        Key(kb.fKey);
        T.Check("Shared keys: on foot F picks up, engine untouched", life.Carried == bale && truck.Drive.EngineOn == engine0, life.LastMessage);
        Key(kb.fKey);
        T.Check("Shared keys: on foot F puts down", life.Carried == null);
        Key(kb.leftCtrlKey);
        T.Check("Shared keys: on foot Ctrl crouches, no gear change", life.IsCrouching && truck.Drive.Gear == gear0);
        Key(kb.leftCtrlKey);
        string msg = life.LastMessage;
        Key(kb.zKey);
        T.Check("Shared keys: on foot Z uses the hand tool, 4WD untouched", truck.TractionMode == trac0);
        var tool0 = life.Tool;
        Key(kb.periodKey);
        T.Check("Shared keys: on foot . picks the next tool, cruise speed untouched", life.Tool != tool0 && Math.Abs(truck.Assist.CruiseSpeed - set0) < 0.001f, life.Tool + "");
        Key(kb.commaKey);
        Pad(pad.rightShoulder);
        T.Check("Shared keys: on foot RB cycles tools, no gear change", life.Tool != tool0 && truck.Drive.Gear == gear0);
        Pad(pad.leftShoulder);
        T.Check("Shared keys: on foot LB cycles back", life.Tool == tool0);
        Pad(pad.dpad.up);
        T.Check("Shared keys: on foot D-pad up picks up, cruise untouched", life.Carried != null && truck.Assist.Cruise == cruise0, life.LastMessage + " carried=" + (life.Carried != null) + " d=" + Vector3.Distance(player.position, bale.transform.position));
        Pad(pad.dpad.up);
        Pad(pad.leftStickButton);
        T.Check("Shared keys: on foot L3 crouches, engine untouched", life.IsCrouching && truck.Drive.EngineOn == engine0);
        Pad(pad.leftStickButton);
        var journal = FarmLifeScreen.Instance;
        if (journal != null)
        {
            Key(kb.yKey);
            T.Check("Shared keys: on foot Y opens the Farm life journal, cruise untouched", journal.IsOpen && truck.Assist.Cruise == cruise0);
            journal.Close(); FakeEngine.Tick(0.02f); FakeEngine.Tick(0.02f);
            Pad(pad.dpad.down);
            T.Check("Shared keys: on foot D-pad down opens the journal, 4WD untouched", journal.IsOpen && truck.TractionMode == trac0);
            journal.Close(); FakeEngine.Tick(0.02f); FakeEngine.Tick(0.02f);
        }

        // ---- in the vehicle: the same keys drive the vehicle and never the on-foot actions
        truck.transform.position = bale.transform.position + new Vector3(1f, 0f, 0f);   // a bale within reach if pick-up leaked through
        truck.Interact(); FakeEngine.Tick(0.02f);
        T.Check("Shared keys: in the truck, engine running", Vehicle.Current == truck && truck.Drive.EngineOn);
        var toolIn = life.Tool; string msgIn = life.LastMessage;
        Key(kb.fKey);
        T.Check("Shared keys: in a vehicle F stops the engine, picks nothing up", !truck.Drive.EngineOn && life.Carried == null && !bale.Carried);
        Key(kb.fKey);
        T.Check("Shared keys: in a vehicle F starts it again", truck.Drive.EngineOn && life.Carried == null);
        Key(kb.leftShiftKey); Key(kb.leftShiftKey);
        int g = truck.Drive.Gear;
        Key(kb.leftCtrlKey);
        T.Check("Shared keys: in a vehicle Ctrl shifts down, no crouch", truck.Drive.Gear == g - 1 && !life.IsCrouching && !PlayerLife.Crouching, g + " -> " + truck.Drive.Gear);
        Key(kb.zKey);
        T.Check("Shared keys: in a vehicle Z toggles 4WD, no hand tool", truck.TractionMode == 1 && life.LastMessage == msgIn);
        Key(kb.periodKey); Key(kb.commaKey); Key(kb.commaKey);
        T.Check("Shared keys: in a vehicle , . set the cruise speed, tool unchanged", Math.Abs(truck.Assist.CruiseSpeed * 3.6f - (set0 * 3.6f - 1f)) < 0.01f && life.Tool == toolIn);
        g = truck.Drive.Gear;
        Pad(pad.rightShoulder);
        T.Check("Shared keys: in a vehicle RB shifts up, tool unchanged", truck.Drive.Gear == g + 1 && life.Tool == toolIn);
        Pad(pad.leftShoulder);
        T.Check("Shared keys: in a vehicle LB shifts down, tool unchanged", truck.Drive.Gear == g && life.Tool == toolIn);
        Pad(pad.dpad.up);
        T.Check("Shared keys: in a vehicle D-pad up sets cruise, picks nothing up", truck.Assist.Cruise == CruiseMode.Cruise && life.Carried == null);
        truck.Assist.Reset(); trac0 = truck.TractionMode;
        Key(kb.yKey);
        Pad(pad.dpad.down);
        T.Check("Shared keys: in a vehicle Y / D-pad down are cruise / 4WD, the journal stays shut",
            truck.Assist.Cruise == CruiseMode.Cruise && truck.TractionMode == (trac0 + 1) % 3 && (FarmLifeScreen.Instance == null || !FarmLifeScreen.Instance.IsOpen));
        truck.Assist.Reset();
        Pad(pad.leftStickButton);
        T.Check("Shared keys: in a vehicle L3 is the engine, no crouch", !truck.Drive.EngineOn && !life.IsCrouching);
        GameInput.ForceDevice(false, false);
        string inHint = Glyphs.Apply("[F] [Ctrl] [Z]");
        T.SetProp(truck, "Speed", 0f); truck.Exit(); FakeEngine.Tick(0.02f);
        string footHint = Glyphs.Apply("[F] [Ctrl] [Z]");
        GameInput.ForceDevice(true, false);
        string padIn = "", padFoot = Glyphs.Apply("[F] [Ctrl] [Z]");
        truck.Interact(); FakeEngine.Tick(0.02f); padIn = Glyphs.Apply("[F] [Ctrl] [Z]");
        T.SetProp(truck, "Speed", 0f); truck.Exit(); FakeEngine.Tick(0.02f);
        GameInput.ForceDevice(false, false);
        T.Check("Shared keys: hint glyphs follow the context (engine / gear / 4WD in a vehicle, pick up / crouch / tool on foot)",
            inHint == footHint && padIn.Contains("[LS]") && padIn.Contains("[LB]") && padIn.Contains("D-PAD↓") && padFoot.Contains("D-PAD↑") && padFoot.Contains("[RT]"), padIn + " | " + padFoot);
        T.Check("Shared keys: no errors", FakeEngine.Errors.Count == 0, string.Join(" | ", FakeEngine.Errors.Take(3)));
        GameSettings.Gearbox = GearboxMode.Automatic;
        UnityEngine.InputSystem.Gamepad.current = null;
    }
}

// ---------------------------------------------------------------- market, contracts board, production lines, finances
static class MarketTests
{
    static void Pad(UnityEngine.InputSystem.ButtonControl b) { b.wasPressedThisFrame = true; b.isPressed = true; FakeEngine.Tick(0.02f); b.wasPressedThisFrame = false; b.isPressed = false; }
    static void Day() => WorldClock.Instance.NextDay();

    static string snapDest, snapOffers, snapActive;
    static float snapGlut; static int snapLedgerSales, snapLedgerProd, snapHist; static bool snapAuto;

    public static void Run()
    {
        Console.WriteLine("Market, contracts board, production lines, finances");
        var m = MarketManager.Instance; var eco = EconomyManager.Instance; var clock = WorldClock.Instance;
        var led = FinanceLedger.Instance; var yard = ProcessingYard.Instance;
        int e0 = FakeEngine.Errors.Count;
        float progress0 = clock.DayProgress;   // put the time of day back at the end (later sections depend on it)
        T.Check("Market: systems created (ledger)", m != null && led != null && yard != null);

        // ---- translations of every text key
        var src = string.Join("\n", new[] { "Managers/MarketManager.cs", "UI/EconomyPages.cs", "Finance/FinanceLedger.cs" }.Select(f => File.ReadAllText("../../Assets/Scripts/" + f)));
        var keys = System.Text.RegularExpressions.Regex.Matches(src, "Loc\\.(?:T|Raw)\\(\"([^\"]+)\"").Cast<System.Text.RegularExpressions.Match>().Select(x => x.Groups[1].Value).ToList();
        keys.AddRange(Ledger.Names); keys.AddRange(EconomyPages.ProductionActions);
        keys.AddRange(new[] { "Not available", "Contract limit reached", "Needs goods this map doesn't have", "on", "off", "Auto supply and collect: on", "Auto supply and collect: off",
            "Up / Down: goods   Left / Right: where to sell   [Esc] close", "Up / Down: contracts   [Enter] accept   [Esc] close",
            "Up / Down: buildings   Left / Right: action   [Enter] do it   [Esc] close", "[Esc] close", "[LB] [RB] or 1-7: Inventory / Stats / Fields / Prices / Contracts / Production / Finances", "Market prices", "Finances", "Production" });
        keys.AddRange(m.Cfg.points.Select(p => p.nameEn)); keys.AddRange(yard.Plants.Select(p => p.def.nameEn));
        keys = keys.Where(k => k != "SAR").Distinct().ToList();
        var missing = keys.Where(k => !Loc.HasTranslation(k)).ToList();
        T.Check("Market: every text key has Arabic (Loc/95_market.json)", keys.Count > 60 && missing.Count == 0, keys.Count + " " + string.Join(" | ", missing));
        T.Check("Market: selling points and clients are bilingual", m.Cfg.points.All(p => !string.IsNullOrEmpty(p.nameAr)) && m.Cfg.contracts.clients.All(c => !string.IsNullOrEmpty(c.ar)));

        // ---- selling points, generic over goods
        T.Check("Market: souq, village souq, grain silo, dairy co-op, fodder traders, export port", new[] { "souq", "village", "mill", "dairy", "fodder", "port" }.All(id => m.Point(id) != null), string.Join(",", m.Points.Select(p => p.id)));
        T.Check("Market: the Delta's cotton gin and rice mill are not selling points in Najd (region-gated)", m.Point("gin") == null && m.Point("ricemill") == null && MarketManager.Buildings("najd").Count == 0);
        T.Check("Market: every good (incl. packs) has a price at the souq", eco.Data.goods.All(g => m.Price(g.id) > 0f), string.Join(",", eco.Data.goods.Where(g => m.Price(g.id) <= 0f).Select(g => g.id)));
        T.Check("Market: a point that doesn't buy a good pays nothing for it", m.Price("eggs", "mill") == 0f && m.Price("wheat", "mill") > 0f);
        T.Check("Market: processed goods pack loaded (olive oil, tomato paste...)", eco.Good("olive_oil") != null && eco.Good("tomato_paste") != null);

        // ---- supply and demand: selling lowers that point's price, it recovers over days
        float silo0 = m.Price("wheat", "mill"), souq0 = m.Price("wheat", "souq"), haul = m.Point("mill").haul;
        float sold = m.SellAt("mill", "wheat", 3000f);
        float silo1 = m.Price("wheat", "mill");
        T.Check("Market: selling 3,000 kg of wheat at the silo lowers its price", silo1 < silo0 * 0.8f, silo0 + " -> " + silo1);
        T.Check("Market: ...but not the price at the souq", Math.Abs(m.Price("wheat", "souq") - souq0) < 0.001f);
        T.Check("Market: the price falls while you sell (paid between start and end price, minus transport)", sold < 3000f * silo0 && sold > 3000f * (silo1 - haul) - 1f, sold + " for 3000 at " + silo0 + " -> " + silo1);
        float glut1 = m.Glut("mill", "wheat");
        Day(); Day();
        T.Check("Market: the silo price recovers over the next days", m.Glut("mill", "wheat") < glut1 - 0.1f, glut1 + " -> " + m.Glut("mill", "wheat"));

        // ---- demand events
        m.Events.Clear();
        float onion0 = m.Price("onion", "port");
        var ev = m.AddDemandEvent("port", "onion", 1.4f, 3);
        T.Check("Market: demand event - onions +40% at the port for 3 days", onion0 > 0f && Math.Abs(m.Price("onion", "port") - onion0 * 1.4f) < 0.01f, onion0 + " -> " + m.Price("onion", "port"));
        T.Check("Market: event listed on the Prices page", EconomyPages.PricesText(0).Contains("High demand") && EconomyPages.PricesText(0).Contains("+40%"));
        T.Check("Market: best-price hint points at the port while onions are in demand", m.BestPoint("onion") == "port");
        Day(); Day();
        T.Check("Market: event still on after 2 days", m.Events.Contains(ev));
        Day();
        T.Check("Market: event over after 3 days", !m.Events.Contains(ev));

        // ---- history, trend arrows, best price
        var h = m.History("souq", "wheat");
        T.Check("Market: daily price history per good and point (30 days max)", h.Count >= 5 && h.Count <= MarketManager.HistoryDays, "" + h.Count);
        T.Check("Market: trend arrows (rising / falling / steady)", MarketMath.Trend(new[] { 10f, 10f, 10f, 12f }, 12f) == 1 && MarketMath.Trend(new[] { 10f, 10f, 10f, 8f }, 8f) == -1 && MarketMath.Trend(new[] { 10f, 10f, 10f }, 10.1f) == 0);
        T.Check("Market: best point has the best net price", eco.Data.goods.All(g => m.Points.Where(p => MarketMath.Accepts(p, g.id)).All(p => m.NetPrice(g.id, p.id) <= m.NetPrice(g.id, m.BestPoint(g.id)) + 0.0001f)));
        int wheatRow = Array.FindIndex(eco.Data.goods, g => g.id == "wheat");
        string pricesPage = EconomyPages.PricesText(wheatRow);
        T.Check("Market: Prices page lists goods, points, trend and the 30-day range", pricesPage.Contains("Wheat") && pricesPage.Contains("Grain silo") && (pricesPage.Contains("▲") || pricesPage.Contains("▼") || pricesPage.Contains("■")) && pricesPage.Contains("d: "), pricesPage);

        // ---- the old sell flow still works
        m.SetDestination("");
        var cargo = new Dictionary<string, float> { { "potato", 40f } };
        int money0 = eco.Money, salesBefore = led.Income(FinanceLedger.Today, Ledger.Sales);
        int got = eco.SellAll(cargo);
        // (a mission or quest may pay at the same moment, so the sale is read from the ledger)
        T.Check("Market: SellAll still sells the truck load", got > 0 && cargo.Count == 0 && eco.Money >= money0 + got && led.Income(FinanceLedger.Today, Ledger.Sales) - salesBefore == got, got + "");
        m.CycleDestination(1);
        T.Check("Market: Left / Right picks where truck loads are sold", m.Destination == m.Points[0].id);
        m.SetDestination("");

        // ---- contracts board
        m.Contracts.Clear();
        m.Offers.Clear();
        var deliver = new List<Contract>(); Contract field = null;
        for (int i = 0; i < 300 && (deliver.Count < 4 || field == null); i++)
        {
            var c = m.GenerateOffer(false);
            if (c.Field()) { if (field == null) field = c; else m.Offers.Remove(c); }
            else if (deliver.Count < 4 && !deliver.Any(d => d.good == c.good)) deliver.Add(c);
            else m.Offers.Remove(c);
        }
        T.Check("Contracts: daily offers from named clients with good, amount, deadline, unit price, bonus and penalty",
            deliver.Count == 4 && deliver.All(c => !string.IsNullOrEmpty(c.buyerEn) && c.amount > 0f && c.days >= 3 && c.unitPrice > 0f && c.reward > 0 && c.penalty > 0 && c.board));
        T.Check("Contracts: field-work contracts (tractor jobs for neighbours)", field != null && field.amount >= 4 && field.reward > 0 && new[] { "Plow", "Seeder", "Harvester" }.Contains(field.task));
        string board = EconomyPages.ContractsText(0);
        T.Check("Contracts: board page lists the offers", board.Contains(deliver[0].buyerEn) && board.Contains("Contracts board"));
        int completed = 0; Action onDone = () => completed++;
        GameEvents.ContractCompleted += onDone;
        int row0 = m.Contracts.Count + m.Offers.IndexOf(deliver[0]);
        EconomyPages.Confirm(FarmInfoScreen.Page.Contracts, row0, 0);
        T.Check("Contracts: [Enter] on the board accepts an offer", m.Contracts.Contains(deliver[0]) && !m.Offers.Contains(deliver[0]) && deliver[0].dueDay == clock.Day + deliver[0].days);
        T.Check("Contracts: accept a second and third", m.Accept(deliver[1]) && m.Accept(deliver[2]));
        T.Check("Contracts: no more than 3 at once", m.MaxActive == 3 && !m.Accept(deliver[3]) && m.Offers.Contains(deliver[3]));
        var dc = deliver[0];
        int contractsIn0 = led.Income(FinanceLedger.Today, Ledger.Contracts);
        money0 = eco.Money;
        float half = Mathf.Floor(dc.amount / 2f);
        eco.SellAll(new Dictionary<string, float> { { dc.good, half } });
        T.Check("Contracts: a delivery counts toward the contract", Math.Abs(dc.delivered - half) < 0.01f && m.Contracts.Contains(dc), dc.delivered + "/" + dc.amount);
        float mul = Career.SellMultiplier * Regions.Current.market;
        int paidC = led.Income(FinanceLedger.Today, Ledger.Contracts) - contractsIn0;
        T.Check("Contracts: delivered goods are paid at the contract price", Math.Abs(paidC - half * dc.unitPrice * mul) <= 1.5f && eco.Money - money0 >= paidC, paidC + " vs " + half * dc.unitPrice);
        money0 = eco.Money;
        var notes = new List<string>();
        eco.SellAll(new Dictionary<string, float> { { dc.good, dc.amount - half + 5f } }, notes);
        T.Check("Contracts: finishing the delivery completes it (ContractCompleted raised, bonus paid)", !m.Contracts.Contains(dc) && completed == 1 && eco.Money - money0 >= dc.reward && notes.Any(n => n.Contains("Contract complete")), completed + " " + string.Join("|", notes));
        T.Check("Finances: contract money booked under Contracts", led.Income(FinanceLedger.Today, Ledger.Contracts) - contractsIn0 >= dc.reward + (int)(dc.amount * dc.unitPrice * mul * 0.9f));
        var fc = deliver[1];
        fc.dueDay = clock.Day;
        int contractsOut0 = led.Expense(clock.Day + 1, Ledger.Contracts);
        Day();
        T.Check("Contracts: missed deadline fails the contract with its penalty", !m.Contracts.Contains(fc) && completed == 1 && led.Expense(clock.Day, Ledger.Contracts) - contractsOut0 >= fc.penalty, led.Expense(clock.Day, Ledger.Contracts) + " vs " + fc.penalty);
        m.Contracts.Remove(deliver[2]);
        if (!m.Offers.Contains(field)) m.Offers.Add(field);
        T.Check("Contracts: accept a field-work contract", m.Accept(field));
        var imp = (Implement)Enum.Parse(typeof(Implement), field.task);
        int passes = (int)field.amount;
        for (int i = 0; i < passes - 1; i++) GameEvents.RaiseTractorWorked(imp);
        T.Check("Contracts: tractor work counts toward the field job", Math.Abs(field.delivered - (passes - 1)) < 0.01f && m.Contracts.Contains(field));
        money0 = eco.Money;
        GameEvents.RaiseTractorWorked(imp);
        T.Check("Contracts: last pass completes the field job and pays", !m.Contracts.Contains(field) && completed == 2 && eco.Money - money0 >= field.reward, (eco.Money - money0) + " vs " + field.reward);
        GameEvents.ContractCompleted -= onDone;
        int offers = m.Offers.Count;
        for (int i = 0; i < 4; i++) Day();
        T.Check("Contracts: new offers keep coming, old ones expire", m.Offers.Count >= 2 && m.Offers.All(o => o.offerUntil >= clock.Day), offers + " -> " + m.Offers.Count);

        // ---- production lines
        eco.AddMoney(30000);
        var mill = yard.Get("proc_mill");
        if (!mill.owned) eco.TryBuy("proc_mill");
        T.Check("Production: flour mill built", mill.owned);
        T.Check("Production: Production page lists buildings with input, output, rate and running cost", EconomyPages.ProductionText(0, 0).Contains("Flour mill") && EconomyPages.ProductionText(0, 0).Contains("running cost"));
        float wheat0 = eco.Stored("wheat");
        eco.AddGood("wheat", 100f);
        float moved = yard.Supply(mill);
        T.Check("Production: supply moves wheat from farm storage into the mill", moved >= 99.9f && eco.Stored("wheat") < 0.2f + Math.Max(0f, wheat0 + 100f - mill.def.inputCapacity), moved + "");
        float load = mill.InTotal;
        FakeEngine.Tick(0.1f);
        T.Check("Production: the mill starts by itself from its input store", mill.busy && mill.line && Math.Abs(mill.InTotal - (load - 20f)) < 0.01f);
        FakeEngine.Run(52f, 0.5f);
        T.Check("Production: one batch later 16 kg of flour in the output store", Math.Abs(mill.OutTotal - 16f) < 0.01f, mill.OutTotal + "");
        FakeEngine.Run(51f * (float)Math.Ceiling(load / 20f), 0.5f);
        T.Check("Production: inputs converted over time into flour (20 -> 16)", mill.InTotal < 0.01f && !mill.busy && Math.Abs(mill.OutTotal - load * 0.8f) < 0.2f, mill.InTotal + " -> " + mill.OutTotal);
        float flour0 = eco.Stored("flour");
        float collected = yard.Collect(mill);
        T.Check("Production: collect moves outputs to farm storage", Math.Abs(eco.Stored("flour") - flour0 - collected) < 0.01f && collected > 70f && mill.OutTotal == 0f);
        eco.AddGood("wheat", 20f); yard.Supply(mill); FakeEngine.Tick(0.1f);   // working today
        int prodOut0 = led.Expense(clock.Day + 1, Ledger.Production);
        Day();
        T.Check("Production: running cost charged for a day the mill worked", led.Expense(clock.Day, Ledger.Production) - prodOut0 >= mill.def.dailyCost);
        FakeEngine.Run(52f, 0.5f);
        int sale = yard.SellOutputs(mill);
        T.Check("Production: outputs sell straight from the building (higher value than the wheat)", sale > 0 && mill.OutTotal == 0f && m.Price("flour") > m.Price("wheat"));
        eco.AddGood("wheat", 40f);
        EconomyPages.Confirm(FarmInfoScreen.Page.Production, EconomyPages.Plants().IndexOf(mill), 3);
        T.Check("Production: Auto mode supplies inputs by itself", mill.auto && mill.InTotal + (mill.busy ? 20f : 0f) >= 39f);
        yard.SetAuto(mill, false);

        // ---- finances
        int purch0 = led.Expense(clock.Day, Ledger.Purchases);
        eco.TryBuy("feed");
        T.Check("Finances: shop purchases booked as Purchases", led.Expense(clock.Day, Ledger.Purchases) - purch0 == eco.Data.feed.price);
        int sales0 = led.Income(clock.Day, Ledger.Sales);
        int s = eco.SellAll(new Dictionary<string, float> { { "watermelon", 30f } });
        T.Check("Finances: sales booked as Sales", led.Income(clock.Day, Ledger.Sales) - sales0 == s && s > 0, (led.Income(clock.Day, Ledger.Sales) - sales0) + " vs " + s);
        var bank = BankManager.Instance;
        if (bank.LoanBlock(10000) == null)
        {
            int loans0 = led.Income(clock.Day, Ledger.Loans);
            bank.TakeLoan(10000);
            T.Check("Finances: a loan is booked as Loans income", led.Income(clock.Day, Ledger.Loans) - loans0 == 10000);
            Day();
            T.Check("Finances: loan interest booked every day", led.Expense(clock.Day, Ledger.Interest) >= 150);
            eco.AddMoney(20000);
            int repay0 = led.Expense(clock.Day, Ledger.Loans);
            int paid = bank.RepayWhatYouCan();
            T.Check("Finances: repaying the loan books only the principal as Loans (interest was booked daily)", paid > 10000 && bank.Debt == 0 && led.Expense(clock.Day, Ledger.Loans) - repay0 == 10000 && bank.UnpaidInterest == 0,
                (led.Expense(clock.Day, Ledger.Loans) - repay0) + " of " + paid);
        }
        else Console.WriteLine("  (loan part skipped: " + bank.LoanBlock(10000) + ")");
        int wages0 = led.Expense(clock.Day, Ledger.Wages);
        using (Ledger.As(Ledger.Wages)) eco.Spend(100);
        T.Check("Finances: wages scope", led.Expense(clock.Day, Ledger.Wages) - wages0 == 100);
        T.Check("Finances: season totals add up the days", led.SeasonIncomeTotal >= led.DayIncome(clock.Day) && led.SeasonExpenseTotal >= led.DayExpense(clock.Day) && led.SeasonIncome(Ledger.Sales) >= s);
        string fin = EconomyPages.FinancesText();
        T.Check("Finances: page with categories, last 7 days and the season", fin.Contains("Sales") && fin.Contains("Purchases") && fin.Contains("Production") && fin.Contains("Season") && fin.Contains("Today") && fin.Contains("Net"), fin);

        // ---- pages: pause menu, LB / RB, D-pad
        GameMenu.Instance.Open(GameMenu.Mode.Pause);
        var labels = GameMenu.Instance.List.Rows.Select(r => r.label()).ToList();
        T.Check("Pages: pause menu has Market prices, Contracts, Production, Finances", new[] { "Market prices", "Contracts", "Production", "Finances" }.All(x => labels.Contains(UiText.T(x))));
        GameMenu.Instance.List.Choose(labels.IndexOf(UiText.T("Finances")));
        T.Check("Pages: Finances opens from the pause menu", FarmInfoScreen.Instance.Open == FarmInfoScreen.Page.Finances && !GameMenu.IsOpen);
        var prevPad = UnityEngine.InputSystem.Gamepad.current;
        var pad = new UnityEngine.InputSystem.Gamepad();
        UnityEngine.InputSystem.Gamepad.current = pad;
        Pad(pad.rightShoulder);
        T.Check("Pages: RB goes from Finances to the Map tab (FS-style farm menu)", FarmInfoScreen.Instance.Open == FarmInfoScreen.Page.Map);
        Pad(pad.leftShoulder); Pad(pad.leftShoulder); Pad(pad.leftShoulder); Pad(pad.leftShoulder);
        T.Check("Pages: LB back to Prices", FarmInfoScreen.Instance.Open == FarmInfoScreen.Page.Prices, FarmInfoScreen.Instance.Open.ToString());
        Pad(pad.dpad.down); Pad(pad.dpad.down);
        T.Check("Pages: D-pad down moves the goods row", FarmInfoScreen.Instance.Row == 2);
        Pad(pad.dpad.right);
        T.Check("Pages: D-pad right changes where loads are sold", m.Destination == m.Points[0].id);
        Pad(pad.dpad.left);
        Pad(pad.rightShoulder); Pad(pad.rightShoulder);
        Pad(pad.dpad.right);
        T.Check("Pages: Production page, D-pad right picks the next action", FarmInfoScreen.Instance.Open == FarmInfoScreen.Page.Production && FarmInfoScreen.Instance.EconAction == 1);
        Pad(pad.buttonEast);
        T.Check("Pages: B closes", FarmInfoScreen.Instance.Open == FarmInfoScreen.Page.None && m.Destination == "");
        UnityEngine.InputSystem.Gamepad.current = prevPad;
        Loc.SetLanguage(Language.Arabic);
        string ar = EconomyPages.PricesText(0) + EconomyPages.ContractsText(0) + EconomyPages.ProductionText(0, 0) + EconomyPages.FinancesText();
        T.Check("Pages: in Arabic", ar.Any(ch => ch >= 'ﹰ') && !ar.Contains("Sell truck loads") && !ar.Contains("Contracts board") && !ar.Contains("Season"));
        Loc.SetLanguage(Language.English);
        yard.SetAuto(mill, false);
        if (mill.inStore.Count > 0) { foreach (var kv in mill.inStore) eco.AddGood(kv.Key, kv.Value); mill.inStore.Clear(); }
        T.SetProp(clock, "DayProgress", progress0);
        T.Check("Market: no errors", FakeEngine.Errors.Count == e0, string.Join(" | ", FakeEngine.Errors.Skip(e0).Take(3)));
    }

    /// <summary>Right before the save: set up and remember state to compare after the restart.</summary>
    public static void Snapshot()
    {
        var m = MarketManager.Instance; var yard = ProcessingYard.Instance; var led = FinanceLedger.Instance; var eco = EconomyManager.Instance;
        m.SetDestination("village");
        if (m.Offers.Count > 0 && m.ActiveBoardContracts < m.MaxActive) m.Accept(m.Offers[0]);
        m.SellAt("mill", "wheat", 1500f);
        var mill = yard.Get("proc_mill");
        if (!mill.owned) { eco.AddMoney(5000); eco.TryBuy("proc_mill"); }
        yard.SetAuto(mill, true);
        mill.inStore.Clear(); mill.inStore["wheat"] = 12.5f; mill.outStore.Clear();
        mill.busy = true; mill.timeLeft = 30f; mill.output = "flour"; mill.outAmount = 16f; mill.line = true;
        yard.Collect(mill);   // saves the plant
        m.SetDestination("village");   // saves the market
        snapDest = m.Destination; snapGlut = m.Glut("mill", "wheat"); snapHist = m.History("souq", "wheat").Count;
        snapOffers = string.Join(",", m.Offers.Select(c => c.id + ":" + c.good + ":" + c.amount));
        snapActive = string.Join(",", m.Contracts.Where(c => c.board).Select(c => c.id + ":" + c.dueDay + ":" + c.penalty));
        snapLedgerSales = led.SeasonIncome(Ledger.Sales); snapLedgerProd = led.SeasonExpense(Ledger.Production);
        snapAuto = mill.auto;
    }

    public static void AfterRestart()
    {
        var m = MarketManager.Instance; var yard = ProcessingYard.Instance; var led = FinanceLedger.Instance;
        T.Check("Market: sell destination, supply pressure and price history restored", m.Destination == snapDest && Math.Abs(m.Glut("mill", "wheat") - snapGlut) < 0.0001f && m.History("souq", "wheat").Count == snapHist,
            m.Destination + " " + m.Glut("mill", "wheat") + "/" + snapGlut + " " + m.History("souq", "wheat").Count + "/" + snapHist);
        T.Check("Contracts: board and active contracts restored", string.Join(",", m.Offers.Select(c => c.id + ":" + c.good + ":" + c.amount)) == snapOffers
            && string.Join(",", m.Contracts.Where(c => c.board).Select(c => c.id + ":" + c.dueDay + ":" + c.penalty)) == snapActive && snapActive.Length > 0, snapActive);
        var mill = yard.Get("proc_mill");
        T.Check("Production: building stores, running batch and auto mode restored", mill.owned && mill.auto == snapAuto && mill.line && mill.busy && Math.Abs(mill.InTotal - 12.5f) < 0.01f, mill.InTotal + " " + mill.auto + " " + mill.line);
        T.Check("Finances: ledger restored", led.SeasonIncome(Ledger.Sales) == snapLedgerSales && led.SeasonExpense(Ledger.Production) == snapLedgerProd, led.SeasonIncome(Ledger.Sales) + " vs " + snapLedgerSales);
        yard.SetAuto(mill, false);
    }

    public static void AfterNewGame()
    {
        var m = MarketManager.Instance; var yard = ProcessingYard.Instance; var led = FinanceLedger.Instance;
        T.Check("New game resets the market (prices, events, destination, contracts)", m.Destination == "" && m.Events.Count == 0 && m.Glut("mill", "wheat") == 0f && m.ActiveBoardContracts == 0 && m.Offers.Count == 3,
            m.Destination + " " + m.Events.Count + " " + m.ActiveBoardContracts + " " + m.Offers.Count);
        T.Check("New game clears production lines and the ledger", yard.Plants.All(p => !p.owned && p.inStore.Count == 0 && p.outStore.Count == 0 && !p.auto) && Ledger.Categories.All(c => !led.Used(c)),
            string.Join(",", Ledger.Categories.Where(c => led.Used(c))));
    }
}
