// Farming Simulator parity features: FS-style HUD layout (clock block, context help, gauge cluster, minimap with field
// numbers), Tab vehicle switching, implement attach / detach / lower / raise / fold (keyboard + gamepad), the tabbed
// farm menu (Map, Calendar, Weather forecast, Vehicles, Animals, Help), the GPS route by road, dealer paint / wheels,
// saving, restart and new game.
using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;

static class ParityTests
{
    static Keyboard kb;
    static Gamepad pad;

    static void Tap(KeyControl k) { k.wasPressedThisFrame = true; k.isPressed = true; FakeEngine.Tick(0.02f); k.wasPressedThisFrame = false; k.isPressed = false; FakeEngine.Tick(0.02f); }
    static void Hold(KeyControl k, float seconds) { k.wasPressedThisFrame = true; k.isPressed = true; FakeEngine.Tick(0.02f); k.wasPressedThisFrame = false; for (float t = 0; t < seconds; t += 0.05f) FakeEngine.Tick(0.05f); k.isPressed = false; FakeEngine.Tick(0.02f); }
    static void TapPad(ButtonControl b) { b.wasPressedThisFrame = true; b.isPressed = true; FakeEngine.Tick(0.02f); b.wasPressedThisFrame = false; b.isPressed = false; FakeEngine.Tick(0.02f); }
    static void HoldPad(ButtonControl b, float seconds) { b.wasPressedThisFrame = true; b.isPressed = true; FakeEngine.Tick(0.02f); b.wasPressedThisFrame = false; for (float t = 0; t < seconds; t += 0.05f) FakeEngine.Tick(0.05f); b.isPressed = false; FakeEngine.Tick(0.02f); }
    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    static void Boot()
    {
        Program.BuildWorld();
        FakeEngine.Run(3f);
        for (int i = 0; i < 400 && LoadingScreen.Visible; i++) FakeEngine.Tick(0.1f);
        kb = new Keyboard();
        foreach (var f in typeof(Keyboard).GetFields()) if (f.FieldType == typeof(KeyControl)) f.SetValue(kb, new KeyControl());
        Keyboard.current = kb;
        pad = null; Gamepad.current = null;
        GameInput.ForceDevice(false, false);
    }

    static void NewGame()
    {
        var hud = UnityEngine.Object.FindAnyObjectByType<GameHUD>();
        typeof(GameHUD).GetMethod("NewGame", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(hud, null);
        FakeEngine.Run(0.5f);
        CloseIntro();
    }

    /// <summary>The fake model loader gives the tractor no paint materials or wheels (real Kenney models have both): add a
    /// primitive livery part and a wheel like the primitive fallback body has, so paint / wheel configuration can be checked.</summary>
    static void GiveTestLivery(Vehicle v)
    {
        if (v.transform.Find("Test livery") == null)
            Build.Part(PrimitiveType.Cube, v.transform, new Vector3(0, 1f, 0), Vector3.one, VehicleVisuals.Paint(VehicleVisuals.TractorRed), "Test livery");
        if (v.wheels == null || v.wheels.Length == 0 || v.wheels[0] == null)
        {
            var w = new GameObject("Test wheel").transform; w.SetParent(v.transform, false); w.localScale = new Vector3(0.4f, 1f, 1f);
            v.wheels = new[] { w };
        }
        VehicleConfig.Apply(v);
    }

    static void CloseIntro()
    {
        var intro = UnityEngine.Object.FindAnyObjectByType<StoryIntro>();
        if (intro != null && UIManager.Instance.IsUIOpened) typeof(StoryIntro).GetMethod("Close", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(intro, null);
        FakeEngine.Run(0.2f);
    }

    /// <summary>Park the tractor so its hitch sits on a point (facing its current way).</summary>
    static void HitchAt(Tractor t, Vector3 p)
    {
        Vector3 off = t.HitchPoint - t.transform.position;
        t.transform.position = p - off;
        T.SetProp(t, "Speed", 0f);
    }

    public static void Run()
    {
        Console.WriteLine("Farming Simulator parity: HUD, vehicle switching, implements, farm menu, GPS route, dealer");
        Boot();
        Loc.SetLanguage(Language.English);
        NewGame();
        int e0 = FakeEngine.Errors.Count;
        var hud = GameHUD.Instance;

        // ---- HUD layout
        FakeEngine.Run(0.6f);
        T.Check("HUD: clock block top-right with time, day, season, weather, temperature and money", hud.ClockPanel.anchorMin.x == 1f && hud.ClockPanel.anchorMin.y == 1f
            && GameHUD.ClockText().Contains(WorldClock.Instance.TimeText()) && GameHUD.ClockText().Contains("Spring") && GameHUD.ClockText().Contains(" C") && GameHUD.ClockText().Contains("SAR"), GameHUD.ClockText());
        T.Check("HUD: context help box top-left with the on-foot keys", hud.ContextPanel.anchorMin.x == 0f && hud.ContextPanel.anchorMin.y == 1f
            && hud.ContextText.Contains("On foot") && hud.ContextText.Contains("[Tab]") && hud.ContextText.Contains("[E]"), hud.ContextText);
        T.Check("HUD: minimap bottom-left", Minimap.Instance.MiniRoot.anchorMin.x == 0f && Minimap.Instance.MiniRoot.anchorMin.y == 0f);
        Minimap.Instance.ScanWorld();
        T.Check("HUD: field plots carry their field number on the map", FieldManager.Instance.Plots.All(p => p.GetComponent<MinimapIcon>() != null && p.GetComponent<MinimapIcon>().letter == (p.index + 1).ToString()));
        T.Check("HUD: gauge cluster hidden on foot", !hud.GaugesVisible);
        Loc.SetLanguage(Language.Arabic);
        FakeEngine.Run(0.3f);
        T.Check("HUD (Arabic): mirrored - clock left, context help right, minimap bottom-right", hud.ClockPanel.anchorMin.x == 0f && hud.ContextPanel.anchorMin.x == 1f && Minimap.Instance.MiniRoot.anchorMin.x == 1f
            && hud.ContextText.Any(c => c >= 'ﭐ'), hud.ContextText);
        Loc.SetLanguage(Language.English);
        FakeEngine.Run(0.3f);

        // ---- Tab: vehicle switching
        var tractor = Tractor.Instance; var truck = FarmTruck.Instance;
        var player = GameObject.Find("PlayerCapsule").transform;
        player.position = tractor.transform.position + tractor.transform.right * 3f;
        Tap(kb.tabKey);
        T.Check("Tab on foot: into the nearest vehicle (the tractor)", Vehicle.Current == tractor, Vehicle.Current == null ? "none sw=" + (VehicleSwitcher.Instance != null) + " ui=" + UIManager.Instance?.IsUIOpened + " menu=" + GameMenu.IsOpen + " ts=" + Time.timeScale + " n=" + VehicleSwitcher.All().Count + " fis=" + FarmInfoScreen.Instance?.Open + " typing=" + NetSession.Typing : Vehicle.Current.name);
        FakeEngine.Run(0.3f);
        T.Check("HUD: gauge cluster bottom-right while driving, fuel bar = fuel", hud.GaugesVisible && hud.GaugePanel.anchorMin.x == 1f && hud.GaugePanel.anchorMin.y == 0f
            && Math.Abs(hud.FuelBar - GameHUD.FuelFraction(tractor)) < 0.01f, hud.FuelBar + " vs " + GameHUD.FuelFraction(tractor));
        T.Check("HUD: tractor context help lists implement keys", hud.ContextText.Contains("Tractor") && hud.ContextText.Contains("[V]") && hud.ContextText.Contains("[Q]"), hud.ContextText);
        Tap(kb.tabKey);
        T.Check("Tab in a vehicle: next vehicle (the truck), tractor left parked", Vehicle.Current == truck && !tractor.IsDriving);
        Tap(kb.tabKey);
        T.Check("Tab cycles back to the tractor", Vehicle.Current == tractor && VehicleSwitcher.Instance.Switches >= 3);
        GameMenu.Instance.Open(GameMenu.Mode.Pause);
        FakeEngine.Tick(0.02f);
        Tap(kb.tabKey);
        T.Check("Tab does nothing while a menu is open", Vehicle.Current == tractor);
        GameMenu.Instance.Close();
        FakeEngine.Tick(0.02f);

        // ---- implements: detach, attach, lower, raise, fold
        T.Check("Implements: new game starts with the plow attached and lowered, seeder and harvester in the yard",
            tractor.ActiveImplement == Implement.Plow && tractor.ImplementLowered && ParkedImplement.All.Count == 2 && ParkedImplement.All.All(p => p.kind != Implement.Plow), tractor.ActiveImplement + " " + ParkedImplement.All.Count);
        int changes = 0; Action<Tractor> onChange = t => changes++; Tractor.ImplementChanged += onChange;
        tractor.transform.position = Tractor.Instance.transform.position;
        T.SetProp(tractor, "Speed", 0f);
        Vector3 hitchBefore = tractor.HitchPoint;
        Tap(kb.qKey);
        var plowGo = tractor.Visual(Implement.Plow);
        T.Check("Q detaches the implement where it is (stands on the ground, on the map)", tractor.ActiveImplement == Implement.None && plowGo.transform.parent == null && ParkedImplement.All.Count == 3
            && Flat(plowGo.transform.position - hitchBefore).magnitude < 0.5f && plowGo.GetComponent<MinimapIcon>() != null && plowGo.GetComponent<MinimapIcon>().enabled, tractor.ActiveImplement + "");
        Tap(kb.qKey);
        T.Check("Q attaches the implement in reach; it comes up raised", tractor.ActiveImplement == Implement.Plow && !tractor.ImplementLowered && plowGo.transform.parent == tractor.hitch && ParkedImplement.All.Count == 2);
        T.Check("Raised implement sits higher on the hitch", plowGo.transform.localPosition.y > 0.2f);
        var plot = FieldManager.Instance.Plots[2];
        HitchAt(tractor, plot.transform.position);
        T.SetProp(tractor, "Speed", 3f); T.Set(tractor, "workCheckTime", -1f);
        T.Check("A raised implement doesn't work the field", !tractor.Working);
        Tap(kb.vKey);
        T.SetProp(tractor, "Speed", 3f); T.Set(tractor, "workCheckTime", -1f);
        T.Check("V lowers it: now working over the plot, full working width", tractor.ImplementLowered && tractor.Working && Math.Abs(tractor.WorkingWidth - 2.4f) < 0.01f, tractor.ImplementLowered + " " + tractor.Working);
        int working = 0; Action<Tractor> onWork = t => working++; Tractor.ImplementWorking += onWork;
        HitchAt(tractor, plot.transform.position); T.SetProp(tractor, "Speed", 3f); T.Set(tractor, "workCheckTime", -1f);
        FakeEngine.Tick(0.02f);
        Tractor.ImplementWorking -= onWork;
        T.Check("FarmCore hook: ImplementWorking fires while the implement is in the ground", working > 0);
        T.SetProp(tractor, "Speed", 0f);
        Hold(kb.vKey, 0.7f);
        T.Check("Hold V folds it (raised first, zero working width)", tractor.ImplementFolded && !tractor.ImplementLowered && tractor.WorkingWidth == 0f && plowGo.transform.localScale.x < 0.6f);
        Tap(kb.vKey);
        T.Check("V on a folded implement unfolds and lowers it", !tractor.ImplementFolded && tractor.ImplementLowered);
        Tap(kb.vKey);
        T.SetProp(tractor, "Speed", 8f);
        FakeEngine.Tick(0.02f);
        T.Check("Auto fold: a raised implement folds itself at road speed", tractor.ImplementFolded, tractor.Speed * 3.6f + " km/h");
        T.SetProp(tractor, "Speed", 0f);
        T.Check("ImplementChanged fired for each change", changes >= 6, changes + "");
        Tractor.ImplementChanged -= onChange;
        // gamepad: tap Y lowers / raises, hold Y attaches / detaches
        pad = new Gamepad(); Gamepad.current = pad;
        GameInput.ForceDevice(true, false);
        bool lowBefore = tractor.ImplementLowered;
        TapPad(pad.buttonNorth);
        T.Check("Gamepad: tap Y lowers / raises", tractor.ImplementLowered != lowBefore && tractor.ActiveImplement == Implement.Plow);
        T.Check("Gamepad: prompts show Y for V in a vehicle and the context box says hold", Glyphs.Apply("[V] x").Contains(">Y<") && hud != null && ContextHelp.Text().Contains("Hold"), ContextHelp.Text());
        T.SetProp(tractor, "Speed", 0f);
        HoldPad(pad.buttonNorth, 0.7f);
        T.Check("Gamepad: hold Y detaches", tractor.ActiveImplement == Implement.None);
        GameInput.ForceDevice(false, false);
        Gamepad.current = null;
        // out of reach, then the seeder in the yard
        tractor.transform.position += tractor.transform.forward * 30f;
        T.SetProp(tractor, "Speed", 0f);
        Tap(kb.qKey);
        T.Check("Q with nothing in reach attaches nothing", tractor.ActiveImplement == Implement.None);
        var seeder = ParkedImplement.All.First(p => p.kind == Implement.Seeder);
        HitchAt(tractor, seeder.HitchPoint + tractor.transform.forward * 1.5f);
        T.Check("Context help names the implement in reach", ContextHelp.Text().Contains("Seeder"), ContextHelp.Text());
        Tap(kb.qKey);
        T.Check("Reversing up to the seeder in the yard and pressing Q attaches it", tractor.ActiveImplement == Implement.Seeder);
        Tap(kb.vKey);
        Vector3 plowPos = plowGo.transform.position;

        // ---- dealer: paint and wheels at a workshop
        EconomyManager.Instance.AddMoney(20000);
        int money = EconomyManager.Instance.Money;
        GiveTestLivery(tractor);
        Color factory = VehicleConfig.BodyColor(tractor);
        tractor.transform.position = DeliveryZone.LoadingBay.transform.position + new Vector3(60f, 0f, 0f);
        T.Check("Dealer: no configuration away from a workshop", !VehicleConfig.BuyPaint(tractor, 2) && EconomyManager.Instance.Money == money);
        tractor.transform.position = DeliveryZone.LoadingBay.transform.position;
        T.SetProp(tractor, "Speed", 0f);
        T.Check("Dealer: repaint at the workshop (palm green), paid", VehicleConfig.BuyPaint(tractor, 2) && VehicleConfig.Paint("tractor") == 2 && EconomyManager.Instance.Money == money - VehicleConfig.PaintCost
            && Math.Abs(VehicleConfig.BodyColor(tractor).g - VehicleConfig.PaintColors[2].g) < 0.05f && !VehicleConfig.BodyColor(tractor).Equals(factory), VehicleConfig.BodyColor(tractor) + " vs " + factory);
        GameSettings.LowTyrePressure = false;
        bool boughtWheels = VehicleConfig.BuyWheels(tractor, VehicleConfig.WheelType.Wide);
        T.Check("Dealer: wide flotation tyres = low pressure always, wider wheels", boughtWheels && tractor.Drive.LowPressure
            && Math.Abs(VehicleConfig.WheelWidthFactor(tractor) - 1.3f) < 0.01f, boughtWheels + " " + tractor.Drive.LowPressure + " " + VehicleConfig.WheelWidthFactor(tractor));

        // ---- farm menu tabs
        tractor.Exit();
        FakeEngine.Tick(0.02f);
        var fis = FarmInfoScreen.Instance;
        fis.Show(FarmInfoScreen.Page.Map);
        FakeEngine.Tick(0.02f);
        string mapText = MenuPages.MapText(0);
        T.Check("Menu: Map tab lists fields with number and state", mapText.Contains("Field 1") && mapText.Contains("Field 6") && fis.TabStrip().Contains("Calendar"), mapText);
        Tap(kb.eKey);
        T.Check("Menu: E switches to the next tab (Calendar)", fis.Open == FarmInfoScreen.Page.Calendar);
        string cal = MenuPages.CalendarText(0);
        T.Check("Menu: Calendar shows sow / harvest seasons per crop", cal.Contains("Wheat") && cal.Contains("S") && cal.Contains("H") && FarmMenuData.Calendar.Rows().All(r => r.sow.Any(x => x) && r.harvest.Any(x => x)), cal);
        Tap(kb.eKey);
        T.Check("Menu: Weather tab with a 5-day forecast and temperatures", fis.Open == FarmInfoScreen.Page.Weather && WorldClock.Instance.Forecast.Count == 5
            && MenuPages.WeatherText().Split('\n').Count(l => l.Contains(" C")) == 6, MenuPages.WeatherText());
        var tomorrow = WorldClock.Instance.Forecast[0];
        WorldClock.Instance.NextDay();
        T.Check("Forecast comes true: tomorrow's weather is the forecast", WorldClock.Instance.Weather == tomorrow && WorldClock.Instance.Forecast.Count == 5, tomorrow + " vs " + WorldClock.Instance.Weather);
        T.Check("Temperatures: summer heatwave hotter than a rainy winter day", WorldClock.Temperature(Season.Summer, Weather.Heatwave).x > WorldClock.Temperature(Season.Winter, Weather.Rain).x + 20f);
        Tap(kb.qKey);
        T.Check("Menu: Q goes back a tab", fis.Open == FarmInfoScreen.Page.Calendar);
        fis.Show(FarmInfoScreen.Page.Vehicles);
        FakeEngine.Tick(0.02f);
        T.Check("Menu: Vehicles tab lists the vehicles and the implements", MenuPages.RowCount(FarmInfoScreen.Page.Vehicles) == 5 && MenuPages.VehiclesText(0, 0).Contains("Tractor") && MenuPages.VehiclesText(0, 0).Contains("Farm truck")
            && MenuPages.VehiclesText(0, 0).Contains("Palm green") && MenuPages.VehiclesText(0, 0).Contains("Harvester"), MenuPages.VehiclesText(0, 0));
        // sell the parked harvester, buy it back
        int harvesterRow = 2 + Array.IndexOf(MenuPages.Implements, Implement.Harvester);
        money = EconomyManager.Instance.Money;
        MenuPages.Confirm(FarmInfoScreen.Page.Vehicles, harvesterRow, 1);
        var harvGo = tractor.Visual(Implement.Harvester);
        T.Check("Vehicles: sell a parked implement (half price), it leaves the yard", ImplementYard.IsSold(Implement.Harvester) && EconomyManager.Instance.Money == money + ImplementYard.SellValue(Implement.Harvester)
            && !harvGo.activeSelf && ParkedImplement.All.All(p => p.kind != Implement.Harvester));
        int seederRow = 2 + Array.IndexOf(MenuPages.Implements, Implement.Seeder);
        money = EconomyManager.Instance.Money;
        MenuPages.Confirm(FarmInfoScreen.Page.Vehicles, seederRow, 1);
        T.Check("Vehicles: the attached implement can't be sold", !ImplementYard.IsSold(Implement.Seeder) && EconomyManager.Instance.Money == money);
        MenuPages.Confirm(FarmInfoScreen.Page.Vehicles, harvesterRow, 2);
        T.Check("Vehicles: buy it back - delivered to the yard", !ImplementYard.IsSold(Implement.Harvester) && harvGo.activeSelf && EconomyManager.Instance.Money == money - ImplementYard.Price(Implement.Harvester)
            && Flat(harvGo.transform.position - ImplementYard.YardSlot(Implement.Harvester)).magnitude < 0.5f);
        // gamepad: D-pad picks the row / action, A enters the vehicle
        pad = new Gamepad(); Gamepad.current = pad;
        int rowTractor = VehicleSwitcher.All().IndexOf(tractor);
        fis.Show(FarmInfoScreen.Page.Vehicles);
        FakeEngine.Tick(0.02f);
        for (int i = 0; i < rowTractor; i++) TapPad(pad.dpad.down);
        TapPad(pad.dpad.right); TapPad(pad.dpad.left);
        TapPad(pad.buttonSouth);
        T.Check("Vehicles (gamepad): A on 'Enter vehicle' closes the menu and gets in", fis.Open == FarmInfoScreen.Page.None && Vehicle.Current == tractor, fis.Open + " " + (Vehicle.Current == null ? "none" : Vehicle.Current.name));
        tractor.Exit();
        FakeEngine.Tick(0.02f);
        fis.Show(FarmInfoScreen.Page.Help);
        FakeEngine.Tick(0.02f);
        TapPad(pad.rightShoulder);
        T.Check("Menu: RB wraps from Help to Inventory", fis.Open == FarmInfoScreen.Page.Inventory);
        Gamepad.current = null;
        fis.Close();
        GameInput.ForceDevice(false, false);
        string help = MenuPages.HelpText(2);
        T.Check("Help: encyclopedia topics with text (implements topic explains Q / V and the gamepad Y)", MenuPages.RowCount(FarmInfoScreen.Page.Help) == 9 && help.Contains("Implements") && help.Contains("press Q") && help.Contains("V lowers") && help.Contains("hold Y") && help.Length > 200, help);
        T.Check("Animals tab: herds, products, feed and pen state", MenuPages.AnimalsText(0).Contains("Feed") && MenuPages.AnimalsText(0).Contains("Water"), MenuPages.AnimalsText(0));
        GameMenu.Instance.Open(GameMenu.Mode.Pause);
        var labels = GameMenu.Instance.List.Rows.Select(r => r.label()).ToList();
        int fm = labels.IndexOf(UiText.T("Farm menu (all tabs)"));
        if (fm >= 0) GameMenu.Instance.List.Choose(fm);
        T.Check("Pause menu: 'Farm menu (all tabs)' opens the Map tab", fm >= 0 && fis.Open == FarmInfoScreen.Page.Map && !GameMenu.IsOpen);
        Loc.SetLanguage(Language.Arabic);
        int eAr = FakeEngine.Errors.Count;
        foreach (FarmInfoScreen.Page pg in new[] { FarmInfoScreen.Page.Map, FarmInfoScreen.Page.Calendar, FarmInfoScreen.Page.Weather, FarmInfoScreen.Page.Vehicles, FarmInfoScreen.Page.Animals, FarmInfoScreen.Page.Help }) { fis.Show(pg); FakeEngine.Tick(0.02f); }
        T.Check("Menu tabs in Arabic: translated, no errors", FakeEngine.Errors.Count == eAr && MenuPages.VehiclesText(0, 0).Any(c => c >= 'ﭐ') && MenuPages.HelpText(0).Any(c => c >= 'ﭐ'));
        Loc.SetLanguage(Language.English);
        fis.Close();

        // ---- statistics
        tractor.Interact();
        float d0 = VehicleSwitcher.Instance.Distance("tractor");
        for (int i = 0; i < 10; i++) { T.SetProp(tractor, "Speed", 5f); FakeEngine.Tick(0.1f); }
        T.Check("Statistics: distance driven per vehicle", VehicleSwitcher.Instance.Distance("tractor") > d0 + 1f && FarmInfoScreen.StatsText().Contains("Distance driven"), VehicleSwitcher.Instance.Distance("tractor") + "");
        T.SetProp(tractor, "Speed", 0f);
        tractor.Exit();

        // ---- GPS route by road
        var w = OpenWorld.Instance;
        var wm = WorldMap.Instance;
        var gps = GpsRoute.Instance;
        T.Check("GPS: systems present", w != null && w.IsBuilt && wm != null && gps != null);
        if (w != null && w.IsBuilt && wm != null && gps != null)
        {
            var town = w.Layout.Poi("town");
            player.position = w.OnGround(DeliveryZone.LoadingBay.transform.position + new Vector3(4f, 0f, 0f), 1f);
            wm.SetWaypoint(town.pos, town);
            FakeEngine.Run(1.2f);
            float straight = Flat(town.pos - player.position).magnitude;
            var pts = gps.Points;
            float maxGap = 0f; for (int i = 2; i < pts.Count - 1; i++) maxGap = Math.Max(maxGap, Flat(pts[i] - pts[i - 1]).magnitude);
            T.Check("GPS: route to the town follows the roads (longer than straight, small steps along the road)", gps.HasRoute && gps.RoadsUsed >= 2 && gps.RouteLength >= straight * 0.99f && gps.RouteLength < straight * 3f && maxGap < 40f,
                gps.RoadsUsed + " roads, " + gps.RouteLength + " m vs " + straight + ", gap " + maxGap);
            T.Check("GPS: starts at you and ends at the waypoint", Flat(pts[0] - player.position).magnitude < 15f && Flat(pts[pts.Count - 1] - town.pos).magnitude < 1f);
            T.Check("GPS: orange line in the world over the route", gps.Line != null && gps.Line.gameObject.activeSelf && gps.Line.positionCount == pts.Count && gps.Line.GetPosition(1).y > w.GroundY(pts[1]));
            T.Check("GPS: dotted route on the minimap and a HUD line", Minimap.Instance.VisibleRouteDots > 3 && HudLines.Status.Any(f => { var s = f(); return s != null && s.Contains("GPS"); }), Minimap.Instance.VisibleRouteDots + "");
            int rc = gps.Recomputes;
            FakeEngine.Run(1f);
            T.Check("GPS: not recomputed while standing still", gps.Recomputes == rc);
            Minimap.Instance.SetFull(true);
            FakeEngine.Run(0.2f);
            T.Check("GPS: route on the full map too", Minimap.Instance.VisibleFullRouteDots > 3);
            Minimap.Instance.SetFull(false);
            player.position = w.OnGround(town.pos + new Vector3(3f, 0f, 0f), 1f);
            FakeEngine.Run(1.2f);
            T.Check("GPS: route cleared on arrival", !gps.HasRoute && !gps.Line.gameObject.activeSelf);
            Vector3 near = DeliveryZone.LoadingBay.transform.position + new Vector3(25f, 0f, 0f);
            player.position = w.OnGround(DeliveryZone.LoadingBay.transform.position + new Vector3(4f, 0f, 0f), 1f);
            wm.SetWaypoint(near);
            FakeEngine.Run(1.2f);
            T.Check("GPS: a waypoint closer than the roads gets a straight line", gps.HasRoute && gps.RoadsUsed == 0 && gps.Points.Count == 2, gps.RoadsUsed + "");
            wm.ClearWaypoint();
            FakeEngine.Run(0.6f);
            T.Check("GPS: clearing the waypoint clears the route", !gps.HasRoute);
        }
        T.Check("Parity: no errors", FakeEngine.Errors.Count == e0, string.Join(" | ", FakeEngine.Errors.Skip(e0).Take(3)));

        // ---- save, restart
        SaveData.Flush(true);
        Boot();
        tractor = Tractor.Instance;
        FakeEngine.Run(1.5f);
        GiveTestLivery(tractor);
        plowGo = tractor.Visual(Implement.Plow);
        T.Check("Restart: attached implement, its lowered state and parked poses restored", tractor.ActiveImplement == Implement.Seeder && tractor.ImplementLowered && plowGo.transform.parent == null
            && Flat(plowGo.transform.position - plowPos).magnitude < 0.5f, tractor.ActiveImplement + " " + plowGo.transform.position + " vs " + plowPos);
        T.Check("Restart: dealer paint and wheels restored", VehicleConfig.Paint("tractor") == 2 && VehicleConfig.Wheels("tractor") == VehicleConfig.WheelType.Wide
            && Math.Abs(VehicleConfig.BodyColor(tractor).g - VehicleConfig.PaintColors[2].g) < 0.05f && Math.Abs(VehicleConfig.WheelWidthFactor(tractor) - 1.3f) < 0.01f);
        T.Check("Restart: distance driven restored", VehicleSwitcher.Instance.Distance("tractor") > 1f);

        // ---- new game
        NewGame();
        FakeEngine.Run(1.2f);
        T.Check("New game: plow attached and lowered, seeder / harvester back in the yard, nothing sold", tractor.ActiveImplement == Implement.Plow && tractor.ImplementLowered && ParkedImplement.All.Count == 2
            && Flat(tractor.Visual(Implement.Seeder).transform.position - ImplementYard.YardSlot(Implement.Seeder)).magnitude < 0.5f && !ImplementYard.IsSold(Implement.Harvester));
        T.Check("New game: factory paint and standard wheels, statistics cleared", VehicleConfig.Paint("tractor") == 0 && VehicleConfig.Wheels("tractor") == VehicleConfig.WheelType.Standard
            && Math.Abs(VehicleConfig.WheelWidthFactor(tractor) - 1f) < 0.01f && VehicleSwitcher.Instance.Distance("tractor") == 0f);
        T.Check("Parity: no errors after restart and new game", FakeEngine.Errors.Count == 0, string.Join(" | ", FakeEngine.Errors.Take(3)));
    }
}
