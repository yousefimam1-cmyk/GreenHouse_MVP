#if MAZRAA_ONLINE && MAZRAA_NETCODE
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

/// <summary>
/// Internet play through Unity Gaming Services (package com.unity.services.multiplayer 2.3.3, which contains the
/// Relay and Lobby SDKs; Authentication and Core come with it). Only compiled with MAZRAA_ONLINE (and MAZRAA_NETCODE).
///  - Anonymous sign-in: no account, no name or e-mail; Unity gives the device an anonymous player id.
///  - Relay: the host reserves a Relay server and gets a 6-character join code; friends join with the code. Nobody's
///    IP address is shown to other players and no port forwarding is needed. DTLS (encrypted) connection.
///  - Lobby (public farms only): the farm name, map, game version, player count and the join code are listed so
///    other players can browse and join. Code-only and friends-only farms are never listed.
/// The project must be linked to a Unity Cloud project once (Edit > Project Settings > Services) and Relay + Lobby
/// enabled on the Unity dashboard, otherwise every call answers OnlineError.NotSetUp.
/// </summary>
public class UgsOnlineServices : IOnlineServices
{
    private const string ConnectionType = "dtls";
    private const float HeartbeatSeconds = 15f;   // a lobby without heartbeats goes inactive after 30 s
    private const float UpdateDelay = 2f;         // lobby updates are rate limited: send the player count at most every 2 s

    private bool signedIn;
    private string lobbyId;
    private float heartbeat;
    private int pendingPlayers = -1;
    private float updateTimer;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install() => NetSession.RegisterOnline(new UgsOnlineServices());

    public bool Available => true;
    public string PlayerId => signedIn && AuthenticationService.Instance.IsSignedIn ? AuthenticationService.Instance.PlayerId ?? "" : "";

    private sealed class NoInternetException : Exception { }
    private sealed class NotSetUpException : Exception { }

    // ------------------------------------------------------------ sign-in

    public async void SignIn(Action<OnlineError> done)
    {
        var e = OnlineError.None;
        try { await EnsureSignedIn(); }
        catch (Exception ex) { e = Map(ex, OnlineError.SignInFailed); }
        done?.Invoke(e);
    }

    private async Task EnsureSignedIn()
    {
        if (Application.internetReachability == NetworkReachability.NotReachable) throw new NoInternetException();
        if (string.IsNullOrEmpty(Application.cloudProjectId)) throw new NotSetUpException();   // not linked to a Unity Cloud project
        if (UnityServices.State == ServicesInitializationState.Uninitialized) await UnityServices.InitializeAsync();
        while (UnityServices.State == ServicesInitializationState.Initializing) await Task.Yield();
        if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync();
        signedIn = true;
    }

    // ------------------------------------------------------------ relay

    public async void CreateRelay(int maxPlayers, Action<OnlineError, string> done)
    {
        var e = OnlineError.None;
        string code = "";
        try
        {
            await EnsureSignedIn();
            var backend = NetcodeBackend.Instance;
            if (backend == null) throw new NotSetUpException();
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(Mathf.Clamp(maxPlayers - 1, 1, NetSession.MaxClients));
            code = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            backend.UseRelay(allocation.ToRelayServerData(ConnectionType));
        }
        catch (Exception ex) { e = Map(ex, OnlineError.ServiceError); code = ""; }
        done?.Invoke(e, code ?? "");
    }

    public async void JoinRelay(string code, Action<OnlineError> done)
    {
        var e = OnlineError.None;
        try
        {
            await EnsureSignedIn();
            var backend = NetcodeBackend.Instance;
            if (backend == null) throw new NotSetUpException();
            JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(JoinCodes.Normalize(code));
            backend.UseRelay(allocation.ToRelayServerData(ConnectionType));
        }
        catch (Exception ex) { e = Map(ex, OnlineError.WrongCode); }
        done?.Invoke(e);
    }

    // ------------------------------------------------------------ lobby (public farms list)

    public async void PublishLobby(LobbyInfo info, Action<OnlineError> done)
    {
        var e = OnlineError.None;
        try
        {
            await EnsureSignedIn();
            await CloseLobbyAsync();
            var options = new CreateLobbyOptions
            {
                IsPrivate = false,
                Data = new Dictionary<string, DataObject>
                {
                    { "code", new DataObject(DataObject.VisibilityOptions.Public, info.code) },
                    { "ver", new DataObject(DataObject.VisibilityOptions.Public, info.version, DataObject.IndexOptions.S1) },
                    { "map", new DataObject(DataObject.VisibilityOptions.Public, info.map) },
                    { "players", new DataObject(DataObject.VisibilityOptions.Public, info.players.ToString(CultureInfo.InvariantCulture)) },
                },
            };
            string name = string.IsNullOrEmpty(info.farmName) ? "Mazraa" : info.farmName;
            Lobby lobby = await LobbyService.Instance.CreateLobbyAsync(name, Mathf.Clamp(info.maxPlayers, 2, NetSession.MaxClients + 1), options);
            lobbyId = lobby.Id;
            heartbeat = HeartbeatSeconds;
        }
        catch (Exception ex) { e = Map(ex, OnlineError.ServiceError); }
        done?.Invoke(e);
    }

    public void UpdateLobby(int players)
    {
        if (string.IsNullOrEmpty(lobbyId)) return;
        pendingPlayers = players;
        if (updateTimer <= 0f) updateTimer = UpdateDelay;
    }

    public async void CloseLobby()
    {
        try { await CloseLobbyAsync(); }
        catch (Exception ex) { Debug.LogWarning("[Online] Could not remove the public farm: " + ex.Message); }
    }

    private async Task CloseLobbyAsync()
    {
        string id = lobbyId;
        lobbyId = null;
        pendingPlayers = -1;
        if (!string.IsNullOrEmpty(id)) await LobbyService.Instance.DeleteLobbyAsync(id);
    }

    public async void QueryLobbies(string version, Action<OnlineError, List<LobbyInfo>> done)
    {
        var e = OnlineError.None;
        var list = new List<LobbyInfo>();
        try
        {
            await EnsureSignedIn();
            var options = new QueryLobbiesOptions
            {
                Count = 25,
                Filters = new List<QueryFilter> { new QueryFilter(QueryFilter.FieldOptions.S1, version ?? "", QueryFilter.OpOptions.EQ) },
            };
            QueryResponse response = await LobbyService.Instance.QueryLobbiesAsync(options);
            if (response != null && response.Results != null)
                foreach (var l in response.Results)
                {
                    if (l == null) continue;
                    var info = new LobbyInfo { id = l.Id ?? "", farmName = l.Name ?? "", maxPlayers = l.MaxPlayers };
                    info.code = Data(l, "code");
                    info.version = Data(l, "ver");
                    info.map = Data(l, "map");
                    int n;
                    info.players = int.TryParse(Data(l, "players"), NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : 1;
                    if (JoinCodes.IsValid(info.code) && info.players < info.maxPlayers) list.Add(info);
                }
        }
        catch (Exception ex) { e = Map(ex, OnlineError.ServiceError); }
        done?.Invoke(e, list);
    }

    private static string Data(Lobby l, string key)
    {
        DataObject d;
        return l.Data != null && l.Data.TryGetValue(key, out d) && d != null ? d.Value ?? "" : "";
    }

    public void Tick(float dt)
    {
        if (string.IsNullOrEmpty(lobbyId)) return;
        heartbeat -= dt;
        if (heartbeat <= 0f) { heartbeat = HeartbeatSeconds; Heartbeat(lobbyId); }
        if (updateTimer > 0f)
        {
            updateTimer -= dt;
            if (updateTimer <= 0f && pendingPlayers >= 0) { SendPlayers(lobbyId, pendingPlayers); pendingPlayers = -1; }
        }
    }

    private static async void Heartbeat(string id)
    {
        try { await LobbyService.Instance.SendHeartbeatPingAsync(id); }
        catch (Exception ex) { Debug.LogWarning("[Online] Lobby heartbeat failed: " + ex.Message); }
    }

    private static async void SendPlayers(string id, int players)
    {
        try
        {
            await LobbyService.Instance.UpdateLobbyAsync(id, new UpdateLobbyOptions
            {
                Data = new Dictionary<string, DataObject> { { "players", new DataObject(DataObject.VisibilityOptions.Public, players.ToString(CultureInfo.InvariantCulture)) } },
            });
        }
        catch (Exception ex) { Debug.LogWarning("[Online] Lobby update failed: " + ex.Message); }
    }

    // ------------------------------------------------------------ errors

    private static OnlineError Map(Exception ex, OnlineError fallback)
    {
        Debug.LogWarning("[Online] " + ex.GetType().Name + ": " + ex.Message);
        if (ex is NoInternetException) return OnlineError.NoInternet;
        if (ex is NotSetUpException || ex is ServicesInitializationException) return OnlineError.NotSetUp;
        var relay = ex as RelayServiceException;
        if (relay != null)
        {
            switch (relay.Reason)
            {
                case RelayExceptionReason.JoinCodeNotFound:
                case RelayExceptionReason.InvalidArgument:
                case RelayExceptionReason.InvalidRequest:
                case RelayExceptionReason.EntityNotFound:
                case RelayExceptionReason.AllocationNotFound:
                    return OnlineError.WrongCode;
                case RelayExceptionReason.RateLimited: return OnlineError.RateLimited;
                case RelayExceptionReason.NetworkError: return OnlineError.NoInternet;
            }
        }
        var lobby = ex as LobbyServiceException;
        if (lobby != null)
        {
            if (lobby.Reason == LobbyExceptionReason.LobbyFull) return OnlineError.Full;
            if (lobby.Reason == LobbyExceptionReason.RateLimited) return OnlineError.RateLimited;
            if (lobby.Reason == LobbyExceptionReason.NetworkError) return OnlineError.NoInternet;
        }
        if (ex is AuthenticationException) return Application.internetReachability == NetworkReachability.NotReachable ? OnlineError.NoInternet : OnlineError.SignInFailed;
        var failed = ex as RequestFailedException;
        if (failed != null)
        {
            if (failed.ErrorCode == CommonErrorCodes.TransportError || failed.ErrorCode == CommonErrorCodes.Timeout) return OnlineError.NoInternet;
            if (failed.ErrorCode == CommonErrorCodes.TooManyRequests) return OnlineError.RateLimited;
        }
        if (ex is InvalidOperationException) return OnlineError.NotSetUp;
        return fallback;
    }
}
#endif
