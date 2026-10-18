using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>Why an online step failed (shown to the player as a clear message, see OnlineErrors.Message).</summary>
public enum OnlineError { None, NotSetUp, NoInternet, SignInFailed, WrongCode, Full, RateLimited, ServiceError }

/// <summary>Who can find and join an online farm.</summary>
public enum OnlineVisibility
{
    /// <summary>Not listed; friends type the join code, and only players on your friends list get in.</summary>
    FriendsOnly,
    /// <summary>Not listed; anyone with the join code can come in.</summary>
    CodeOnly,
    /// <summary>Listed under "Browse public games" (farm name, map, players), and joinable with the code.</summary>
    Public,
}

/// <summary>A public farm in the online games list.</summary>
public class LobbyInfo
{
    public string id = "", farmName = "", map = "", code = "", version = "";
    public int players = 1, maxPlayers = 4;
}

/// <summary>
/// Unity Gaming Services as seen by the game: anonymous sign-in, Relay (join codes) and Lobby (public games list).
/// The real implementation (UgsOnlineServices.cs) is only compiled with MAZRAA_ONLINE, so the project builds without
/// the services package; tests use a fake. Every call finishes through its callback on the main thread.
/// </summary>
public interface IOnlineServices
{
    /// <summary>False when the Unity Gaming Services package isn't compiled in (online play hidden).</summary>
    bool Available { get; }
    /// <summary>Anonymous Unity player id once signed in ("" before). No name, e-mail or account.</summary>
    string PlayerId { get; }
    void SignIn(Action<OnlineError> done);
    /// <summary>Host: reserve a Relay server for maxPlayers (host included), set the network transport to it and
    /// return the join code friends type.</summary>
    void CreateRelay(int maxPlayers, Action<OnlineError, string> done);
    /// <summary>Client: find the Relay server of a join code and set the network transport to it.</summary>
    void JoinRelay(string code, Action<OnlineError> done);
    /// <summary>Host of a public farm: list it in the online games list.</summary>
    void PublishLobby(LobbyInfo info, Action<OnlineError> done);
    /// <summary>Host: new player count for the public list (ignored when not listed).</summary>
    void UpdateLobby(int players);
    /// <summary>Host: remove the farm from the public list (stopped hosting).</summary>
    void CloseLobby();
    /// <summary>Public farms of this game version that still have room.</summary>
    void QueryLobbies(string version, Action<OnlineError, List<LobbyInfo>> done);
    /// <summary>Called every frame (keeps a listed farm alive with heartbeats).</summary>
    void Tick(float unscaledDeltaTime);
}

/// <summary>No services in this build: online play is unavailable (LAN still works with Netcode).</summary>
public class OfflineServices : IOnlineServices
{
    public bool Available => false;
    public string PlayerId => "";
    public void SignIn(Action<OnlineError> done) => done?.Invoke(OnlineError.NotSetUp);
    public void CreateRelay(int maxPlayers, Action<OnlineError, string> done) => done?.Invoke(OnlineError.NotSetUp, "");
    public void JoinRelay(string code, Action<OnlineError> done) => done?.Invoke(OnlineError.NotSetUp);
    public void PublishLobby(LobbyInfo info, Action<OnlineError> done) => done?.Invoke(OnlineError.NotSetUp);
    public void UpdateLobby(int players) { }
    public void CloseLobby() { }
    public void QueryLobbies(string version, Action<OnlineError, List<LobbyInfo>> done) => done?.Invoke(OnlineError.NotSetUp, new List<LobbyInfo>());
    public void Tick(float unscaledDeltaTime) { }
}

/// <summary>Player-facing texts for online errors (English Loc keys, Arabic in Data/Loc/99_online.json).</summary>
public static class OnlineErrors
{
    public static string Message(OnlineError e)
    {
        switch (e)
        {
            case OnlineError.NotSetUp: return "Online play is not available in this version";
            case OnlineError.NoInternet: return "No internet connection - check your network and try again";
            case OnlineError.SignInFailed: return "Could not reach the game servers - try again in a moment";
            case OnlineError.WrongCode: return "That code didn't work - check the 6 letters with your friend";
            case OnlineError.Full: return "That farm is full";
            case OnlineError.RateLimited: return "Too many tries - wait a few seconds";
            case OnlineError.ServiceError: return "The game servers had a problem - try again in a moment";
            default: return "";
        }
    }
}

/// <summary>Join codes (Unity Relay): 6 letters / digits, typed with a character picker (no keyboard needed).</summary>
public static class JoinCodes
{
    public const int Length = 6;
    public const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    /// <summary>Upper case, spaces and dashes removed.</summary>
    public static string Normalize(string code)
    {
        if (string.IsNullOrEmpty(code)) return "";
        var sb = new StringBuilder(code.Length);
        foreach (char ch in code.Trim().ToUpperInvariant()) if (ch != ' ' && ch != '-') sb.Append(ch);
        return sb.ToString();
    }

    public static bool IsValid(string code)
    {
        code = Normalize(code);
        if (code.Length != Length) return false;
        foreach (char c in code) if (Alphabet.IndexOf(c) < 0) return false;
        return true;
    }

    /// <summary>Character picker: the next / previous character of the alphabet.</summary>
    public static char Step(char c, int d)
    {
        int i = Alphabet.IndexOf(char.ToUpperInvariant(c));
        if (i < 0) i = 0;
        int n = Alphabet.Length;
        return Alphabet[((i + d) % n + n) % n];
    }

    /// <summary>"ABC DEF": easier to read out loud.</summary>
    public static string Pretty(string code)
    {
        code = Normalize(code);
        return code.Length == Length ? code.Substring(0, 3) + " " + code.Substring(3) : code;
    }
}

/// <summary>
/// Connection handshake: the joining player sends "mazraa|version|player id|name" with the connection request; the
/// host accepts or answers with a reason code (version mismatch, full, banned, friends only, blocked, kicked) that
/// the joining player sees as a clear message.
/// </summary>
public static class NetHandshake
{
    public const string Magic = "mazraa";
    public const string Prefix = "mz:";
    public const string Version = "mz:version", Full = "mz:full", Banned = "mz:banned", FriendsOnly = "mz:friends", Blocked = "mz:blocked",
                        Kicked = "mz:kicked", BadName = "mz:name", Closed = "mz:closed";

    /// <summary>Game version sent in the handshake (Player Settings version; tests can override it).</summary>
    public static string GameVersion
    {
        get => versionOverride ?? Application.version;
        set => versionOverride = value;
    }
    private static string versionOverride;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { versionOverride = null; }

    public struct Request { public bool ok; public string version, playerId, name; }

    public static string Encode(string version, string playerId, string name)
        => string.Join("|", new[] { Magic, Clean(version, 24), Clean(playerId, 64), Clean(name, 24) });

    public static Request Decode(string payload)
    {
        var r = new Request { version = "", playerId = "", name = "" };
        if (string.IsNullOrEmpty(payload)) return r;
        var p = payload.Split('|');
        if (p.Length < 4 || p[0] != Magic) return r;
        r.ok = true;
        r.version = Clean(p[1], 24);
        r.playerId = Clean(p[2], 64);
        r.name = NetProtocol.Clean(p[3], 24);
        return r;
    }

    private static string Clean(string s, int max) => NetProtocol.Clean((s ?? "").Replace("|", ""), max);

    /// <summary>The message a joining player sees for a reason code from the host ("" = not one of ours).</summary>
    public static string ReasonMessage(string reason)
    {
        if (string.IsNullOrEmpty(reason) || !reason.StartsWith(Prefix)) return "";
        if (reason.StartsWith(Version)) return "Different game version - update Mazraa so you both have the same version";
        switch (reason)
        {
            case Full: return "That farm is full";
            case Banned: return "The host removed you from this farm";
            case Kicked: return "The host removed you from the farm";
            case FriendsOnly: return "Only the host's friends can join this farm";
            case Blocked: return "You can't join this farm";
            case BadName: return "Pick a player name from the list to play online";
            case Closed: return "The host left the game";
        }
        return "";
    }
}

/// <summary>
/// Players a host banned for this session (they can't come back until the host stops hosting). Matched by the
/// anonymous Unity player id when there is one, otherwise by name (LAN).
/// </summary>
public class SessionBans
{
    private readonly HashSet<string> ids = new HashSet<string>();
    private readonly HashSet<string> names = new HashSet<string>();

    public int Count => ids.Count + names.Count;
    public void Clear() { ids.Clear(); names.Clear(); }

    public void Add(string playerId, string name)
    {
        if (!string.IsNullOrEmpty(playerId)) ids.Add(playerId);
        else if (!string.IsNullOrEmpty(name)) names.Add(name.Trim().ToLowerInvariant());
    }

    public bool IsBanned(string playerId, string name)
    {
        if (!string.IsNullOrEmpty(playerId)) return ids.Contains(playerId);
        return !string.IsNullOrEmpty(name) && names.Contains(name.Trim().ToLowerInvariant());
    }
}
