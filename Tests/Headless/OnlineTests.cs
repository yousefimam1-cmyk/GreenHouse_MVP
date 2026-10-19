// Online co-op (Unity Relay join codes, public games list) without Unity Gaming Services: a fake IOnlineServices
// stands in for Relay / Lobby / Authentication, TestBackend for Netcode. Covers the offline default, join codes, the
// connection handshake (game version, bans), the name list, LAN approval and the market / contracts sync.
// The front-end flows (host options, code picker, browse, kick / ban from the menu) are in FrontEndTests.cs.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>Fake Unity Gaming Services: every call waits until the test calls Complete() (like a real network call).</summary>
class FakeOnline : IOnlineServices
{
    public bool Available => true;
    bool signedIn;
    public string PlayerId => signedIn ? "anon-me" : "";
    public OnlineError FailNext = OnlineError.None;
    public string Code = "ABC123";
    Action pending;
    public LobbyInfo Published;
    public bool Closed;
    public int LastCount = -1;
    public string JoinedCode, QueriedVersion;
    public int RelayPlayers;
    public readonly List<LobbyInfo> Lobbies = new List<LobbyInfo>();
    public float Ticked;

    public bool HasPending => pending != null;
    public void Complete() { var p = pending; pending = null; p?.Invoke(); }
    OnlineError Take() { var e = FailNext; FailNext = OnlineError.None; if (e == OnlineError.None) signedIn = true; return e; }

    public void SignIn(Action<OnlineError> done) => pending = () => done(Take());
    public void CreateRelay(int maxPlayers, Action<OnlineError, string> done) { RelayPlayers = maxPlayers; pending = () => { var e = Take(); done(e, e == OnlineError.None ? Code : ""); }; }
    public void JoinRelay(string code, Action<OnlineError> done) { JoinedCode = code; pending = () => done(Take()); }
    public void PublishLobby(LobbyInfo info, Action<OnlineError> done) { Published = info; Closed = false; done(OnlineError.None); }
    public void UpdateLobby(int players) => LastCount = players;
    public void CloseLobby() => Closed = true;
    public void QueryLobbies(string version, Action<OnlineError, List<LobbyInfo>> done) { QueriedVersion = version; pending = () => { var e = Take(); done(e, e == OnlineError.None ? new List<LobbyInfo>(Lobbies) : new List<LobbyInfo>()); }; }
    public void Tick(float dt) => Ticked += dt;
}

static class OnlineTests
{
    public static void Run()
    {
        Console.WriteLine("Online co-op: services, join codes, handshake, names, market sync");
        var mp = MultiplayerManager.Instance;
        int e0 = FakeEngine.Errors.Count;

        // ---- offline default: no services compiled in
        NetSession.Register(null); NetSession.RegisterOnline(null);
        T.Check("No services by default (OfflineServices)", !NetSession.Online.Available && !MultiplayerManager.OnlineAvailable && NetSession.Online.PlayerId == "");
        var net = new TestBackend();
        NetSession.Register(net);
        FakeEngine.Run(0.2f);
        T.Check("Netcode but no services: online hosting refused with a clear message (LAN still possible)", !mp.HostOnline() && mp.Status == OnlineErrors.Message(OnlineError.NotSetUp)
            && !mp.JoinWithCode("ABC123") && !mp.BrowsePublic() && mp.Busy == MultiplayerManager.OnlineStep.Idle && !NetSession.IsOnline);

        // ---- join codes
        T.Check("Join codes: 6 letters / digits, normalised, picker wraps", JoinCodes.IsValid("ABC123") && JoinCodes.IsValid("a1b2c3") && !JoinCodes.IsValid("ABC12") && !JoinCodes.IsValid("ABC1234")
            && !JoinCodes.IsValid("ABC_12") && JoinCodes.Normalize("ab-c 12 3") == "ABC123" && JoinCodes.Step('A', -1) == '9' && JoinCodes.Step('9', 1) == 'A' && JoinCodes.Step('Z', 1) == '0'
            && JoinCodes.Pretty("abc123") == "ABC 123");

        // ---- handshake
        var h = NetHandshake.Decode(NetHandshake.Encode("1.2.3", "id|x", "Golden Palm 7"));
        T.Check("Handshake round-trips (separators stripped)", h.ok && h.version == "1.2.3" && h.playerId == "idx" && h.name == "Golden Palm 7");
        T.Check("Handshake: junk is not a handshake", !NetHandshake.Decode("hello").ok && !NetHandshake.Decode("").ok && !NetHandshake.Decode("other|1|2|3").ok);
        T.Check("Reason codes from the host become player messages, other text is ignored", NetHandshake.ReasonMessage(NetHandshake.Version + ":2.0") != "" && NetHandshake.ReasonMessage("Disconnected by server") == ""
            && NetHandshake.ReasonMessage(NetHandshake.Closed) == "The host left the game");
        var bans = new SessionBans();
        bans.Add("anon-1", "Busy Bee 3"); bans.Add("", "Lan Guy");
        T.Check("Session bans: by anonymous id online, by name on LAN", bans.IsBanned("anon-1", "Other Name 2") && !bans.IsBanned("anon-2", "Busy Bee 3") && bans.IsBanned("", "lan guy") && !bans.IsBanned("anon-3", "Lan Guy"));

        // ---- the name list: friendly, unique, translated, passes the chat filter
        var names = PlayerNames.All.Select(n => n[0]).ToList();
        T.Check("Player names: 30 friendly names, unique, Arabic for each, nothing the chat filter would hide", names.Count >= 30 && names.Distinct().Count() == names.Count
            && PlayerNames.All.All(n => !string.IsNullOrEmpty(n[1])) && names.All(n => ChatSafety.Filter(n) == n) && PlayerNames.All.All(n => ChatSafety.Filter(n[1]) == n[1])
            && names.All(n => NetProtocol.Clean(n + " 99", 20) == n + " 99"), string.Join(",", names.Where(n => ChatSafety.Filter(n) != n || (n + " 99").Length > 20)));
        PlayerNames.Index = 3; PlayerNames.Number = 42;
        T.Check("Player names: picker index / number stored, current name is listed", PlayerNames.Current == PlayerNames.All[3][0] + " 42" && PlayerNames.IsGenerated(PlayerNames.Current));
        PlayerNames.Index = -1;
        T.Check("Player names: picker wraps around the list", PlayerNames.Index == PlayerNames.Count - 1);
        PlayerNames.Number = 100;
        T.Check("Player names: number wraps 1-99", PlayerNames.Number == 1);

        // ---- LAN host: the version handshake applies too (typed names allowed on LAN)
        net = new TestBackend { mode = NetMode.Offline };
        NetSession.Register(net);
        FakeEngine.Run(0.1f);
        T.Check("LAN host started", mp.Host() && NetSession.IsHost && !mp.IsOnlineSession);
        string ver = NetHandshake.GameVersion;
        T.Check("LAN: wrong version refused", (net.TryJoin(30, NetHandshake.Encode("0.0.1", "", "Sara")) ?? "").StartsWith(NetHandshake.Version));
        T.Check("LAN: typed names are fine between friends at home", net.TryJoin(31, NetHandshake.Encode(ver, "", "Sara")) == null && mp.Players[31] == "Sara");
        NetHandshake.GameVersion = "7.7.7";
        T.Check("Version comes from the game version (override for tests)", (net.TryJoin(32, NetHandshake.Encode(ver, "", "Omar")) ?? "").StartsWith(NetHandshake.Version + ":7.7.7"));
        NetHandshake.GameVersion = null;
        T.Check("Game version defaults to Application.version", NetHandshake.GameVersion == Application.version);
        mp.Block(31, "Sara");
        T.Check("Block (host): removed with the 'blocked' reason", net.KickReasons.ContainsKey(31) && net.KickReasons[31] == NetHandshake.Blocked);
        ChatSafety.SetBlocked("Sara", false);
        net.RaiseClientDisconnected(31);
        mp.Leave();

        // ---- market sync: a client sees the host's prices and contracts board; accepting goes to the host
        var market = MarketManager.Instance;
        var eco = EconomyManager.Instance;
        string good = eco.Data.goods.First(g => market.Price(g.id) > 0f).id;
        float ownPrice = market.Price(good);
        var ownOffers = market.Offers.Select(o => o.id).ToList();
        var hostState = Newtonsoft.Json.Linq.JObject.Parse(NetProtocol.Split(market.EncodeSync())[1]);
        foreach (var t in hostState["trend"]) if ((string)t["k"] == good) t["v"] = 1.45f;
        var offers = (Newtonsoft.Json.Linq.JArray)hostState["offers"];
        offers.Clear();
        offers.Add(Newtonsoft.Json.Linq.JObject.FromObject(new Contract { id = "host-7", buyerEn = "Host Buyer", buyerAr = "مشتري المضيف", good = good, amount = 50, days = 5, reward = 900, board = true, kind = "deliver" }));
        string hostMsg = NetProtocol.Make(NetProtocol.MarketSync, hostState.ToString(Newtonsoft.Json.Formatting.None));
        net = new TestBackend { mode = NetMode.Client, localId = 9 };
        NetSession.Register(net);
        FakeEngine.Run(0.1f);
        net.RaiseConnected();
        net.Receive(0, hostMsg);
        T.Check("Client market: host's prices and contracts board shown", Math.Abs(market.Price(good) - ownPrice) > 0.01f && market.Offers.Count == 1 && market.Offers[0].id == "host-7",
            ownPrice + " -> " + market.Price(good));
        net.Sent.Clear();
        T.Check("Client market: accepting a contract asks the host (board unchanged until the next sync)", market.Accept(market.Offers[0]) && market.Offers.Count == 1 && market.Contracts.All(c => c.id != "host-7")
            && net.Sent.Any(x => x.msg == NetProtocol.Make(NetProtocol.ContractAccept, "host-7")));
        net.RaiseRefused("");
        T.Check("Leaving: own prices and contracts board back", Math.Abs(market.Price(good) - ownPrice) < 0.01f && market.Offers.Select(o => o.id).SequenceEqual(ownOffers), market.Price(good) + " vs " + ownPrice);

        // host side: a client's accept is applied and the new board is sent
        net = new TestBackend { mode = NetMode.Host, localId = 0 };
        net.Clients.Add(5);
        NetSession.Register(net);
        FakeEngine.Run(0.1f);
        mp.Players[0] = mp.PlayerName;
        net.RaiseClientConnected(5);
        if (market.Offers.Count == 0) market.GenerateOffer(false);
        var offer = market.Offers[0];
        FakeEngine.Run(0.2f);
        net.Sent.Clear();
        net.Receive(5, NetProtocol.Make(NetProtocol.ContractAccept, offer.id));
        FakeEngine.Run(0.2f);
        T.Check("Host: a client's contract accept is applied and the board synced right after", market.Contracts.Contains(offer) && !market.Offers.Contains(offer)
            && net.Sent.Any(x => x.to == 5 && x.msg.StartsWith(NetProtocol.MarketSync + NetProtocol.Sep) && x.msg.Contains(offer.id)));
        net.Sent.Clear();
        net.Receive(5, NetProtocol.Make(NetProtocol.ContractAccept, "no-such-contract"));
        T.Check("Host: an unknown contract gets a polite answer", net.Sent.Any(x => x.to == 5 && x.msg.StartsWith(NetProtocol.Alert) && x.msg.Contains("no longer available")));
        net.Sent.Clear();
        FakeEngine.Run(5.5f);
        T.Check("Host: market sent every few seconds (prices change daily)", net.Sent.Count(x => x.msg.StartsWith(NetProtocol.MarketSync + NetProtocol.Sep)) >= 1);
        net.RaiseClientDisconnected(5);
        mp.Leave();

        // ---- online services tick every frame (lobby heartbeats)
        var fake = new FakeOnline();
        NetSession.RegisterOnline(fake);
        FakeEngine.Run(1f);
        T.Check("Online services ticked every frame (lobby heartbeat)", fake.Ticked > 0.5f);
        fake.FailNext = OnlineError.Full;
        mp.HostOnline(); fake.Complete();
        T.Check("Online host: a failed Relay request leaves you offline with a message", !NetSession.IsOnline && mp.Status == OnlineErrors.Message(OnlineError.Full));
        mp.HostOnline();
        mp.CancelOnline();
        fake.Complete();
        T.Check("Cancel: a late Relay answer is ignored", !NetSession.IsOnline && mp.Busy == MultiplayerManager.OnlineStep.Idle && mp.JoinCode == "");
        MultiplayerManager.MaxPlayers = 2;
        mp.HostOnline(); fake.Complete();
        T.Check("Online host: Relay asked for the chosen farm size; code-only farms are not listed", NetSession.IsHost && fake.RelayPlayers == 2 && fake.Published == null && mp.JoinCode == "ABC123");
        T.Check("Online host of 2: one friend fills the farm", net.TryJoin(40, NetHandshake.Encode(NetHandshake.GameVersion, "anon-40", "Olive Tree 4")) == null
            && net.TryJoin(41, NetHandshake.Encode(NetHandshake.GameVersion, "anon-41", "Rain Cloud 9")) == NetHandshake.Full);
        mp.Leave();
        MultiplayerManager.MaxPlayers = 4;
        NetSession.Register(null); NetSession.RegisterOnline(null);

        T.Check("Online co-op: no errors", FakeEngine.Errors.Count == e0, string.Join(" | ", FakeEngine.Errors.Skip(e0).Take(3)));
    }
}
