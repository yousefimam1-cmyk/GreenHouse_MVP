using System;
using System.Collections.Generic;
using StarterAssets;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Co-op on one farm (host + up to 3 players over LAN/IP). Works through NetSession.Backend, so it has no Netcode
/// dependency. The host runs the real farm: it applies the requests clients send (interact, buy, sell, tractor work,
/// gifts), and every second sends the farm state (NetSyncState). Every player sends its own position ~10 times a
/// second; the host relays them. Also: emotes (1-4), quick chat (Enter, see MultiplayerPanel) and gifts.
/// A client's own save file is never touched while connected (SaveData session); gifts are added when leaving.
/// </summary>
[MazraaSystem(70)]
public class MultiplayerManager : MonoBehaviour
{
    private static readonly Key[] EmoteKeys = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4 };
    public static MultiplayerManager Instance;

    public const string NameKey = "mazraa.mp.name";
    public const string AddressKey = "mazraa.mp.ip";
    public const string GiftsKey = "mazraa.mp.giftsReceived";
    public const float GiftRange = 8f;
    public const int GiftAmount = 5;

    private static readonly string[] Emotes = { "Hello!", "Dance time!", "Yalla, well done!", "Thank you!" };
    public static string EmoteText(int i) => Emotes[Mathf.Clamp(i, 0, Emotes.Length - 1)];

    public class ChatLine { public string text, from, message; public float time; }

    public string PlayerName { get; private set; }
    public string LastAddress { get; private set; }
    /// <summary>Last status/error, English Loc key.</summary>
    public string Status { get; private set; } = "";
    /// <summary>Everyone on the farm (including you): id -> name.</summary>
    public readonly SortedDictionary<ulong, string> Players = new SortedDictionary<ulong, string>();
    public readonly List<ChatLine> ChatLog = new List<ChatLine>();
    /// <summary>Goods given to you as a visitor; added to your own farm when you leave.</summary>
    public readonly Dictionary<string, float> PendingGifts = new Dictionary<string, float>();
    public IEnumerable<RemotePlayer> Remotes => remotes.Values;

    private readonly Dictionary<ulong, RemotePlayer> remotes = new Dictionary<ulong, RemotePlayer>();
    private readonly Dictionary<ulong, Vehicle> drivenBy = new Dictionary<ulong, Vehicle>();
    private readonly Dictionary<ulong, float> lastGift = new Dictionary<ulong, float>();
    private INetBackend backend;
    private bool inClientSession;
    private float poseTimer, syncTimer;
    private bool syncDirty;
    private Transform player;
    private Vector3 lastPlayerPos;
    private Transform remotesRoot;

    private void Awake()
    {
        Instance = this;
        PlayerName = SaveData.GetString(NameKey, "");
        if (string.IsNullOrEmpty(PlayerName)) PlayerName = "Farmer " + UnityEngine.Random.Range(100, 1000);
        LastAddress = SaveData.GetString(AddressKey, "127.0.0.1");
        remotesRoot = new GameObject("Remote Players").transform;
    }

    private void Start() => Attach();

    private void OnDestroy()
    {
        Detach();
        if (Instance == this) Instance = null;
    }

    private void OnApplicationQuit()
    {
        if (inClientSession) EndClientSession();
        SaveData.Flush(true);
    }

    // ------------------------------------------------------------ backend wiring

    private void Attach()
    {
        if (backend == NetSession.Backend) return;
        Detach();
        backend = NetSession.Backend;
        backend.Received += OnReceived;
        backend.ClientConnected += OnClientConnected;
        backend.ClientDisconnected += OnClientDisconnected;
        backend.Connected += OnConnected;
        backend.Disconnected += OnDisconnected;
    }

    private void Detach()
    {
        if (backend == null) return;
        backend.Received -= OnReceived;
        backend.ClientConnected -= OnClientConnected;
        backend.ClientDisconnected -= OnClientDisconnected;
        backend.Connected -= OnConnected;
        backend.Disconnected -= OnDisconnected;
        backend = null;
    }

    // ------------------------------------------------------------ menu actions

    public void SetPlayerName(string playerName)
    {
        playerName = NetProtocol.Clean(playerName, 20);
        if (playerName.Length == 0 || playerName == PlayerName) return;
        PlayerName = playerName;
        if (!SaveData.InSession) { SaveData.SetString(NameKey, PlayerName); SaveData.Save(); }
        if (NetSession.IsClient) backend.SendToHost(NetProtocol.Make(NetProtocol.Hello, PlayerName), true);
        else if (NetSession.IsHost) { Players[backend.LocalId] = PlayerName; BroadcastRoster(); }
    }

    public bool Host()
    {
        Attach();
        if (NetSession.IsOnline) return false;
        if (!backend.Available) { Status = "Multiplayer needs the Netcode package (see Documentation.txt)"; return false; }
        if (!backend.StartHost(NetSession.DefaultPort)) { Status = "Could not host - is port 7777 already in use?"; return false; }
        Players.Clear();
        Players[backend.LocalId] = PlayerName;
        Status = "Hosting on port 7777 - friends join with your IP address";
        syncDirty = true;
        return true;
    }

    public bool Join(string address)
    {
        Attach();
        if (NetSession.IsOnline || backend.Connecting) return false;
        address = (address ?? "").Trim();
        if (address.Length == 0) address = "127.0.0.1";
        LastAddress = address;
        SaveData.SetString(AddressKey, address); SaveData.Save();
        if (!backend.Available) { Status = "Multiplayer needs the Netcode package (see Documentation.txt)"; return false; }
        if (!backend.StartClient(address, NetSession.DefaultPort)) { Status = "Could not connect - check the IP address"; return false; }
        Status = "Connecting...";
        return true;
    }

    public void Leave()
    {
        bool wasClient = inClientSession;
        if (backend != null) backend.Leave();
        CleanupSession();
        Status = wasClient ? "You left the farm - back on your own farm" : "Stopped hosting";
        Say(Mp.Tr(Status));
    }

    // ------------------------------------------------------------ connection events

    private void OnClientConnected(ulong id)
    {
        if (!NetSession.IsHost) return;
        if (Players.Count > NetSession.MaxClients) { backend.Kick(id); return; }   // host + 3 already here
        Players[id] = "Player " + id;
        backend.SendToClient(id, NetProtocol.Make(NetProtocol.Welcome, NetProtocol.I((long)id)), true);
        BroadcastRoster();
        syncDirty = true;
    }

    private void OnClientDisconnected(ulong id)
    {
        if (!NetSession.IsHost) return;
        string n;
        if (Players.TryGetValue(id, out n)) Say(Mp.Tr("{0} left the farm", Mp.Name(n)));
        Players.Remove(id);
        RemoveRemote(id);
        BroadcastRoster();
    }

    private void OnConnected()
    {
        BeginClientSession();
        Status = "Connected - you are on the host's farm";
        backend.SendToHost(NetProtocol.Make(NetProtocol.Hello, PlayerName), true);
        Say(Mp.Tr(Status));
    }

    private void OnDisconnected()
    {
        bool was = inClientSession;
        CleanupSession();
        Status = was ? "Disconnected from the host" : "Could not connect - check the IP address and firewall";
        Say(Mp.Tr(Status));
    }

    private void CleanupSession()
    {
        if (inClientSession) EndClientSession();
        foreach (var id in new List<ulong>(remotes.Keys)) RemoveRemote(id);
        Players.Clear();
        lastGift.Clear();
    }

    // ------------------------------------------------------------ client session (save isolation)

    /// <summary>Joined someone's farm: stop writing our own save, let the host drive missions and the calendar.</summary>
    public void BeginClientSession()
    {
        if (inClientSession) return;
        inClientSession = true;
        PendingGifts.Clear();
        SaveData.BeginSession();
        if (MissionManager.Instance != null) MissionManager.Instance.enabled = false;   // progress comes from the host
        if (WorldClock.Instance != null) WorldClock.Instance.FollowRemote = true;
    }

    /// <summary>Back to our own farm: reload the synced values from our save (untouched while away) plus gifts.</summary>
    public void EndClientSession()
    {
        if (!inClientSession) return;
        inClientSession = false;
        SaveData.EndSession();
        var eco = EconomyManager.Instance;
        if (eco != null)
        {
            var goods = new Dictionary<string, float>();
            foreach (var g in eco.Data.goods)
            {
                float v = SaveData.GetFloat("mazraa.store." + g.id, 0f);
                float gift;
                if (PendingGifts.TryGetValue(g.id, out gift) && gift > 0f) { v += gift; SaveData.SetFloat("mazraa.store." + g.id, v); }
                goods[g.id] = v;
            }
            eco.ApplyRemote(SaveData.GetInt("mazraa.money", 500), goods);
        }
        if (PendingGifts.Count > 0)
        {
            SaveData.SetInt(GiftsKey, SaveData.GetInt(GiftsKey, 0) + PendingGifts.Count);
            Say(Mp.Tr("Gifts from your friends are in your farm storage"));
        }
        PendingGifts.Clear();
        SaveData.Save();
        var clock = WorldClock.Instance;
        if (clock != null)
        {
            clock.FollowRemote = false;
            clock.ApplyRemote(Mathf.Max(1, SaveData.GetInt("mazraa.day", 1)), SaveData.GetFloat("mazraa.dayProgress", 0.25f), (Weather)SaveData.GetInt("mazraa.weather", 0));
        }
        var mm = MissionManager.Instance;
        if (mm != null)
        {
            mm.ApplyRemote(SaveData.GetInt("mazraa.mission", 0), SaveData.GetFloat("mazraa.missionProgress", 0f), false);
            mm.enabled = true;
        }
        if (FieldManager.Instance != null)
            foreach (var p in FieldManager.Instance.Plots)
            {
                if (p == null) continue;
                string k = "mazraa.plot." + p.index + ".";
                p.ApplyRemote((PlotState)Mathf.Clamp(SaveData.GetInt(k + "state", 0), 0, 3), SaveData.GetString(k + "crop", ""), SaveData.GetFloat(k + "growth", 0f), SaveData.GetFloat(k + "water", 0f));
            }
        if (SoilManager.Instance != null) SoilManager.Instance.ReloadLocal();
        SaveData.Flush(true);
    }

    // ------------------------------------------------------------ frame

    private void Update()
    {
        Attach();   // a network backend may register after this system started
        if (!NetSession.IsOnline) return;

        poseTimer -= Time.unscaledDeltaTime;
        if (poseTimer <= 0f) { poseTimer = 0.1f; SendPose(); }

        if (NetSession.IsHost)
        {
            syncTimer -= Time.unscaledDeltaTime;
            if (syncTimer <= 0f || syncDirty)
            {
                syncTimer = 1f; syncDirty = false;
                backend.SendToClients(NetSyncState.Capture().Encode(), true);
                if (SoilManager.Instance != null) backend.SendToClients(SoilManager.Instance.EncodeSync(), true);
            }
        }

        var kb = Keyboard.current;
        bool uiOpen = (UIManager.Instance != null && UIManager.Instance.IsUIOpened) || Time.timeScale == 0f;
        if (kb != null && !uiOpen && !NetSession.Typing && Vehicle.Current == null)
        {
            for (int i = 0; i < EmoteKeys.Length; i++)
                if (kb[EmoteKeys[i]].wasPressedThisFrame) SendEmote(i);
        }
    }

    private Transform PlayerTransform()
    {
        if (player == null)
        {
            var p = FindAnyObjectByType<StarterAssetsInputs>();
            if (p != null) player = p.transform;
        }
        return player;
    }

    /// <summary>Where this player is now (on foot, or the vehicle they drive).</summary>
    public NetPose LocalPose()
    {
        var v = Vehicle.Current;
        if (v != null) return new NetPose { position = v.transform.position, yaw = v.transform.eulerAngles.y, vehicle = NetIds.Of(v), speed = v.Speed };
        var p = PlayerTransform();
        if (p == null) return new NetPose { vehicle = "" };
        return new NetPose { position = p.position, yaw = p.eulerAngles.y, vehicle = "" };
    }

    private void SendPose()
    {
        var pose = LocalPose();
        if (pose.vehicle.Length == 0)
        {   // walking speed from the last pose (sent every 0.1 s) so others see walk/run animations
            pose.speed = Mathf.Min((pose.position - lastPlayerPos).magnitude / 0.1f, 20f);
            lastPlayerPos = pose.position;
        }
        if (NetSession.IsHost)
        {
            var f = new List<string> { NetProtocol.I((long)backend.LocalId) };
            f.AddRange(pose.Fields());
            backend.SendToClients(NetProtocol.Make(NetProtocol.Pose, f.ToArray()), false);
        }
        else backend.SendToHost(NetProtocol.Make(NetProtocol.Pose, pose.Fields()), false);
    }

    // ------------------------------------------------------------ friendly extras

    public void SendEmote(int index)
    {
        if (!NetSession.IsOnline) return;
        index = Mathf.Clamp(index, 0, Emotes.Length - 1);
        Say(Mp.Tr("You") + ": " + Mp.Tr(EmoteText(index)));
        if (NetSession.IsHost) backend.SendToClients(NetProtocol.Make(NetProtocol.Emote, NetProtocol.I((long)backend.LocalId), NetProtocol.I(index)), true);
        else backend.SendToHost(NetProtocol.Make(NetProtocol.Emote, NetProtocol.I(index)), true);
    }

    public void SendChat(string text)
    {
        text = ChatSafety.Filter(NetProtocol.Clean(text, 80));
        if (!NetSession.IsOnline || text.Length == 0) return;
        if (ChatSafety.Chat == ChatSafety.ChatMode.Off) { Say(Mp.Tr("Chat is off (Co-op menu > Chat)")); return; }
        ChatLog.Add(new ChatLine { text = PlayerName + ": " + text, from = PlayerName, message = text, time = Time.unscaledTime });
        if (ChatLog.Count > 30) ChatLog.RemoveAt(0);
        if (NetSession.IsHost) backend.SendToClients(NetProtocol.Make(NetProtocol.Chat, NetProtocol.I((long)backend.LocalId), text), true);
        else backend.SendToHost(NetProtocol.Make(NetProtocol.Chat, text), true);
    }

    /// <summary>Incoming chat: filtered, and hidden when the sender is blocked or chat is limited (ChatSafety).
    /// Returns the text to show in a bubble, or null.</summary>
    private string AddChat(string from, string text)
    {
        text = ChatSafety.Filter(text);
        if (!recent.ContainsKey(from)) recent[from] = new List<string>();
        recent[from].Add(text);
        if (recent[from].Count > 5) recent[from].RemoveAt(0);
        if (!ChatSafety.ShouldShow(from)) return null;
        ChatLog.Add(new ChatLine { text = from + ": " + text, from = from, message = text, time = Time.unscaledTime });
        if (ChatLog.Count > 30) ChatLog.RemoveAt(0);
        return text;
    }

    private readonly Dictionary<string, List<string>> recent = new Dictionary<string, List<string>>();

    /// <summary>Last few messages a player sent (kept even when hidden, for reports).</summary>
    public List<string> RecentMessages(string name) => recent.ContainsKey(name) ? new List<string>(recent[name]) : new List<string>();

    /// <summary>Block a player: hide their chat; the host also removes them from the farm.</summary>
    public void Block(ulong id, string name)
    {
        ChatSafety.SetBlocked(name, true);
        ChatLog.RemoveAll(c => c.from == name);
        if (NetSession.IsHost && id != backend.LocalId && Players.ContainsKey(id)) backend.Kick(id);
        Say(Mp.Tr("{0} is blocked", Mp.Name(name)));
    }

    /// <summary>Report a player: saves the report, blocks them and returns the e-mail link to send it to the Mazraa team.</summary>
    public string Report(ulong id, string name, string reason)
    {
        string url = ChatSafety.Report(name, RecentMessages(name), reason);
        Block(id, name);
        Say(Mp.Tr("Report saved - thank you. {0} is blocked", Mp.Name(name)));
        return url;
    }

    /// <summary>The nearest other player within gift range who is a visitor (gifts go home with them), or null.</summary>
    public RemotePlayer NearestGiftTarget()
    {
        var me = LocalPose();
        RemotePlayer best = null; float bestD = GiftRange;
        foreach (var r in remotes.Values)
        {
            if (r == null || r.VehicleId.Length > 0 || r.Id == HostId) continue;
            float d = Vector3.Distance(r.TargetPosition, me.position);
            if (d <= bestD) { bestD = d; best = r; }
        }
        return best;
    }

    private ulong HostId => 0UL;   // Netcode's server client id

    /// <summary>Give GiftAmount of a good from the farm storage to a visiting player (they take it home).</summary>
    public void SendGift(ulong toId, string goodId)
    {
        if (!NetSession.IsOnline || string.IsNullOrEmpty(goodId)) return;
        string msg = NetProtocol.Make(NetProtocol.Gift, NetProtocol.I((long)toId), goodId, NetProtocol.I(GiftAmount));
        if (NetSession.IsHost) HostGift(backend.LocalId, msg);
        else backend.SendToHost(msg, true);
    }

    // ------------------------------------------------------------ receiving

    private void OnReceived(ulong sender, string msg)
    {
        try
        {
            var p = NetProtocol.Split(msg);
            if (NetSession.IsHost) HostReceive(sender, p, msg);
            else if (NetSession.IsClient) ClientReceive(p, msg);
        }
        catch (Exception e) { Debug.LogWarning("[Multiplayer] Bad message ignored: " + e.Message); }
    }

    private void HostReceive(ulong sender, string[] p, string msg)
    {
        if (sender == backend.LocalId) return;   // the host acts directly, never through messages to itself
        string type = p[0];
        string who = Players.ContainsKey(sender) ? Players[sender] : "Player " + sender;
        if (type == NetProtocol.Hello)
        {
            string n = NetProtocol.Clean(NetProtocol.Field(p, 1), 20);
            if (n.Length == 0) n = "Player " + sender;
            if (!ChatSafety.AllowJoin(n))
            {
                backend.Kick(sender);
                Say(Mp.Tr(ChatSafety.IsBlocked(n) ? "{0} is blocked and can't join your farm" : "{0} can't join: only friends can join your farm", Mp.Name(n)));
                return;
            }
            bool first = !Players.ContainsKey(sender) || Players[sender] == "Player " + sender;
            Players[sender] = n;
            if (remotes.ContainsKey(sender)) remotes[sender].SetName(n);
            BroadcastRoster();
            if (first) Say(Mp.Tr("{0} joined the farm", Mp.Name(n)));
        }
        else if (type == NetProtocol.Pose)
        {
            var pose = NetPose.Parse(p, 1);
            ApplyRemotePose(sender, pose);
            var f = new List<string> { NetProtocol.I((long)sender) };
            f.AddRange(pose.Fields());
            SendToOthers(sender, NetProtocol.Make(NetProtocol.Pose, f.ToArray()), false);
        }
        else if (type == NetProtocol.Interact) HostInteract(sender, NetProtocol.Field(p, 1));
        else if (type == NetProtocol.Buy) HostBuy(sender, who, NetProtocol.Field(p, 1));
        else if (type == NetProtocol.Sell) HostSell(sender, who, NetProtocol.DecodeGoods(NetProtocol.Field(p, 1)));
        else if (type == NetProtocol.PlotJob) HostPlotJob(NetProtocol.PI(NetProtocol.Field(p, 1)), NetProtocol.PI(NetProtocol.Field(p, 2)));
        else if (type == NetProtocol.Gift) HostGift(sender, msg);
        else if (type == NetProtocol.SoilSet) { if (SoilManager.Instance != null && SoilManager.Instance.HostSetting(p)) syncDirty = true; }
        else if (type == NetProtocol.Emote)
        {
            int i = NetProtocol.PI(NetProtocol.Field(p, 1));
            if (!ChatSafety.IsBlocked(who)) GetRemote(sender).PlayEmote(i);
            SendToOthers(sender, NetProtocol.Make(NetProtocol.Emote, NetProtocol.I((long)sender), NetProtocol.I(i)), true);
        }
        else if (type == NetProtocol.Chat)
        {
            string text = NetProtocol.Clean(NetProtocol.Field(p, 1), 80);
            if (text.Length == 0) return;
            string shown = AddChat(who, text);
            if (shown != null) GetRemote(sender).Say(shown);
            text = ChatSafety.Filter(text);
            SendToOthers(sender, NetProtocol.Make(NetProtocol.Chat, NetProtocol.I((long)sender), text), true);
        }
        else if (type == NetProtocol.Optimize)
        {
            if (ApplyOptimize(NetProtocol.Field(p, 1), NetProtocol.Field(p, 2))) SendToOthers(sender, msg, true);
        }
    }

    private void ClientReceive(string[] p, string msg)
    {
        string type = p[0];
        if (type == NetProtocol.Sync) { var s = NetSyncState.Decode(msg); if (s != null) s.Apply(); }
        else if (type == NetProtocol.SoilSync) { if (SoilManager.Instance != null) SoilManager.Instance.ApplySync(p); }
        else if (type == NetProtocol.Pose)
        {
            ulong id = NetProtocol.PU(NetProtocol.Field(p, 1));
            if (id != backend.LocalId) ApplyRemotePose(id, NetPose.Parse(p, 2));
        }
        else if (type == NetProtocol.Roster)
        {
            Players.Clear();
            string list = NetProtocol.Field(p, 1);
            if (list.Length > 0)
                foreach (var item in list.Split(NetProtocol.Item))
                {
                    var kv = item.Split(NetProtocol.Pair);
                    if (kv.Length == 2) Players[NetProtocol.PU(kv[0])] = kv[1];
                }
            foreach (var id in new List<ulong>(remotes.Keys)) if (!Players.ContainsKey(id)) RemoveRemote(id);
            foreach (var kv in Players) if (kv.Key != backend.LocalId) GetRemote(kv.Key).SetName(kv.Value);
        }
        else if (type == NetProtocol.Emote)
        {
            ulong id = NetProtocol.PU(NetProtocol.Field(p, 1));
            if (id != backend.LocalId && !(Players.ContainsKey(id) && ChatSafety.IsBlocked(Players[id]))) GetRemote(id).PlayEmote(NetProtocol.PI(NetProtocol.Field(p, 2)));
        }
        else if (type == NetProtocol.Chat)
        {
            ulong id = NetProtocol.PU(NetProtocol.Field(p, 1));
            string text = NetProtocol.Clean(NetProtocol.Field(p, 2), 80);
            if (id == backend.LocalId || text.Length == 0) return;
            string shown = AddChat(Players.ContainsKey(id) ? Players[id] : "?", text);
            if (shown != null) GetRemote(id).Say(shown);
        }
        else if (type == NetProtocol.Echo) ApplyEcho(NetProtocol.Field(p, 1));
        else if (type == NetProtocol.Optimize) ApplyOptimize(NetProtocol.Field(p, 1), NetProtocol.Field(p, 2));
        else if (type == NetProtocol.Alert)
        {
            var args = new List<object>();
            for (int i = 2; i < p.Length; i++) args.Add(Mp.Name(p[i]));
            Say(Mp.Tr(NetProtocol.Field(p, 1), args.ToArray()));
        }
        else if (type == NetProtocol.GiftIn)
        {
            string good = NetProtocol.Field(p, 2);
            float amount = NetProtocol.PF(NetProtocol.Field(p, 3));
            if (amount <= 0f) return;
            PendingGifts[good] = (PendingGifts.ContainsKey(good) ? PendingGifts[good] : 0f) + amount;
            var g = EconomyManager.Instance != null ? EconomyManager.Instance.Good(good) : null;
            Say(Mp.Tr("{0} gave you {1} {2} to take home", Mp.Name(NetProtocol.Clean(NetProtocol.Field(p, 1), 20)), NetProtocol.F(amount), g != null ? EconomyManager.GoodName(g) : good));
        }
    }

    // ------------------------------------------------------------ host: applying client requests

    /// <summary>Host: tell clients that an object was used (well started, bed harvested) and send the new state soon.</summary>
    public void HostEcho(string objectId)
    {
        if (!NetSession.IsHost || string.IsNullOrEmpty(objectId)) return;
        backend.SendToClients(NetProtocol.Make(NetProtocol.Echo, objectId), true);
        syncDirty = true;
    }

    private void HostInteract(ulong sender, string objectId)
    {
        var c = NetIds.Find(objectId);
        var target = c as IInteractable;
        if (target == null) return;
        if (!NetSession.ForwardsToHost(target) || !target.CanInteract)
        {
            if (c is Cropbed) Reply(sender, "Not ready yet on the host's farm");
            return;
        }
        if (NetSession.Echoes(target)) HostEcho(objectId);
        target.Interact();
        syncDirty = true;
    }

    private void HostBuy(ulong sender, string who, string itemId)
    {
        var eco = EconomyManager.Instance;
        if (eco == null) return;
        int before = eco.Money;
        if (eco.TryBuy(itemId))
        {
            Say(Mp.Tr("{0} spent {1} SAR at the shop", Mp.Name(who), (before - eco.Money).ToString("N0")));
            syncDirty = true;
            return;
        }
        var a = eco.Animal(itemId);
        var extra = a == null ? ShopCatalog.Find(itemId) : null;
        string block = extra != null && extra.blockReason != null ? extra.blockReason() : null;
        string why = !string.IsNullOrEmpty(block) ? block : eco.PriceOf(itemId) < 0 ? "Fully upgraded" : (a != null && eco.Owned(itemId) >= a.max) ? "The pen is full for this animal" : "Not enough money";
        Reply(sender, why);
    }

    private void HostSell(ulong sender, string who, Dictionary<string, float> wanted)
    {
        var eco = EconomyManager.Instance;
        if (eco == null) return;
        var cargo = new Dictionary<string, float>();
        foreach (var kv in wanted)
        {
            float take = Mathf.Min(kv.Value, eco.Stored(kv.Key));
            if (take > 0.001f && eco.TakeGood(kv.Key, take)) cargo[kv.Key] = take;
        }
        if (cargo.Count == 0) { Reply(sender, "Farm storage is empty - harvest first"); return; }
        int earned = eco.SellAll(cargo, new List<string>());
        Reply(sender, "Sold the load for {0} SAR", earned.ToString("N0"));
        Say(Mp.Tr("{0} sold goods for {1} SAR", Mp.Name(who), earned.ToString("N0")));
        syncDirty = true;
    }

    /// <summary>Host: send the farm state to clients on the next frame (something changed).</summary>
    public void MarkSyncDirty() => syncDirty = true;

    private void HostPlotJob(int index, int job)
    {
        if (FieldManager.Instance == null || index < 0 || index >= FieldManager.Instance.Plots.Count) return;
        var plot = FieldManager.Instance.Plots[index];
        if (plot == null) return;
        bool done = job == (int)Implement.Plow ? plot.TryPlow(false)
                  : job == (int)Implement.Seeder ? plot.TrySeed(false)
                  : job == (int)Implement.Harvester && plot.TryHarvest(false);
        if (done) { GameEvents.RaiseTractorWorked((Implement)job); syncDirty = true; }
    }

    private void HostGift(ulong sender, string msg)
    {
        var p = NetProtocol.Split(msg);
        ulong to = NetProtocol.PU(NetProtocol.Field(p, 1));
        string good = NetProtocol.Field(p, 2);
        float amount = Mathf.Clamp(NetProtocol.PF(NetProtocol.Field(p, 3)), 0f, GiftAmount);
        var eco = EconomyManager.Instance;
        string fromName = Players.ContainsKey(sender) ? Players[sender] : PlayerName;
        if (eco == null || amount <= 0f || to == backend.LocalId || to == sender || !Players.ContainsKey(to) || eco.Good(good) == null) return;
        float last;
        if (lastGift.TryGetValue(sender, out last) && Time.unscaledTime - last < 3f) { Reply(sender, "Wait a moment before the next gift"); return; }
        Vector3 from = sender == backend.LocalId ? LocalPose().position : (remotes.ContainsKey(sender) ? remotes[sender].TargetPosition : Vector3.zero);
        Vector3 toPos = remotes.ContainsKey(to) ? remotes[to].TargetPosition : new Vector3(1e6f, 0, 0);
        if (Vector3.Distance(from, toPos) > GiftRange + 2f) { Reply(sender, "Stand next to the player to give a gift"); return; }
        if (!eco.TakeGood(good, amount)) { Reply(sender, "Not enough in the farm storage"); return; }
        lastGift[sender] = Time.unscaledTime;
        backend.SendToClient(to, NetProtocol.Make(NetProtocol.GiftIn, fromName, good, NetProtocol.F(amount)), true);
        Reply(sender, "Gift sent to {0}", Players[to]);
        syncDirty = true;
    }

    /// <summary>Show a message to a player (the host shows its own messages directly).</summary>
    private void Reply(ulong to, string englishKey, params string[] args)
    {
        if (to == backend.LocalId)
        {
            var shown = new object[args == null ? 0 : args.Length];
            for (int i = 0; i < shown.Length; i++) shown[i] = Mp.Name(args[i]);
            Say(Mp.Tr(englishKey, shown));
            return;
        }
        var f = new List<string> { englishKey };
        if (args != null) f.AddRange(args);
        backend.SendToClient(to, NetProtocol.Make(NetProtocol.Alert, f.ToArray()), true);
    }

    private void SendToOthers(ulong except, string msg, bool reliable)
    {
        foreach (var id in new List<ulong>(backend.ClientIds)) if (id != except) backend.SendToClient(id, msg, reliable);
    }

    private void BroadcastRoster()
    {
        if (!NetSession.IsHost) return;
        var items = new List<string>();
        foreach (var kv in Players) items.Add(NetProtocol.I((long)kv.Key) + NetProtocol.Pair + NetProtocol.Clean(kv.Value, 20));
        backend.SendToClients(NetProtocol.Make(NetProtocol.Roster, string.Join(NetProtocol.Item.ToString(), items.ToArray())), true);
    }

    // ------------------------------------------------------------ shared helpers

    private static bool ApplyOptimize(string greenhouse, string module)
    {
        ModuleType m;
        if (!Enum.TryParse(module, out m)) return false;
        foreach (var gh in FindObjectsByType<GreenhouseManager>(FindObjectsSortMode.None))
            if (gh.name == greenhouse) { gh.OptimizeModule(m); return true; }
        return false;
    }

    private static void ApplyEcho(string objectId)
    {
        var c = NetIds.Find(objectId);
        var bed = c as Cropbed;
        if (bed != null) { bed.ResetGrowth(); return; }
        var well = c as WellController;
        if (well != null && !well.IsRunning) well.StartWell();
    }

    private RemotePlayer GetRemote(ulong id)
    {
        RemotePlayer r;
        if (remotes.TryGetValue(id, out r) && r != null) return r;
        string n;
        r = RemotePlayer.Create(id, Players.TryGetValue(id, out n) ? n : "", remotesRoot);
        remotes[id] = r;
        return r;
    }

    private void RemoveRemote(ulong id)
    {
        Vehicle v;
        if (drivenBy.TryGetValue(id, out v) && v != null) v.SetRemoteDriven(false);
        drivenBy.Remove(id);
        RemotePlayer r;
        if (remotes.TryGetValue(id, out r) && r != null) Destroy(r.gameObject);
        remotes.Remove(id);
    }

    private void ApplyRemotePose(ulong id, NetPose pose)
    {
        GetRemote(id).ApplyPose(pose);
        Vehicle prev;
        drivenBy.TryGetValue(id, out prev);
        Vehicle now = pose.vehicle.Length > 0 ? NetIds.Find<Vehicle>(pose.vehicle) : null;
        if (now != null && now.IsDriving) now = null;   // we are driving it here: our copy wins
        if (prev != null && prev != now) prev.SetRemoteDriven(false);
        if (now != null)
        {
            now.SetRemoteDriven(true);
            now.ApplyRemotePose(pose.position, pose.yaw, pose.speed);
            drivenBy[id] = now;
        }
        else drivenBy.Remove(id);
    }

    private static void Say(string text)
    {
        if (UIManager.Instance != null) UIManager.Instance.ShowAlertMessage(text);
    }
}
