using System.Collections.Generic;
using StarterAssets;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Co-op menu (F8, or the "Co-op" button while the cursor is free): player name (picked from a list, never typed:
/// strangers see it online), LAN Host / Join (IP) / Leave, the online join code, players on the farm (friend, block,
/// report; the host can also remove or ban for the session) and gifts. Online hosting / joining is in Main menu >
/// Multiplayer. Quick chat: Enter opens a line at the bottom-left, Enter sends, Esc cancels.
/// Drawn with IMGUI because it needs text fields (Arabic names/chat can be typed; Tajawal font).
/// While a text field is open, NetSession.Typing is set and walking is paused so keys don't trigger the game.
/// </summary>
[MazraaSystem(71)]
public class MultiplayerPanel : MonoBehaviour
{
    public static MultiplayerPanel Instance;
    public bool IsOpen { get; private set; }
    public bool ChatOpen { get; private set; }

    private string ipField = "", chatField = "";
    private int openedFrame = -10;
    private bool holdingUI;
    private readonly List<MonoBehaviour> pausedMovement = new List<MonoBehaviour>();
    private GUIStyle title, label, small, button, field, box;
    private bool stylesArabic;
    private float scale = 1f;
    private string lanAddress;

    private void Awake() => Instance = this;
    private void OnDestroy() { SetTyping(false); if (Instance == this) Instance = null; }

    private void Update()
    {
        var mp = MultiplayerManager.Instance;
        var kb = Keyboard.current;
        if (mp == null || kb == null) return;
        if (!NetSession.Backend.Available) { if (IsOpen) Close(); return; }   // release builds without Netcode: no co-op UI at all
        bool otherUI = UIManager.Instance != null && UIManager.Instance.IsUIOpened && !holdingUI;

        if (kb.f8Key.wasPressedThisFrame && !ChatOpen)
        {
            if (IsOpen) Close();
            else if (!otherUI || Time.timeScale == 0f) Open();   // not over the shop/station panels (their number keys)
            return;
        }
        var enter = kb.enterKey; var numEnter = kb.numpadEnterKey;   // state-guarded: chat only when no other panel is open
        bool enterPressed = (enter != null && enter.wasPressedThisFrame) || (numEnter != null && numEnter.wasPressedThisFrame);
        if (ChatOpen)
        {
            if (Time.frameCount == openedFrame) return;
            if (kb.escapeKey.wasPressedThisFrame) CloseChat(false);
            else if (enterPressed) CloseChat(true);
            return;
        }
        if (IsOpen)
        {
            if (kb.escapeKey.wasPressedThisFrame && Time.frameCount != openedFrame) Close();
            return;
        }
        if (enterPressed && NetSession.IsOnline && !otherUI && Time.timeScale > 0f && Vehicle.Current == null) OpenChat();
    }

    // ------------------------------------------------------------ open / close

    public void Open()
    {
        var mp = MultiplayerManager.Instance;
        if (IsOpen || mp == null) return;
        IsOpen = true;
        openedFrame = Time.frameCount;
        ipField = mp.LastAddress;
        SetTyping(true);
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        SetTyping(false);
    }

    public void OpenChat()
    {
        if (ChatOpen || IsOpen) return;
        ChatOpen = true;
        chatField = "";
        openedFrame = Time.frameCount;
        SetTyping(true);
    }

    public void CloseChat(bool send)
    {
        if (!ChatOpen) return;
        ChatOpen = false;
        if (send && MultiplayerManager.Instance != null) MultiplayerManager.Instance.SendChat(chatField);
        chatField = "";
        SetTyping(false);
    }

    /// <summary>Text entry on/off: free the cursor, block interactions and hotkeys, pause walking.</summary>
    private void SetTyping(bool on)
    {
        NetSession.Typing = on;
        if (on != holdingUI)
        {
            holdingUI = on;
            if (UIManager.Instance != null) UIManager.Instance.SetExternalPanel(on);
            else { Cursor.lockState = on ? CursorLockMode.None : CursorLockMode.Locked; Cursor.visible = on; }
        }
        if (on)
        {
            var inputs = FindAnyObjectByType<StarterAssetsInputs>();
            if (inputs != null)
                foreach (var mb in inputs.GetComponentsInChildren<MonoBehaviour>(true))
                    if (mb != null && mb.enabled && mb.GetType().Name == "FirstPersonController") { mb.enabled = false; pausedMovement.Add(mb); }
        }
        else
        {
            foreach (var mb in pausedMovement) if (mb != null) mb.enabled = true;
            pausedMovement.Clear();
        }
    }

    // ------------------------------------------------------------ drawing

    private Rect R(float x, float y, float w, float h) => new Rect(x * scale, y * scale, w * scale, h * scale);

    private void MakeStyles()
    {
        scale = Mathf.Max(0.6f, Screen.height / 1080f);
        if (title != null && stylesArabic == Loc.IsArabic) { ApplySizes(); return; }
        stylesArabic = Loc.IsArabic;
        var font = Resources.Load<Font>("Fonts/Tajawal-Bold");
        TextAnchor align = Loc.IsArabic ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
        title = new GUIStyle(GUI.skin.label) { alignment = align };
        title.normal.textColor = new Color(1f, 0.6f, 0.2f);
        label = new GUIStyle(GUI.skin.label) { alignment = align, wordWrap = true };
        label.normal.textColor = Color.white;
        small = new GUIStyle(label);
        small.normal.textColor = new Color(0.8f, 0.78f, 0.74f);
        button = new GUIStyle(GUI.skin.button);
        field = new GUIStyle(GUI.skin.textField) { alignment = TextAnchor.MiddleLeft };
        box = new GUIStyle(GUI.skin.box);
        if (font != null) foreach (var s in new[] { title, label, small, button, field }) s.font = font;
        ApplySizes();
    }

    private void ApplySizes()
    {
        title.fontSize = Mathf.RoundToInt(30 * scale);
        label.fontSize = field.fontSize = button.fontSize = Mathf.RoundToInt(21 * scale);
        small.fontSize = Mathf.RoundToInt(17 * scale);
    }

    private void OnGUI()
    {
        var mp = MultiplayerManager.Instance;
        if (mp == null || !NetSession.Backend.Available) return;
        MakeStyles();
        if (IsOpen) DrawPanel(mp);
        else if (Cursor.visible && !ChatOpen && GUI.Button(R(Screen.width / scale - 190f, 12f, 175f, 44f), Mp.Tr("Co-op (F8)"), button)) Open();
        DrawChat(mp);
    }

    private void DrawPanel(MultiplayerManager mp)
    {
        var backend = NetSession.Backend;
        float w = 620f, x = (Screen.width / scale - w) * 0.5f, y = 110f, line = 40f;
        float h;

        float cy = y;
        // header
        cy += 46f;
        cy += line;                                    // name
        if (!backend.Available) cy += line * 2f;
        else if (!NetSession.IsOnline && !backend.Connecting) cy += line * 3.4f + line * 1.2f;
        else if (backend.Connecting) cy += line * 1.4f;
        else
        {
            cy += line * (1.3f + mp.Players.Count) + line * 0.4f + line * 1.2f;
            var target = mp.NearestGiftTarget();
            cy += target == null ? line : line * (1f + ((GiftGoods().Count + 1) / 2));
            if (mp.PendingGifts.Count > 0) cy += line;
            cy += line * 1.3f;
        }
        cy += line * 1.6f;                             // status + keys
        h = cy - y;
        GUI.Box(R(x - 12f, y - 12f, w + 24f, h + 24f), "", box);
        GUI.Box(R(x - 12f, y - 12f, w + 24f, h + 24f), "", box);   // twice: darker backdrop

        cy = y;
        GUI.Label(R(x, cy, w, 40f), Mp.Tr("Co-op Farm") + "  (F8)", title); cy += 46f;
        GUI.Label(R(x, cy, 190f, 34f), Mp.Tr("Your name"), label);
        bool canRename = !NetSession.IsOnline && !backend.Connecting;   // the name others already see stays while playing
        GUI.enabled = canRename;
        if (GUI.Button(R(x + 200f, cy, 44f, 34f), "<", button)) { PlayerNames.Index = PlayerNames.Index - 1; mp.UseGeneratedName(); }
        GUI.Label(R(x + 252f, cy, w - 252f - 52f, 34f), Mp.UserText(PlayerNames.Display(mp.PlayerName)), label);
        if (GUI.Button(R(x + w - 44f, cy, 44f, 34f), ">", button)) { PlayerNames.Index = PlayerNames.Index + 1; mp.UseGeneratedName(); }
        GUI.enabled = true;
        cy += line;

        if (!backend.Available)
        {
            GUI.Label(R(x, cy, w, line * 2f), Mp.Tr("Multiplayer needs the Netcode package (see Documentation.txt)"), label); cy += line * 2f;
        }
        else if (!NetSession.IsOnline && !backend.Connecting)
        {
            GUI.Label(R(x, cy, 190f, 34f), Mp.Tr("Host IP address"), label);
            ipField = GUI.TextField(R(x + 200f, cy, w - 200f, 34f), ipField ?? "", 40, field); cy += line * 1.2f;
            if (GUI.Button(R(x, cy, w * 0.5f - 6f, 40f), Mp.Tr("Host a farm"), button)) { if (mp.Host()) lanAddress = LanAddress(); }
            if (GUI.Button(R(x + w * 0.5f + 6f, cy, w * 0.5f - 6f, 40f), Mp.Tr("Join"), button)) mp.Join(ipField);
            cy += line * 1.2f;
            GUI.Label(R(x, cy, w, 34f), Mp.Tr("Port 7777 (UDP) - allow the game in your firewall") + "   " + Mp.Tr("Online: Main menu > Multiplayer"), small); cy += line;
            cy = DrawSafety(x, cy, w, line);
        }
        else if (backend.Connecting)
        {
            GUI.Label(R(x, cy, w * 0.6f, 40f), Mp.Tr("Connecting..."), label);
            if (GUI.Button(R(x + w * 0.65f, cy, w * 0.35f, 40f), Mp.Tr("Cancel"), button)) mp.Leave();
            cy += line * 1.4f;
        }
        else
        {
            string role = NetSession.IsHost && mp.IsOnlineSession ? Mp.Tr("Join code") + ":  <size=140%><b>" + JoinCodes.Pretty(mp.JoinCode) + "</b></size>"
                        : NetSession.IsHost ? Mp.Tr("You are hosting - friends join with your IP address") + (string.IsNullOrEmpty(lanAddress) ? "" : "  " + lanAddress)
                                            : Mp.Tr("You are visiting a friend's farm");
            GUI.Label(R(x, cy, w, 48f), role, label); cy += line * 1.3f;
            foreach (var kv in new List<KeyValuePair<ulong, string>>(mp.Players))
            {
                bool me = kv.Key == backend.LocalId;
                string who = "- " + Mp.UserText(PlayerNames.Display(kv.Value)) + (kv.Key == 0UL ? "  (" + Mp.Tr("host") + ")" : "") + (me ? "  (" + Mp.Tr("you") + ")" : "")
                           + (ChatSafety.IsFriend(kv.Value) ? "  *" : "");
                float bw = 96f;
                int nb = NetSession.IsHost ? 5 : 3;
                GUI.Label(R(x + 16f, cy, me ? w - 16f : w - 16f - bw * nb - 12f, 34f), who, label);
                if (!me)
                {
                    float bx = x + w - bw * nb - 8f;
                    if (NetSession.IsHost)
                    {
                        if (GUI.Button(R(bx, cy, bw - 4f, 32f), Mp.Tr("Remove"), small)) mp.Kick(kv.Key);
                        if (GUI.Button(R(bx + bw, cy, bw - 4f, 32f), Mp.Tr("Ban"), small)) mp.Ban(kv.Key);
                        bx += bw * 2f;
                    }
                    bool friend = ChatSafety.IsFriend(kv.Value);
                    if (GUI.Button(R(bx, cy, bw - 4f, 32f), Mp.Tr(friend ? "Unfriend" : "Friend"), small)) ChatSafety.SetFriend(kv.Value, !friend);
                    bool blocked = ChatSafety.IsBlocked(kv.Value);
                    if (GUI.Button(R(bx + bw, cy, bw - 4f, 32f), Mp.Tr(blocked ? "Unblock" : "Block"), small))
                    { if (blocked) ChatSafety.SetBlocked(kv.Value, false); else mp.Block(kv.Key, kv.Value); }
                    if (GUI.Button(R(bx + bw * 2f, cy, bw - 4f, 32f), Mp.Tr("Report"), small))
                        Application.OpenURL(mp.Report(kv.Key, kv.Value, "Reported from the co-op menu"));
                }
                cy += line;
            }
            cy = DrawSafety(x, cy, w, line);
            cy += line * 0.4f;
            var target = mp.NearestGiftTarget();
            if (target == null) { GUI.Label(R(x, cy, w, 34f), Mp.Tr("Stand next to a visiting player to give a gift"), small); cy += line; }
            else
            {
                GUI.Label(R(x, cy, w, 34f), Mp.Tr("Gift for {0} (from the farm storage):", Mp.Name(target.PlayerName)), label); cy += line;
                var goods = GiftGoods();
                for (int i = 0; i < goods.Count; i++)
                {
                    float bx = x + (i % 2) * (w * 0.5f + 6f), by = cy + (i / 2) * line;
                    if (GUI.Button(R(bx, by, w * 0.5f - 6f, 36f), MultiplayerManager.GiftAmount + " " + Loc.Display(EconomyManager.GoodName(goods[i])), button))
                        mp.SendGift(target.Id, goods[i].id);
                }
                cy += ((goods.Count + 1) / 2) * line;
            }
            if (mp.PendingGifts.Count > 0)
            {
                var parts = new List<string>();
                foreach (var kv in mp.PendingGifts)
                {
                    var g = EconomyManager.Instance != null ? EconomyManager.Instance.Good(kv.Key) : null;
                    parts.Add(kv.Value.ToString("F0") + " " + (g != null ? EconomyManager.GoodName(g) : kv.Key));
                }
                GUI.Label(R(x, cy, w, 34f), Loc.Display(Mp.Raw("Gifts to take home") + ": " + string.Join(", ", parts.ToArray())), small); cy += line;
            }
            if (GUI.Button(R(x, cy, w, 40f), Mp.Tr(NetSession.IsHost ? "Stop hosting" : "Leave the farm"), button)) mp.Leave();
            cy += line * 1.3f;
        }
        if (!string.IsNullOrEmpty(mp.Status)) GUI.Label(R(x, cy, w, 34f), Mp.Tr(mp.Status), small);
        cy += line * 0.8f;
        GUI.Label(R(x, cy, w, 34f), Mp.Tr("[1-4] emotes   [Enter] chat   [F8] close"), small);
    }

    /// <summary>Chat and join settings row: Chat (Everyone / Friends only / Off), Who can join (Anyone / Friends only).</summary>
    private float DrawSafety(float x, float cy, float w, float line)
    {
        if (GUI.Button(R(x, cy, w * 0.5f - 6f, 36f), Mp.Tr("Chat") + ": " + Mp.Tr(ChatSafety.ChatModeLabel(ChatSafety.Chat)), button)) ChatSafety.NextChatMode();
        if (GUI.Button(R(x + w * 0.5f + 6f, cy, w * 0.5f - 6f, 36f), Mp.Tr("Who can join") + ": " + Mp.Tr(MultiplayerManager.VisibilityLabel(MultiplayerManager.Visibility)), button))
            MultiplayerManager.Visibility = (OnlineVisibility)(((int)MultiplayerManager.Visibility + 1) % 3);
        return cy + line * 1.2f;
    }

    /// <summary>Goods with enough in the farm storage for a gift.</summary>
    private static List<GoodDef> GiftGoods()
    {
        var list = new List<GoodDef>();
        var eco = EconomyManager.Instance;
        if (eco == null) return list;
        foreach (var g in eco.Data.goods)
            if (eco.Stored(g.id) >= MultiplayerManager.GiftAmount && list.Count < 6) list.Add(g);
        return list;
    }

    private void DrawChat(MultiplayerManager mp)
    {
        if (!NetSession.IsOnline && !ChatOpen) return;
        float x = 24f, bottom = Screen.height / scale - 150f, w = 640f, line = 30f;
        int shown = 0;
        for (int i = mp.ChatLog.Count - 1; i >= 0 && shown < 6; i--)
        {
            var c = mp.ChatLog[i];
            if (!ChatOpen && Time.unscaledTime - c.time > 15f) break;
            shown++;
            GUI.Label(R(x, bottom - shown * line, w, line), Mp.UserText(c.text), label);
        }
        if (!ChatOpen) return;
        GUI.Label(R(x, bottom + 4f, 90f, 36f), Mp.Tr("Chat"), label);
        GUI.SetNextControlName("mazraa.chat");
        chatField = GUI.TextField(R(x + 96f, bottom + 4f, w - 96f, 36f), chatField ?? "", 80, field);
        GUI.FocusControl("mazraa.chat");
    }

    /// <summary>This PC's LAN address to tell friends (first IPv4 that isn't loopback).</summary>
    public static string LanAddress()
    {
        try
        {
            foreach (var a in System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName()).AddressList)
                if (a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(a)) return a.ToString();
        }
        catch (System.Exception) { }
        return "";
    }
}
