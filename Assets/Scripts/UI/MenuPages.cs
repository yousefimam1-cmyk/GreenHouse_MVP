using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>One field as shown on the farm menu's Map tab (filled by the farming core; FieldManager for now).</summary>
public struct FieldInfoRow
{
    public int number;          // field number shown on the map and minimap
    public string state;        // English Loc key: "Untilled", "Plowed", "Growing", "Ripe" ...
    public string crop;         // translated crop name ("" = none)
    public float growth;        // 0..1
    public float areaM2;
    public Vector3 position;
}

/// <summary>Fields for the Map tab and the minimap's field numbers. The farming core (FarmCore) can replace FarmMenuData.Fields.</summary>
public interface IFieldInfoSource
{
    int Count { get; }
    FieldInfoRow Row(int i);
}

/// <summary>One crop of the Calendar tab: sow / harvest per season (Spring, Summer, Autumn, Winter).</summary>
public class CropCalendarRow
{
    public string name;                         // translated
    public readonly bool[] sow = new bool[4], harvest = new bool[4];
}

/// <summary>Crops for the Calendar tab. The farming core can replace FarmMenuData.Calendar.</summary>
public interface ICropCalendarSource
{
    List<CropCalendarRow> Rows();
}

/// <summary>Data sources of the farm menu tabs (swap in the farming core's implementations when it exists).</summary>
public static class FarmMenuData
{
    public static IFieldInfoSource Fields = new FarmCoreFields();          // FarmCore (FarmCoreMenu.cs)
    public static ICropCalendarSource Calendar = new FarmCoreCalendar();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { Fields = new FarmCoreFields(); Calendar = new FarmCoreCalendar(); }   // FarmCore
}

/// <summary>Calendar from economy.json: a crop is sown in its seasons and harvested one growth period later.</summary>
public class EconomyCropCalendar : ICropCalendarSource
{
    public List<CropCalendarRow> Rows()
    {
        var list = new List<CropCalendarRow>();
        var eco = EconomyManager.Instance;
        if (eco == null || eco.Data.fieldCrops == null) return list;
        var clock = WorldClock.Instance;
        float dayLen = clock != null ? Mathf.Max(10f, clock.secondsPerDay) : 1440f;
        int perSeason = clock != null ? Mathf.Max(1, clock.daysPerSeason) : 5;
        foreach (var c in eco.Data.fieldCrops)
        {
            if (c.regions != null && c.regions.Length > 0 && Regions.Current != null && System.Array.IndexOf(c.regions, Regions.Current.id) < 0) continue;
            var row = new CropCalendarRow { name = FieldCropText.Name(c) };
            int shift = Mathf.Clamp(Mathf.CeilToInt(c.growSeconds / dayLen / perSeason), 0, 3);
            bool anySeason = c.seasons == null || c.seasons.Length == 0;
            for (int s = 0; s < 4; s++)
            {
                bool sow = anySeason || System.Array.IndexOf(c.seasons, WorldClock.SeasonId((Season)s)) >= 0;
                if (!sow) continue;
                row.sow[s] = true;
                row.harvest[(s + shift) % 4] = true;
            }
            list.Add(row);
        }
        return list;
    }
}

/// <summary>
/// Text and row actions of the farm menu tabs added for Farming-Simulator parity: Map (fields with number, state,
/// crop, growth; Enter = waypoint + full map), Calendar (sow / harvest per season), Weather (today + 5-day forecast with
/// temperatures), Vehicles (list with fuel, condition, distance, hours, where; Enter vehicle, locate, dealer paint /
/// wheels; implements: locate, sell, buy back), Animals (herds, products, pen state) and Help (encyclopedia).
/// FarmInfoScreen draws them and routes Up / Down (row), Left / Right (action) and Enter / A (do it).
/// </summary>
public static class MenuPages
{
    public static readonly string[] VehicleActions = { "Enter vehicle", "Locate", "Repaint", "Change wheels" };
    public static readonly string[] ImplementActions = { "Locate", "Sell", "Buy back" };
    public static readonly Implement[] Implements = { Implement.Plow, Implement.Seeder, Implement.Harvester };

    /// <summary>Help topics: English Loc keys of the title and the text.</summary>
    public static readonly string[,] HelpTopics =
    {
        { "Getting started", "Your farm starts with a truck, a tractor with a plow and a few fields. Plow, sow and harvest the fields, sell the crops at the market or the places around the map, and grow the farm with new land, animals and production. The top-right block shows the time, weather and your money; the box top-left shows the keys for what you are doing now." },
        { "Driving and vehicles", "[E] gets in and out, [Tab] jumps to the next vehicle. [F] starts the engine, [Y] cruise control, [Plus] / [Minus] its speed, [Z] 4WD and diff lock, [Slash] the GPS straight line. The gauges bottom-right show speed, gear, fuel, damage and load. Refuel and repair at the loading bay or the dealership workshop." },
        { "Implements", "Implements stand in the yard next to the tractor. Reverse until the hitch is close and press Q to attach (gamepad: hold Y). V lowers and raises it (gamepad: tap Y) - only a lowered implement works the ground. Hold V to fold it for the road; a raised implement folds itself at road speed. Press Q again (hold Y) to leave it where you are." },
        { "Fields and crops", "Each field has a number on the map. Plow, sow a crop that is in season, water it if it does not rain and harvest when it is ripe. The Calendar tab shows when each crop is sown (S) and harvested (H); the Fields tab shows soil, irrigation and expected yield." },
        { "Seasons and weather", "A season lasts a few days. Rain waters every field, heatwaves dry them twice as fast. The Weather tab shows today and the next five days with temperatures - plan sowing and harvest around it." },
        { "Animals", "Buy animals at the farm shop. They need feed, a full water trough and a clean pen. Collect their products from the crate and sell them. The Animals tab shows each herd, what it produces and how the pen is doing." },
        { "Selling and contracts", "Load the truck at the loading bay and park on a selling point. Prices differ per place and change every day - compare them on the Prices tab. Contracts pay a bonus for delivering a set amount before a due day." },
        { "Map, GPS and travel", "[N] opens the map. Pick a place and press [Enter] to set a waypoint: an orange line shows the way by road on the map, the minimap and the road itself. Places you have visited can be reached by fast travel for a small fee." },
        { "Dealer and workshop", "Park at a workshop (the farm loading bay or the dealership) and open the Vehicles tab to repaint a vehicle or change its wheels: wide tyres grip better in the field, road tyres are faster on asphalt. Implements you do not need can be sold there and bought back later." },
    };

    public static bool Handles(FarmInfoScreen.Page p) => p >= FarmInfoScreen.Page.Map;

    private static string L(string s) => "\n" + Loc.Display(s);
    private static string Sel(bool on, string s) => on ? "<color=#F2A93B>> " + s + "</color>" : "  " + s;
    private static string Dim(string s) => "<color=#BBBBBB>" + s + "</color>";

    public static int RowCount(FarmInfoScreen.Page p)
    {
        switch (p)
        {
            case FarmInfoScreen.Page.Map: return FarmMenuData.Fields.Count;
            case FarmInfoScreen.Page.Calendar: return FarmMenuData.Calendar.Rows().Count;
            case FarmInfoScreen.Page.Vehicles: return VehicleSwitcher.All().Count + Implements.Length;
            case FarmInfoScreen.Page.Animals: return EconomyManager.Instance != null ? EconomyManager.Instance.Data.animals.Length : 0;
            case FarmInfoScreen.Page.Help: return HelpTopics.GetLength(0);
            default: return 0;
        }
    }

    public static bool Adjust(FarmInfoScreen.Page p, int row, ref int action, int dir)
    {
        if (dir == 0 || p != FarmInfoScreen.Page.Vehicles) return false;
        int n = ActionsFor(row).Length;
        action = ((action + dir) % n + n) % n;
        return true;
    }

    /// <summary>Vehicles tab: the actions of a row (vehicles or implements).</summary>
    public static string[] ActionsFor(int row) => row < VehicleSwitcher.All().Count ? VehicleActions : ImplementActions;

    /// <summary>Enter / A on a row. Returns the message shown ("" = none).</summary>
    public static string Confirm(FarmInfoScreen.Page p, int row, int action)
    {
        string msg = "";
        if (p == FarmInfoScreen.Page.Map)
        {
            if (row < 0 || row >= FarmMenuData.Fields.Count) return "";
            var f = FarmMenuData.Fields.Row(row);
            if (WorldMap.Instance != null) WorldMap.Instance.SetWaypoint(f.position);
            if (FarmInfoScreen.Instance != null) FarmInfoScreen.Instance.Close();
            if (Minimap.Instance != null) Minimap.Instance.SetFull(true);
            return Loc.Display(Loc.Raw("Field") + " " + f.number);
        }
        if (p != FarmInfoScreen.Page.Vehicles) return "";
        var vehicles = VehicleSwitcher.All();
        if (row < vehicles.Count)
        {
            var v = vehicles[row];
            action = Mathf.Clamp(action, 0, VehicleActions.Length - 1);
            switch (action)
            {
                case 0:
                    if (FarmInfoScreen.Instance != null) FarmInfoScreen.Instance.Close();
                    if (VehicleSwitcher.Instance != null) VehicleSwitcher.Instance.SwitchTo(v);
                    break;
                case 1: msg = Locate(v.transform.position, VehicleSwitcher.VehicleName(v)); break;
                case 2: VehicleConfig.BuyPaint(v, VehicleConfig.Paint(v.Id) + 1); break;
                default: VehicleConfig.BuyWheels(v, (VehicleConfig.WheelType)(((int)VehicleConfig.Wheels(v.Id) + 1) % 3)); break;
            }
            return msg;
        }
        int ii = row - vehicles.Count;
        if (ii < 0 || ii >= Implements.Length) return "";
        var kind = Implements[ii];
        var tractor = Tractor.Instance;
        action = Mathf.Clamp(action, 0, ImplementActions.Length - 1);
        if (action == 0)
        {
            var go = tractor != null ? tractor.Visual(kind) : null;
            if (go != null && go.activeInHierarchy) msg = Locate(go.transform.position, Tractor.ImplementName(kind));
        }
        else if (action == 1 && tractor != null)
        {
            string block = SellBlock(kind);
            if (block != null) msg = Loc.T(block);
            else msg = Loc.Display(Loc.Raw("Sold") + ": " + Tractor.ImplementName(kind) + " +" + tractor.SellImplement(kind).ToString("N0") + " " + Loc.Raw("SAR"));
        }
        else if (action == 2 && tractor != null)
        {
            var eco = EconomyManager.Instance;
            int price = ImplementYard.Price(kind);
            if (!ImplementYard.IsSold(kind)) msg = Loc.T("You already own it");
            else if (eco == null || eco.Money < price) msg = Loc.T("Not enough money");
            else
            {
                using (Ledger.As(Ledger.Purchases)) eco.AddMoney(-price);
                tractor.BuyBackImplement(kind);
                msg = Loc.Display(Loc.Raw("Bought") + ": " + Tractor.ImplementName(kind) + " - " + Loc.Raw("delivered to the implement yard"));
            }
        }
        if (!string.IsNullOrEmpty(msg) && UIManager.Instance != null) UIManager.Instance.ShowAlertMessage(msg);
        return msg;
    }

    /// <summary>English Loc key of why an implement can't be sold now (null = it can).</summary>
    public static string SellBlock(Implement kind)
    {
        var t = Tractor.Instance;
        if (t == null) return "No tractor";
        if (ImplementYard.IsSold(kind)) return "Already sold";
        if (t.ActiveImplement == kind) return "Detach it first";
        return null;
    }

    private static string Locate(Vector3 pos, string name)
    {
        if (WorldMap.Instance != null) WorldMap.Instance.SetWaypoint(pos);
        var anchor = Minimap.Anchor();
        float d = anchor != null ? Vector3.Distance(Flat(anchor.position), Flat(pos)) : 0f;
        string where = anchor != null ? WorldMap.Metres(d) + " " + WorldMap.Compass(anchor.position, pos) : "";
        return Loc.Display(name + ": " + where + " - " + Loc.Raw("waypoint set"));
    }

    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    // ------------------------------------------------------------------ texts

    public static string Text(FarmInfoScreen.Page p, int row, int action)
    {
        switch (p)
        {
            case FarmInfoScreen.Page.Map: return MapText(row);
            case FarmInfoScreen.Page.Calendar: return CalendarText(row);
            case FarmInfoScreen.Page.Weather: return WeatherText();
            case FarmInfoScreen.Page.Vehicles: return VehiclesText(row, action);
            case FarmInfoScreen.Page.Animals: return AnimalsText(row);
            default: return HelpText(row);
        }
    }

    public static string Hint(FarmInfoScreen.Page p)
    {
        switch (p)
        {
            case FarmInfoScreen.Page.Map: return "Up / Down: fields   [Enter] waypoint + full map   [Esc] close";
            case FarmInfoScreen.Page.Vehicles: return "Up / Down: vehicles   Left / Right: action   [Enter] do it   [Esc] close";
            case FarmInfoScreen.Page.Weather: return "[Esc] close";
            default: return "Up / Down: choose   [Esc] close";
        }
    }

    public static string MapText(int sel)
    {
        var sb = new StringBuilder();
        sb.Append(MenuKit.Title(UiText.T("Map")));
        var src = FarmMenuData.Fields;
        sb.Append("\n\n").Append(MenuKit.Title(UiText.T("Fields")));
        if (src.Count == 0) sb.Append(L(UiText.Raw("No fields yet")));
        for (int i = 0; i < src.Count; i++)
        {
            var f = src.Row(i);
            string line = UiText.Raw("Field") + " " + f.number + ": " + (string.IsNullOrEmpty(f.state) ? string.Empty : UiText.Raw(f.state)) + (string.IsNullOrEmpty(f.crop) ? "" : " - " + f.crop + " " + Mathf.RoundToInt(f.growth * 100f) + "%")
                + "  (" + Mathf.RoundToInt(f.areaM2) + " m2)";
            sb.Append('\n').Append(Sel(i == sel, Loc.Display(line)));
        }
        var wm = WorldMap.Instance;
        if (wm != null && wm.Waypoint.HasValue)
        {
            var anchor = Minimap.Anchor();
            sb.Append("\n\n").Append(L(UiText.Raw("Waypoint") + ": " + (anchor != null ? WorldMap.Metres(wm.WaypointDistance(anchor.position)) : "")
                + (GpsRoute.Instance != null && GpsRoute.Instance.HasRoute ? " - " + UiText.Raw("route by road") + " " + WorldMap.Metres(GpsRoute.Instance.RouteLength) : "")));
        }
        return sb.ToString();
    }

    public static string CalendarText(int sel)
    {
        var sb = new StringBuilder();
        sb.Append(MenuKit.Title(UiText.T("Calendar")));
        var clock = WorldClock.Instance;
        if (clock != null) sb.Append(L(UiText.Raw("Now") + ": " + clock.SeasonName() + " " + clock.DayOfSeason + "/" + clock.daysPerSeason));
        sb.Append("\n").Append(Dim(Loc.Display(UiText.Raw("S = sow, H = harvest") + "   " + Loc.Raw("Spring") + " | " + Loc.Raw("Summer") + " | " + Loc.Raw("Autumn") + " | " + Loc.Raw("Winter"))));
        var rows = FarmMenuData.Calendar.Rows();
        int now = clock != null ? (int)clock.Season : -1;
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var cells = new StringBuilder();
            for (int s = 0; s < 4; s++)
            {
                string c = r.sow[s] && r.harvest[s] ? "SH" : r.sow[s] ? "S" : r.harvest[s] ? "H" : "-";
                if (s > 0) cells.Append(" | ");
                cells.Append(s == now ? "[" + c + "]" : c);
            }
            sb.Append('\n').Append(Sel(i == sel, Loc.Display(r.name + ":  ") + cells));
        }
        if (rows.Count == 0) sb.Append(L(UiText.Raw("No crops on this map")));
        return sb.ToString();
    }

    public static string WeatherText()
    {
        var sb = new StringBuilder();
        sb.Append(MenuKit.Title(UiText.T("Weather forecast")));
        var clock = WorldClock.Instance;
        if (clock == null) return sb.ToString();
        var t = WorldClock.Temperature(clock.Season, clock.Weather);
        sb.Append(L(UiText.Raw("Today") + " (" + UiText.Raw("Day") + " " + clock.Day + "): " + clock.WeatherName() + "  " + t.x + " / " + t.y + " C"));
        var f = clock.Forecast;
        for (int i = 0; i < f.Count; i++)
        {
            int day = clock.Day + i + 1;
            var s = clock.SeasonOf(day);
            var tt = WorldClock.Temperature(s, f[i]);
            sb.Append(L(UiText.Raw("Day") + " " + day + " (" + Loc.Raw(s.ToString()) + "): " + Loc.Raw(f[i].ToString()) + "  " + tt.x + " / " + tt.y + " C"));
        }
        sb.Append("\n\n").Append(Dim(Loc.Display(UiText.Raw("Rain waters every field; heatwaves dry fields twice as fast."))));
        return sb.ToString();
    }

    public static string VehiclesText(int sel, int action)
    {
        var sb = new StringBuilder();
        sb.Append(MenuKit.Title(UiText.T("Vehicles")));
        var vehicles = VehicleSwitcher.All();
        var sw = VehicleSwitcher.Instance;
        var anchor = Minimap.Anchor();
        for (int i = 0; i < vehicles.Count; i++)
        {
            var v = vehicles[i];
            int fuel = Mathf.RoundToInt(v.Fuel / Mathf.Max(1f, v.fuelCapacity) * 100f);
            int cond = Mathf.RoundToInt(Mathf.Min(v.EngineCondition, v.TyreCondition) * 100f);
            float km = sw != null ? sw.Distance(v.Id) / 1000f : 0f;
            float hours = sw != null ? sw.OperatingSeconds(v.Id) / 3600f : 0f;
            string where = v == Vehicle.Current ? UiText.Raw("in use") : anchor != null ? WorldMap.Metres(Vector3.Distance(Flat(anchor.position), Flat(v.transform.position))) : "";
            string line = VehicleSwitcher.VehicleName(v) + " - " + UiText.Raw("Fuel") + " " + fuel + "%  " + UiText.Raw("Condition") + " " + cond + "%  " + km.ToString("0.0") + " " + Loc.Raw("km")
                + "  " + hours.ToString("0.0") + " h  " + where;
            sb.Append('\n').Append(Sel(i == sel, Loc.Display(line)));
            sb.Append("\n<size=85%>").Append(Dim(Loc.Display("    " + VehicleConfig.PaintName(v.Id) + ", " + VehicleConfig.WheelName(v.Id)))).Append("</size>");
            if (i == sel) sb.Append("\n    ").Append(Actions(VehicleActions, action));
        }
        sb.Append("\n\n").Append(MenuKit.Title(UiText.T("Implements")));
        var tractor = Tractor.Instance;
        for (int k = 0; k < Implements.Length; k++)
        {
            var kind = Implements[k];
            int row = vehicles.Count + k;
            string state = ImplementYard.IsSold(kind) ? UiText.Raw("sold") + " (" + ImplementYard.Price(kind).ToString("N0") + " " + Loc.Raw("SAR") + ")"
                : tractor != null && tractor.ActiveImplement == kind ? UiText.Raw("on the tractor") + (tractor.InWorkPose ? " - " + Loc.Raw("lowered") : tractor.ImplementFolded ? " - " + Loc.Raw("folded") : " - " + Loc.Raw("raised"))
                : UiText.Raw("parked");
            string line = Tractor.ImplementName(kind) + " - " + state + "  " + Tractor.ImplementWidth(kind).ToString("0.0") + " m";
            sb.Append('\n').Append(Sel(row == sel, Loc.Display(line)));
            if (row == sel) sb.Append("\n    ").Append(Actions(ImplementActions, action));
        }
        sb.Append("\n\n").Append(Dim(Loc.Display(UiText.Raw("Paint") + " " + VehicleConfig.PaintCost + " " + Loc.Raw("SAR") + ", " + UiText.Raw("wheels") + " " + VehicleConfig.WheelCost + " " + Loc.Raw("SAR") + " - " + UiText.Raw("at a workshop"))));
        return sb.ToString();
    }

    private static string Actions(string[] names, int action)
    {
        var sb = new StringBuilder();
        int a = Mathf.Clamp(action, 0, names.Length - 1);
        for (int i = 0; i < names.Length; i++)
        {
            if (i > 0) sb.Append("  ");
            string n = UiText.T(names[i]);
            sb.Append(i == a ? "<color=#F2A93B>[" + n + "]</color>" : Dim(n));
        }
        return sb.ToString();
    }

    public static string AnimalsText(int sel)
    {
        var sb = new StringBuilder();
        sb.Append(MenuKit.Title(UiText.T("Animals")));
        var eco = EconomyManager.Instance;
        if (eco == null) return sb.ToString();
        var pen = AnimalPen.Instance;
        for (int i = 0; i < eco.Data.animals.Length; i++)
        {
            var a = eco.Data.animals[i];
            var g = eco.Good(a.product);
            string product = g != null ? EconomyManager.GoodName(g) : a.product;
            float waiting = 0f; if (pen != null && pen.Waiting != null) pen.Waiting.TryGetValue(a.id, out waiting);
            string line = EconomyManager.AnimalName(a) + ": " + eco.Owned(a.id) + " / " + a.max + " - " + product + " " + a.amount.ToString("0.#") + " / " + Mathf.RoundToInt(a.interval) + " s"
                + (waiting > 0.05f ? " (" + UiText.Raw("ready") + " " + waiting.ToString("0.#") + ")" : "");
            sb.Append('\n').Append(Sel(i == sel, Loc.Display(line)));
        }
        sb.Append("\n\n").Append(L(UiText.Raw("Feed") + ": " + eco.Feed.ToString("0.0")));
        if (pen != null)
            sb.Append(L(UiText.Raw("Water") + " " + Mathf.RoundToInt(pen.WaterLevel * 100f) + "%  " + UiText.Raw("Cleanliness") + " " + Mathf.RoundToInt(pen.Cleanliness * 100f) + "%  "
                + UiText.Raw("Health") + " " + Mathf.RoundToInt(pen.Health * 100f) + "%  " + UiText.Raw("Productivity") + " " + Mathf.RoundToInt(pen.Productivity * 100f) + "%"));
        return sb.ToString();
    }

    public static string HelpText(int sel)
    {
        var sb = new StringBuilder();
        sb.Append(MenuKit.Title(UiText.T("Help")));
        int n = HelpTopics.GetLength(0);
        sel = Mathf.Clamp(sel, 0, n - 1);
        for (int i = 0; i < n; i++) sb.Append('\n').Append(Sel(i == sel, UiText.T(HelpTopics[i, 0])));
        sb.Append("\n\n").Append(MenuKit.Title(UiText.T(HelpTopics[sel, 0])));
        sb.Append("\n<size=90%>").Append(Glyphs.Apply(UiText.T(HelpTopics[sel, 1]))).Append("</size>");
        return sb.ToString();
    }
}
