#if MAZRAA_NETCODE
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

/// <summary>
/// Netcode for GameObjects (com.unity.netcode.gameobjects 2.x) + Unity Transport implementation of INetBackend.
/// Only compiled when MAZRAA_NETCODE is defined (Project Settings > Player > Scripting Define Symbols).
///
/// Kept deliberately small: no NetworkObject prefabs, no NetworkBehaviours, no scene management. The NetworkManager
/// is created in code and everything travels as one named message ("mazraa") whose payload is a string built by
/// NetProtocol. This avoids prefab hash registration, which needs the Unity editor, and keeps all game logic in
/// files that compile and are tested without Netcode.
/// </summary>
public class NetcodeBackend : INetBackend
{
    private const string Channel = "mazraa";

    private NetworkManager nm;
    private UnityTransport transport;
    private bool handlerRegistered;
    private bool leaving;   // we called Leave(): don't report it as a lost connection

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install() => NetSession.Register(new NetcodeBackend());

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
        nm.NetworkConfig.ConnectionApproval = false;
        nm.NetworkConfig.PlayerPrefab = null;             // players are drawn by RemotePlayer, not spawned objects
        nm.OnClientConnectedCallback += OnClientConnected;
        nm.OnClientDisconnectCallback += OnClientDisconnected;
    }

    public bool StartHost(ushort port)
    {
        Ensure();
        if (nm.IsListening) return false;
        leaving = false;
        transport.SetConnectionData("127.0.0.1", port, "0.0.0.0");   // listen on every network card (LAN)
        if (!nm.StartHost()) return false;
        RegisterHandler();
        return true;
    }

    public bool StartClient(string address, ushort port)
    {
        Ensure();
        if (nm.IsListening) return false;
        leaving = false;
        transport.SetConnectionData(address, port);
        if (!nm.StartClient()) return false;
        RegisterHandler();
        return true;
    }

    public void Leave()
    {
        if (nm == null) return;
        leaving = true;
        if (nm.CustomMessagingManager != null && handlerRegistered) nm.CustomMessagingManager.UnregisterNamedMessageHandler(Channel);
        handlerRegistered = false;
        if (nm.IsListening && !nm.ShutdownInProgress) nm.Shutdown();
    }

    public void Kick(ulong clientId)
    {
        if (nm != null && nm.IsServer && clientId != nm.LocalClientId) nm.DisconnectClient(clientId);
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
        // On a client this fires for our own id when the host goes away or the connection fails.
        handlerRegistered = false;
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
        using (var writer = new FastBufferWriter(FastBufferWriter.GetWriteSize(msg) + 8, Allocator.Temp))
        {
            writer.WriteValueSafe(msg);
            nm.CustomMessagingManager.SendNamedMessage(Channel, clientId, writer, delivery);
        }
    }
}
#endif
