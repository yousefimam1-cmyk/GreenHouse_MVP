using System;
using System.Collections.Generic;
using UnityEngine;

public enum NetMode { Offline, Host, Client }

/// <summary>
/// Transport behind NetSession. The default is LocalBackend (single player). NetcodeBackend (Unity Netcode for
/// GameObjects, only compiled with MAZRAA_NETCODE) registers itself at startup. Messages are plain strings built by
/// NetProtocol, so all game logic stays outside the Netcode-only file.
/// </summary>
public interface INetBackend
{
    /// <summary>False when no network library is installed (hosting/joining is impossible).</summary>
    bool Available { get; }
    NetMode Mode { get; }
    /// <summary>True while a client is still connecting.</summary>
    bool Connecting { get; }
    ulong LocalId { get; }
    /// <summary>Remote clients connected to this host (empty on clients).</summary>
    IEnumerable<ulong> ClientIds { get; }

    bool StartHost(ushort port);
    bool StartClient(string address, ushort port);
    void Leave();
    /// <summary>Host only: drop a client (e.g. the farm is full).</summary>
    void Kick(ulong clientId);

    void SendToHost(string msg, bool reliable);
    void SendToClient(ulong clientId, string msg, bool reliable);
    /// <summary>Host: send to every remote client.</summary>
    void SendToClients(string msg, bool reliable);

    /// <summary>(sender id, message). On the host, messages the host sends to itself arrive here too.</summary>
    event Action<ulong, string> Received;
    /// <summary>Host: a remote client connected / left.</summary>
    event Action<ulong> ClientConnected;
    event Action<ulong> ClientDisconnected;
    /// <summary>Client: connected to the host / lost the connection (or failed to connect).</summary>
    event Action Connected;
    event Action Disconnected;
}

/// <summary>Single player: never online. Used when the Netcode package is not installed.</summary>
public class LocalBackend : INetBackend
{
    public bool Available => false;
    public NetMode Mode => NetMode.Offline;
    public bool Connecting => false;
    public ulong LocalId => 0;
    public IEnumerable<ulong> ClientIds { get { yield break; } }
    public bool StartHost(ushort port) => false;
    public bool StartClient(string address, ushort port) => false;
    public void Leave() { }
    public void Kick(ulong clientId) { }
    public void SendToHost(string msg, bool reliable) { }
    public void SendToClient(ulong clientId, string msg, bool reliable) { }
    public void SendToClients(string msg, bool reliable) { }
#pragma warning disable 0067
    public event Action<ulong, string> Received;
    public event Action<ulong> ClientConnected, ClientDisconnected;
    public event Action Connected, Disconnected;
#pragma warning restore 0067
}

/// <summary>
/// What the rest of the game knows about multiplayer. Nothing here depends on Netcode.
/// Co-op on one farm: the host is authoritative (economy, missions, clock, fields); clients forward their
/// actions to the host through the hooks below and receive the farm state back every second.
/// </summary>
public static class NetSession
{
    public const ushort DefaultPort = 7777;
    public const int MaxClients = 3;

    private static INetBackend backend;
    public static INetBackend Backend => backend ?? (backend = new LocalBackend());
    public static NetMode Mode => Backend.Mode;
    public static bool IsHost => Backend.Mode == NetMode.Host;
    public static bool IsClient => Backend.Mode == NetMode.Client;
    public static bool IsOnline => Backend.Mode != NetMode.Offline;

    /// <summary>True while the player types in a text field (chat, name, IP): gameplay hotkeys should ignore keys.</summary>
    public static bool Typing;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { backend = null; Typing = false; lastJob.Clear(); }

    /// <summary>Called by a network implementation (e.g. NetcodeBackend) at startup. null = back to single player.</summary>
    public static void Register(INetBackend b) { backend = b; }

    // ------------------------------------------------------------ hooks used by shared game code

    /// <summary>
    /// InteractionController calls this before Interact(). Returns true when the interaction was sent to the host
    /// instead (client), so it must not run locally. UI-only objects (shop, control station, crop bed inspection) and
    /// vehicles stay local. On the host it tells clients about wells started / beds harvested.
    /// </summary>
    public static bool HandleInteract(IInteractable target)
    {
        var c = target as Component;
        if (c == null || !IsOnline) return false;
        if (IsHost)
        {
            if (Echoes(target) && MultiplayerManager.Instance != null) MultiplayerManager.Instance.HostEcho(NetIds.Of(c));
            return false;
        }
        if (!ForwardsToHost(target)) return false;
        RequestInteract(NetIds.Of(c));
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX();
        return true;
    }

    /// <summary>State-changing interactions that the host must perform for clients.</summary>
    public static bool ForwardsToHost(IInteractable t)
    {
        if (t is FieldPlot || t is WellController || t is PenCrate || t is PenTask) return true;
        var bed = t as Cropbed;
        return bed != null && bed.IsReadyToHarvest;   // otherwise E opens the local inspection panel
    }

    /// <summary>Interactions whose visible result other players must copy (well running, bed harvested).</summary>
    public static bool Echoes(IInteractable t)
    {
        var bed = t as Cropbed;
        if (bed != null) return bed.IsReadyToHarvest;
        var well = t as WellController;
        return well != null && !well.IsRunning;
    }

    public static void RequestInteract(string objectId)
    {
        if (IsClient && !string.IsNullOrEmpty(objectId)) Backend.SendToHost(NetProtocol.Make(NetProtocol.Interact, objectId), true);
    }

    /// <summary>EconomyManager.TryBuy on a client: the host buys from the shared farm money.</summary>
    public static void RequestBuy(string itemId)
    {
        if (IsClient && !string.IsNullOrEmpty(itemId)) Backend.SendToHost(NetProtocol.Make(NetProtocol.Buy, itemId), true);
    }

    /// <summary>EconomyManager.SellAll on a client: the host sells these goods from the farm storage. Returns an estimate (SAR).</summary>
    public static int RequestSell(Dictionary<string, float> cargo)
    {
        if (!IsClient || cargo == null || cargo.Count == 0) return 0;
        float estimate = 0f;
        foreach (var kv in cargo)
            estimate += kv.Value * (MarketManager.Instance != null ? MarketManager.Instance.Price(kv.Key) : 0f);
        Backend.SendToHost(NetProtocol.Make(NetProtocol.Sell, NetProtocol.EncodeGoods(cargo)), true);
        return Mathf.RoundToInt(estimate);
    }

    private static readonly Dictionary<string, float> lastJob = new Dictionary<string, float>();

    /// <summary>
    /// FieldPlot.TryPlow/TrySeed/TryHarvest on a client (tractor implements): send the job to the host.
    /// Returns true when forwarded (the caller must not change the plot locally; the host's state syncs back).
    /// </summary>
    public static bool ForwardPlotJob(FieldPlot plot, Implement job)
    {
        if (!IsClient || plot == null) return false;
        string key = plot.index + ":" + (int)job;
        float now = Time.unscaledTime;
        if (lastJob.TryGetValue(key, out var t) && now - t < 1f) return true;   // already asked, waiting for the sync
        lastJob[key] = now;
        Backend.SendToHost(NetProtocol.Make(NetProtocol.PlotJob, plot.index.ToString(), ((int)job).ToString()), true);
        return true;
    }

    /// <summary>UIManager after a module was optimized from the control station: copy it on the other machines.</summary>
    public static void OnOptimized(GreenhouseManager gh, ModuleType module)
    {
        if (!IsOnline || gh == null) return;
        string msg = NetProtocol.Make(NetProtocol.Optimize, gh.name, module.ToString());
        if (IsClient) Backend.SendToHost(msg, true);
        else Backend.SendToClients(msg, true);
    }
}

/// <summary>Translation helper for the multiplayer texts (Arabic in Resources/Data/Loc/70_mp.json).</summary>
public static class Mp
{
    public static string Tr(string english) => Loc.T(english);
    public static string Tr(string english, params object[] args) => Loc.T(english, args);
    public static string Raw(string english) => Loc.Raw(english);

    /// <summary>Player-typed text (names, chat) for TextMeshPro: Arabic letters are joined/reordered whatever the UI language.</summary>
    public static string UserText(string s) => string.IsNullOrEmpty(s) ? "" : ArabicShaper.HasArabic(s) ? ArabicShaper.Shape(s) : s;

    /// <summary>A player name to put inside a translated sentence (Loc.T shapes the whole line in Arabic mode).</summary>
    public static string Name(string s) => Loc.IsArabic ? (s ?? "") : UserText(s);

    public static bool HasArabic(string s) => !string.IsNullOrEmpty(s) && ArabicShaper.HasArabic(s);
}
