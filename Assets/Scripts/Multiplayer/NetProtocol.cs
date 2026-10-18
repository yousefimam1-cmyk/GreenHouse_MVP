using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// Co-op wire format: one string per message, "type" then fields separated by U+001F. Lists inside a field use
/// U+001E between items and U+001D between key and value. Kept as text so it can be tested without Netcode.
/// </summary>
public static class NetProtocol
{
    public const char Sep = '\u001f', Item = '\u001e', Pair = '\u001d';

    // client -> host
    public const string Hello = "H";       // name
    public const string Interact = "I";    // object id
    public const string Buy = "B";         // shop item id
    public const string Sell = "S";        // goods
    public const string FieldWork = FarmNet.FieldWork;   // "J" field work (FarmNet): tool, from, to, width, by hand, tractor, crop, product, density, mm
    public const string Gift = "G";        // to player id, good id, amount
    public const string FieldSet = FarmNet.FieldSet;     // "F" "d", sowing density (FarmNet)
    public const string IrrEdit = FarmNet.IrrEdit;       // "X" irrigation planner edit: add / pipe / del / set (FarmNet)
    public const string ContractAccept = "Q";   // contract offer id (MarketManager)
    // both ways (client -> host, host relays with the sender id first)
    public const string Pose = "P";        // [id] x, y, z, yaw, vehicle id, speed
    public const string Emote = "E";       // [id] emote index
    public const string Chat = "C";        // [id] text
    public const string Optimize = "O";    // greenhouse name, module
    // host -> client
    public const string Welcome = "W";     // your id
    public const string Roster = "R";      // players: id, name
    public const string Sync = "Y";        // NetSyncState
    public const string Echo = "U";        // object id used on the host (well started, bed harvested)
    public const string Alert = "A";       // English Loc key, args...
    public const string GiftIn = "K";      // from name, good id, amount
    public const string FieldCells = FarmNet.FieldCells; // "L" FarmCore cells: "d" deltas / "s" one field's snapshot (FarmNet)
    public const string IrrSync = FarmNet.IrrSync;       // "N" irrigation network, groundwater, combine tank, density (FarmNet)
    public const string MarketSync = "M";  // market prices, supply, demand events, contracts (MarketManager.EncodeSync)

    public static string Make(string type, params string[] fields)
    {
        var sb = new StringBuilder(type);
        if (fields != null) foreach (var f in fields) sb.Append(Sep).Append(f ?? "");
        return sb.ToString();
    }

    /// <summary>[0] is the type. Never null; missing fields read as "".</summary>
    public static string[] Split(string msg) => string.IsNullOrEmpty(msg) ? new[] { "" } : msg.Split(Sep);

    public static string Field(string[] parts, int i) => parts != null && i >= 0 && i < parts.Length ? parts[i] : "";

    public static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    public static string I(long v) => v.ToString(CultureInfo.InvariantCulture);
    public static float PF(string s) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && !float.IsNaN(v) && !float.IsInfinity(v) ? v : 0f;
    public static int PI(string s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
    public static ulong PU(string s) => ulong.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0UL;

    /// <summary>Player-typed text: separators and control characters removed, trimmed, length capped.</summary>
    public static string Clean(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (char.IsControl(c) || c == '<' || c == '>') continue;   // no TextMeshPro rich-text tags from other players
            sb.Append(c);
            if (sb.Length >= maxLength) break;
        }
        return sb.ToString().Trim();
    }

    public static string EncodeGoods(IEnumerable<KeyValuePair<string, float>> goods)
    {
        var sb = new StringBuilder();
        foreach (var kv in goods)
        {
            if (string.IsNullOrEmpty(kv.Key)) continue;
            if (sb.Length > 0) sb.Append(Item);
            sb.Append(Clean(kv.Key, 40)).Append(Pair).Append(F(kv.Value));
        }
        return sb.ToString();
    }

    public static Dictionary<string, float> DecodeGoods(string field)
    {
        var d = new Dictionary<string, float>();
        if (string.IsNullOrEmpty(field)) return d;
        foreach (var item in field.Split(Item))
        {
            var kv = item.Split(Pair);
            if (kv.Length != 2 || kv[0].Length == 0) continue;
            float v = PF(kv[1]);
            if (v > 0f) d[kv[0]] = (d.TryGetValue(kv[0], out var old) ? old : 0f) + v;
        }
        return d;
    }
}

/// <summary>A player's position as sent ~10 times a second (owner-authoritative).</summary>
public struct NetPose
{
    public Vector3 position;
    public float yaw, speed;
    public string vehicle;     // object id of the vehicle being driven, "" on foot

    public string[] Fields() => new[] { NetProtocol.F(position.x), NetProtocol.F(position.y), NetProtocol.F(position.z), NetProtocol.F(yaw), vehicle ?? "", NetProtocol.F(speed) };

    /// <summary>Parse from parts starting at index first.</summary>
    public static NetPose Parse(string[] p, int first)
    {
        return new NetPose
        {
            position = new Vector3(NetProtocol.PF(NetProtocol.Field(p, first)), NetProtocol.PF(NetProtocol.Field(p, first + 1)), NetProtocol.PF(NetProtocol.Field(p, first + 2))),
            yaw = NetProtocol.PF(NetProtocol.Field(p, first + 3)),
            vehicle = NetProtocol.Field(p, first + 4),
            speed = NetProtocol.PF(NetProtocol.Field(p, first + 5)),
        };
    }
}

/// <summary>
/// The farm state the host sends every second: money, storage, calendar, weather, mission and well (fields: FarmNet).
/// Capture() reads the running systems, Apply() writes them on a client (nothing is saved there).
/// </summary>
public class NetSyncState
{
    public int money;
    public int day = 1;
    public float dayProgress;
    public int weather;
    public int mission;
    public float missionProgress;
    public bool wellRunning;
    public Dictionary<string, float> goods = new Dictionary<string, float>();

    public static NetSyncState Capture()
    {
        var s = new NetSyncState();
        var eco = EconomyManager.Instance;
        if (eco != null)
        {
            s.money = eco.Money;
            foreach (var g in eco.Data.goods) if (eco.Stored(g.id) > 0f) s.goods[g.id] = eco.Stored(g.id);
        }
        var clock = WorldClock.Instance;
        if (clock != null) { s.day = clock.Day; s.dayProgress = clock.DayProgress; s.weather = (int)clock.Weather; }
        var mm = MissionManager.Instance;
        if (mm != null) { s.mission = mm.Index; s.missionProgress = mm.Progress; }
        foreach (var w in Object.FindObjectsByType<WellController>(FindObjectsSortMode.None)) if (w.IsRunning) s.wellRunning = true;
        return s;
    }

    public void Apply()
    {
        var eco = EconomyManager.Instance;
        if (eco != null) eco.ApplyRemote(money, goods);
        var clock = WorldClock.Instance;
        if (clock != null) { clock.FollowRemote = true; clock.ApplyRemote(day, dayProgress, (Weather)Mathf.Clamp(weather, 0, 3)); }
        var mm = MissionManager.Instance;
        if (mm != null) mm.ApplyRemote(mission, missionProgress, true);
        if (wellRunning)
            foreach (var w in Object.FindObjectsByType<WellController>(FindObjectsSortMode.None))
                if (!w.IsRunning) w.StartWell();
    }

    public string Encode()
    {
        return NetProtocol.Make(NetProtocol.Sync, NetProtocol.I(money), NetProtocol.I(day), NetProtocol.F(dayProgress), NetProtocol.I(weather),
            NetProtocol.I(mission), NetProtocol.F(missionProgress), wellRunning ? "1" : "0", NetProtocol.EncodeGoods(goods));
    }

    /// <summary>Returns null if the message is not a sync message.</summary>
    public static NetSyncState Decode(string msg)
    {
        var p = NetProtocol.Split(msg);
        if (p[0] != NetProtocol.Sync) return null;
        var s = new NetSyncState
        {
            money = NetProtocol.PI(NetProtocol.Field(p, 1)),
            day = Mathf.Max(1, NetProtocol.PI(NetProtocol.Field(p, 2))),
            dayProgress = NetProtocol.PF(NetProtocol.Field(p, 3)),
            weather = NetProtocol.PI(NetProtocol.Field(p, 4)),
            mission = NetProtocol.PI(NetProtocol.Field(p, 5)),
            missionProgress = NetProtocol.PF(NetProtocol.Field(p, 6)),
            wellRunning = NetProtocol.Field(p, 7) == "1",
            goods = NetProtocol.DecodeGoods(NetProtocol.Field(p, 8)),
        };
        return s;
    }
}
