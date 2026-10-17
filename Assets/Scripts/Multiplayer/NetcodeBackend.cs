#if MAZRAA_NETCODE
using System;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using UnityEngine;

/// <summary>
/// Netcode for GameObjects (com.unity.netcode.gameobjects 2.x) + Unity Transport implementation of INetBackend.
/// Only compiled when MAZRAA_NETCODE is defined (Project Settings > Player > Scripting Define Symbols; on for Windows).
///
/// Kept deliberately small: no NetworkObject prefabs, no NetworkBehaviours, no scene management. The NetworkManager
/// is created in code and everything travels as one named message ("mazraa") whose payload is a string built by
/// NetProtocol. This avoids prefab hash registration, which needs the Unity editor, and keeps all game logic in
/// files that compile and are tested without Netcode.
///
/// Two ways to connect: LAN / direct IP (StartHost / StartClient with a port) and the internet through a Unity Relay
/// server (UseRelay, set by UgsOnlineServices, then StartRelayHost / StartRelayClient). Connection approval carries
/// the NetHandshake (game version, anonymous player id, name) so the host can refuse with a reason the joining
/// player sees (wrong version, full, banned...).
/// API checked against com.unity.netcode.gameobjects 2.11.2 and com.unity.transport 2.7.3.
/// </summary>
public class NetcodeBackend : INetBackend
{
    private const string Channel = "mazraa";

    public static NetcodeBackend Instance { get; private set; }

    private NetworkManager nm;
    private UnityTransport transport;
    private bool handlerRegistered;
    private bool leaving;   // we called Leave(): don't report it as a lost connection
    private bool relayReady;
    private string disconnectReason = "";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        Instance = new NetcodeBackend();
        NetSession.Register(Instance);
    }

    public bool Available => true;

    public NetMode Mode
    {
        get
        {
            if (nm == null || !nm.IsListening) return NetMode.Offline;
            if (nm.IsServer) return NetMode.Host;
            return nm.IsConnectedClient ? NetMode.Client : NetMode.Offline;
        }
    }

    public bool Connecting => nm != null && nm.IsListening && !nm.IsServer && nm.IsClient && !nm.IsConnectedClient;
    public ulong LocalId => nm != null ? nm.LocalClientId : 0UL;
    public string DisconnectReason => disconnectReason ?? "";
    public string ConnectPayload { get; set; }
    public Func<ulong, string, string> ApprovalCheck { get; set; }

    public IEnumerable<ulong> ClientIds
    {
        get
        {
            if (nm == null || !nm.IsServer) yield break;
            foreach (var id in new List<ulong>(nm.ConnectedClientsIds))
                if (id != nm.LocalClientId) yield return id;
        }
    }

    public event Action<ulong, string> Received;
    public event Action<ulong> ClientConnected;
    public event Action<ulong> ClientDisconnected;
    public event Action Connected;
    public event Action Disconnected;

    private void Ensure()
    {
        if (nm != null) return;
        var go = new GameObject("Mazraa NetworkManager");
        UnityEngine.Object.DontDestroyOnLoad(go);
        transport = go.AddComponent<UnityTransport>();
        nm = go.AddComponent<NetworkManager>();
        if (nm.NetworkConfig == null) nm.NetworkConfig = new NetworkConfig();
        nm.NetworkConfig.NetworkTransport = transport;
        nm.NetworkConfig.EnableSceneManagement = false;   // both sides already have the same scene
        nm.NetworkConfig.ConnectionApproval = true;       // NetHandshake: version, bans, room left
        nm.NetworkConfig.PlayerPrefab = null;             // players are drawn by RemotePlayer, not spawned objects
        nm.ConnectionApprovalCallback = OnApproval;
        nm.OnClientConnectedCallback += OnClientConnected;
        nm.OnClientDisconnectCallback += OnClientDisconnected;
    }

    /// <summary>Online: connect through this Unity Relay server next (UgsOnlineServices after CreateRelay / JoinRelay).</summary>
    public void UseRelay(RelayServerData data)
    {
        Ensure();
        transport.SetRelayServerData(data);
        relayReady = true;
    }

    public bool StartHost(ushort port)
    {
        Ensure();
        if (nm.IsListening) return false;
        relayReady = false;
        transport.SetConnectionData("127.0.0.1", port, "0.0.0.0");   // listen on every network card (LAN)
        return BeginHost();
    }

    public bool StartClient(string address, ushort port)
    {
        Ensure();
        if (nm.IsListening) return false;
        relayReady = false;
        transport.SetConnectionData(address, port);
        return BeginClient();
    }

    public bool StartRelayHost()
    {
        Ensure();
        if (nm.IsListening || !relayReady) return false;
        return BeginHost();
    }

    public bool StartRelayClient()
    {
        Ensure();
        if (nm.IsListening || !relayReady) return false;
        return BeginClient();
    }

    private bool BeginHost()
    {
        leaving = false;
        disconnectReason = "";
        nm.NetworkConfig.ConnectionData = new byte[0];
        if (!nm.StartHost()) return false;
        RegisterHandler();
        return true;
    }

    private bool BeginClient()
    {
        leaving = false;
        disconnectReason = "";
        nm.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(ConnectPayload ?? "");
        if (!nm.StartClient()) return false;
        RegisterHandler();
        return true;
    }

    public void Leave()
    {
        if (nm == null) return;
        leaving = true;
        relayReady = false;
        if (nm.CustomMessagingManager != null && handlerRegistered) nm.CustomMessagingManager.UnregisterNamedMessageHandler(Channel);
        handlerRegistered = false;
        if (nm.IsListening && !nm.ShutdownInProgress) nm.Shutdown();
    }

    public void Kick(ulong clientId, string reason)
    {
        if (nm != null && nm.IsServer && clientId != nm.LocalClientId) nm.DisconnectClient(clientId, reason ?? "");
    }

    /// <summary>Host: NetHandshake check of every joining player (the host's own connection is always accepted).</summary>
    private void OnApproval(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        response.CreatePlayerObject = false;
        response.Pending = false;
        if (request.ClientNetworkId == NetworkManager.ServerClientId) { response.Approved = true; return; }
        string payload = "";
        try { payload = request.Payload != null ? Encoding.UTF8.GetString(request.Payload) : ""; }
        catch (Exception) { payload = ""; }
        string refuse = ApprovalCheck != null ? ApprovalCheck(request.ClientNetworkId, payload) : null;
        response.Approved = string.IsNullOrEmpty(refuse);
        response.Reason = refuse ?? "";
    }

    private void RegisterHandler()
    {
        if (nm.CustomMessagingManager == null) return;
        nm.CustomMessagingManager.RegisterNamedMessageHandler(Channel, OnNamedMessage);
        handlerRegistered = true;
    }

    private void OnNamedMessage(ulong senderClientId, FastBufferReader reader)
    {
        string msg;
        reader.ReadValueSafe(out msg);
        if (Received != null) Received(senderClientId, msg);
    }

    private void OnClientConnected(ulong clientId)
    {
        if (nm.IsServer)
        {
            if (clientId != nm.LocalClientId && ClientConnected != null) ClientConnected(clientId);
        }
        else if (clientId == nm.LocalClientId && Connected != null) Connected();
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (nm.IsServer)
        {
            if (clientId != nm.LocalClientId && ClientDisconnected != null) ClientDisconnected(clientId);
            return;
        }
        // On a client this fires for our own id when the host goes away, refuses us or the connection fails.
        disconnectReason = nm.DisconnectReason ?? "";
        handlerRegistered = false;
        relayReady = false;
        if (nm.IsListening && !nm.ShutdownInProgress) nm.Shutdown();
        if (!leaving && Disconnected != null) Disconnected();
    }

    // ------------------------------------------------------------ sending

    public void SendToHost(string msg, bool reliable)
    {
        if (nm == null || !nm.IsListening) return;
        if (nm.IsServer) { if (Received != null) Received(nm.LocalClientId, msg); return; }
        Send(NetworkManager.ServerClientId, msg, reliable);
    }

    public void SendToClient(ulong clientId, string msg, bool reliable)
    {
        if (nm == null || !nm.IsListening) return;
        if (clientId == nm.LocalClientId) { if (Received != null) Received(clientId, msg); return; }
        Send(clientId, msg, reliable);
    }

    public void SendToClients(string msg, bool reliable)
    {
        foreach (var id in ClientIds) Send(id, msg, reliable);
    }

    private void Send(ulong clientId, string msg, bool reliable)
    {
        if (nm.CustomMessagingManager == null || msg == null) return;
        var delivery = reliable ? NetworkDelivery.ReliableFragmentedSequenced : NetworkDelivery.UnreliableSequenced;
        var writer = new FastBufferWriter(FastBufferWriter.GetWriteSize(msg) + 8, Allocator.Temp);
        try
        {
            writer.WriteValueSafe(msg);
            nm.CustomMessagingManager.SendNamedMessage(Channel, clientId, writer, delivery);
        }
        finally { writer.Dispose(); }
    }
}
#endif
