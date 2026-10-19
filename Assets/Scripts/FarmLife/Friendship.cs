using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable] class FriendEntry { public string id; public float pts; public int talkDay, giftDay; public List<int> events = new List<int>(); public List<string> known = new List<string>(); }
[Serializable] class FriendSave { public List<FriendEntry> friends = new List<FriendEntry>(); }

/// <summary>
/// Villager friendship (like Stardew Valley / Harvest Moon) for six villagers: the three cozy villagers (Abu Saleh,
/// Abu Fahad, Abu Nasser; hearts kept by Villagers.cs) plus Dr. Huda the vet, Grandma Moudhi the storyteller and
/// Faisal the young farmer next door. E on a villager opens a small menu: talk (once a day) or give one item from the
/// farm store (once a day). Loved / liked gifts raise friendship more; reactions are remembered. Birthdays are on the
/// calendar (gifts count triple). New villagers have heart events with rewards at 2 and 4 hearts and a small daily
/// gift at 5 hearts. Data in farmlife.json "villagers". Saved as mazraa.fl.friends.
/// </summary>
[MazraaSystem(77)]
public class Friendship : MonoBehaviour
{
    public const string SaveKey = "mazraa.fl.friends";
    public const float TalkPoints = 30f, LovedPoints = 80f, LikedPoints = 45f, NeutralPoints = 20f, DislikedPoints = 0f;
    public static Friendship Instance { get; private set; }

    private FriendSave state = new FriendSave();
    public readonly Dictionary<string, GameObject> Npcs = new Dictionary<string, GameObject>();

    private Canvas canvas;
    private MenuList list;
    private string openId;
    private int openedFrame;

    public bool PanelOpen => openId != null;
    public string PanelId => openId;
    public MenuList List => list;

    private void Awake()
    {
        Instance = this;
        state = FarmLife.Load<FriendSave>(SaveKey);
        if (state.friends == null) state.friends = new List<FriendEntry>();
        HudLines.Status.Remove(HudLine);
        HudLines.Status.Add(HudLine);
    }

    private void Start()
    {
        canvas = MenuKit.Canvas("Friend Menu UI", transform, 63);
        MenuKit.Backdrop(canvas.transform, 0.35f);
        list = new MenuList(canvas.transform, 780);
        canvas.gameObject.SetActive(false);
        GameEvents.NewGameStarted += OnNewGame;
        WorldClock.DayStarted += OnDay;
        Loc.OnLanguageChanged += OnLanguage;
        Invoke(nameof(Spawn), 2.1f);
    }

    private void OnDestroy()
    {
        GameEvents.NewGameStarted -= OnNewGame;
        WorldClock.DayStarted -= OnDay;
        Loc.OnLanguageChanged -= OnLanguage;
        HudLines.Status.Remove(HudLine);
        if (Instance == this) Instance = null;
    }

    private void OnNewGame() { Close(); state = new FriendSave(); Save(); }
    private void OnLanguage() { if (PanelOpen) list.Refresh(); }
    private void Save() => FarmLife.Store(SaveKey, state);

    private void OnDay(int day)
    {
        foreach (var v in FarmLife.Data.villagers)
            if (IsBirthday(v, day)) CozyFx.Alert(Loc.Display(Loc.Raw("Today is the birthday of") + " " + ShortName(v) + " - " + Loc.Raw("a gift counts triple!")));
    }

    // ---------------- data ----------------

    public static FLVillager Def(string id) => Array.Find(FarmLife.Data.villagers, v => v.id == id);
    public static string NameOf(FLVillager v) => v == null ? "" : FarmLife.Pick(v.en, v.ar);
    public static string ShortName(FLVillager v) => v == null ? "" : !string.IsNullOrEmpty(v.@short) ? FarmLife.Pick(v.@short, v.shortAr) : NameOf(v);
    private static float PerHeart => Mathf.Max(1, FarmLife.Data.pointsPerHeart);

    private FriendEntry Entry(string id)
    {
        var e = state.friends.Find(x => x.id == id);
        if (e == null) { e = new FriendEntry { id = id }; state.friends.Add(e); }
        if (e.events == null) e.events = new List<int>();
        if (e.known == null) e.known = new List<string>();
        return e;
    }

    public int Hearts(string id)
    {
        var v = Def(id);
        if (v == null) return 0;
        if (v.old) return Villagers.Hearts(id);
        return Mathf.Clamp(Mathf.FloorToInt(Entry(id).pts / PerHeart + 1e-4f), 0, Villagers.MaxHearts);
    }

    /// <summary>Progress towards the next heart (0..1).</summary>
    public float HeartProgress(string id)
    {
        var v = Def(id);
        if (v == null || Hearts(id) >= Villagers.MaxHearts) return 1f;
        float p = Entry(id).pts;
        return v.old ? Mathf.Clamp01(p / PerHeart) : Mathf.Clamp01((p - Hearts(id) * PerHeart) / PerHeart);
    }

    /// <summary>Total friendship points (hearts x points per heart + progress).</summary>
    public float Points(string id)
    {
        var v = Def(id);
        if (v == null) return 0f;
        return v.old ? Villagers.Hearts(id) * PerHeart + Entry(id).pts : Entry(id).pts;
    }

    /// <summary>
    /// The player bumped into this villager (vehicle or running into them): friendship drops a little, a heart can be lost
    /// but never below zero. Returns the points actually lost.
    /// </summary>
    public float Bumped(string id, float pts)
    {
        var v = Def(id);
        if (v == null || pts <= 0f) return 0f;
        float before = Points(id);
        var e = Entry(id);
        if (v.old)
        {
            int h = Villagers.Hearts(id);
            e.pts -= pts;
            while (e.pts < 0f && h > 0) { h--; e.pts += PerHeart; }
            if (e.pts < 0f) e.pts = 0f;
            SaveData.SetInt("mazraa.cozy.v." + id + ".hearts", h);
        }
        else e.pts = Mathf.Max(0f, e.pts - pts);
        Save();
        return before - Points(id);
    }

    public bool TalkedToday(string id) { var v = Def(id); return v != null && v.old ? Villagers.TalkedToday(id) : Entry(id).talkDay == CozyFx.Today; }
    public bool GiftedToday(string id) => Entry(id).giftDay == CozyFx.Today;

    public static bool IsBirthday(FLVillager v, int day)
    {
        if (v == null || v.bdDay <= 0) return false;
        return FarmLife.SeasonOf(day) == FarmLife.ParseSeason(v.bdSeason) && FarmLife.DayOfSeason(day) == Mathf.Min(v.bdDay, FarmLife.DaysPerSeason);
    }

    /// <summary>Next day (from 'after', inclusive) that is this villager's birthday.</summary>
    public static int NextBirthday(string id, int after)
    {
        var v = Def(id);
        for (int d = after; d < after + FarmLife.DaysPerSeason * 4 + 1; d++) if (IsBirthday(v, d)) return d;
        return -1;
    }

    public enum Reaction { Loved, Liked, Neutral, Disliked }

    public static Reaction ReactionTo(FLVillager v, string good)
    {
        if (v == null) return Reaction.Neutral;
        if (v.loves != null && Array.IndexOf(v.loves, good) >= 0) return Reaction.Loved;
        if (v.likes != null && Array.IndexOf(v.likes, good) >= 0) return Reaction.Liked;
        if (v.dislikes != null && Array.IndexOf(v.dislikes, good) >= 0) return Reaction.Disliked;
        return Reaction.Neutral;
    }

    private static string ReactionWord(Reaction r)
    {
        switch (r)
        {
            case Reaction.Loved: return Loc.Raw("loves it");
            case Reaction.Liked: return Loc.Raw("likes it");
            case Reaction.Disliked: return Loc.Raw("doesn't like it");
            default: return Loc.Raw("thanks you");
        }
    }

    /// <summary>Gift reaction you have already seen for this good (or null).</summary>
    public string KnownReaction(string id, string good)
    {
        foreach (var k in Entry(id).known) if (k.StartsWith(good + ":")) return ReactionWord((Reaction)int.Parse(k.Substring(good.Length + 1)));
        return null;
    }

    // ---------------- points ----------------

    private void AddPoints(FLVillager v, float pts)
    {
        if (pts <= 0f) return;
        var e = Entry(v.id);
        int before = Hearts(v.id);
        if (v.old)
        {
            e.pts += pts;
            int h = Villagers.Hearts(v.id);
            while (e.pts >= PerHeart && h < Villagers.MaxHearts)
            {
                e.pts -= PerHeart; h++;
                SaveData.SetInt("mazraa.cozy.v." + v.id + ".hearts", h);
            }
            if (h >= Villagers.MaxHearts) e.pts = 0f;
        }
        else e.pts = Mathf.Min(e.pts + pts, PerHeart * Villagers.MaxHearts);
        int after = Hearts(v.id);
        if (after > before)
        {
            if (FarmLevel.Instance != null) FarmLevel.Instance.AddXp(FarmLevel.Instance.Rate("heartUp") * (after - before));
            if (Npcs.TryGetValue(v.id, out var go) && go != null) CozyFx.Hearts(go.transform.position + Vector3.up * 2f);
            CozyFx.Alert(Loc.Display(ShortName(v) + ": " + FarmLife.Stars(after, Villagers.MaxHearts)));
            CheckEvents(v);
        }
        Save();
    }

    private void CheckEvents(FLVillager v)
    {
        if (v.events == null) return;
        var e = Entry(v.id);
        foreach (var ev in v.events)
        {
            if (ev == null || Hearts(v.id) < ev.hearts || e.events.Contains(ev.hearts)) continue;
            e.events.Add(ev.hearts);
            string reward = FarmLife.Grant(ev, "heartEvent");
            FarmLife.Card(ShortName(v) + "  " + FarmLife.Stars(ev.hearts, Villagers.MaxHearts), FarmLife.Pick(ev.en, ev.ar), reward != "" ? Loc.Raw("Reward") + ": " + reward : null);
            GameEvents.RaiseCustom("heartEvent", 1f);
        }
        Save();
    }

    public bool EventSeen(string id, int hearts) => Entry(id).events.Contains(hearts);

    // ---------------- talk + gifts ----------------

    /// <summary>Talk to a villager (once a day raises friendship). Returns the line said (unshaped).</summary>
    public string Talk(string id)
    {
        var v = Def(id);
        if (v == null) return "";
        if (v.old) return Villagers.Instance != null ? Villagers.Instance.Talk(id) : "";
        var e = Entry(id);
        if (Npcs.TryGetValue(id, out var go) && go != null) { var a = go.GetComponent<Actor>(); if (a != null) a.React("emote-yes", 1.2f); }
        string line;
        if (e.talkDay == CozyFx.Today)
        {
            line = ShortName(v) + ": " + Loc.Raw("Good to see you again! Come by tomorrow for a proper chat.");
            CozyFx.Alert(Loc.Display(line));
            return line;
        }
        e.talkDay = CozyFx.Today;
        int h = Hearts(id);
        line = ShortName(v) + ": " + (v.talk != null && v.talk.Length > 0 ? v.talk[Mathf.Clamp(h, 0, v.talk.Length - 1)].T : "");
        if (IsBirthday(v, CozyFx.Today)) line += "  " + Loc.Raw("(It's my birthday today!)");
        AddPoints(v, TalkPoints);
        if (Hearts(id) >= Villagers.MaxHearts && v.dailyGift != null)
        {
            string g = FarmLife.Grant(v.dailyGift, "friendGift");
            if (g != "") line += "  " + Loc.Raw("Gift") + ": " + g;
        }
        Save();
        CozyFx.Alert(Loc.Display(line));
        GameEvents.RaiseCustom("talkVillager", 1f);
        return line;
    }

    /// <summary>Give one unit of a good from the farm store (once a day per villager). Returns the reaction or null.</summary>
    public Reaction? Gift(string id, string good)
    {
        var v = Def(id);
        var eco = EconomyManager.Instance;
        if (v == null || eco == null || eco.Good(good) == null) return null;
        var e = Entry(id);
        if (e.giftDay == CozyFx.Today) { CozyFx.Alert(Loc.Display(ShortName(v) + ": " + Loc.Raw("You already gave me something today, thank you!"))); return null; }
        float unit = Mathf.Min(1f, eco.Stored(good));
        if (unit < 0.99f || !eco.TakeGood(good, 1f)) { CozyFx.Alert(Loc.T("Not enough in the farm store yet")); return null; }
        var r = ReactionTo(v, good);
        float pts = r == Reaction.Loved ? LovedPoints : r == Reaction.Liked ? LikedPoints : r == Reaction.Disliked ? DislikedPoints : NeutralPoints;
        bool birthday = IsBirthday(v, CozyFx.Today);
        if (birthday) pts *= 3f;
        e.giftDay = CozyFx.Today;
        e.known.RemoveAll(k => k.StartsWith(good + ":"));
        e.known.Add(good + ":" + (int)r);
        Save();
        AddPoints(v, pts);
        if (FarmLevel.Instance != null) FarmLevel.Instance.AddXp(FarmLevel.Instance.Rate("gift"));
        CozyFx.Alert(Loc.Display(ShortName(v) + " " + ReactionWord(r) + ": " + FarmLife.GoodName(good) + (birthday ? "  " + Loc.Raw("(birthday gift x3)") : "")));
        GameEvents.RaiseCustom("giftVillager", 1f);
        return r;
    }

    private static string HudLine()
    {
        foreach (var v in FarmLife.Data.villagers)
            if (IsBirthday(v, CozyFx.Today)) return Loc.Display(Loc.Raw("Birthday today") + ": " + ShortName(v));
        return null;
    }

    // ---------------- villagers in the world ----------------

    private Vector3 SpotPos(string spot)
    {
        Vector3 start = CozyFx.FarmStart;
        if (spot == "pen" && AnimalPen.Instance != null) return AnimalPen.Instance.transform.position + new Vector3(-3f, 0, -8f);
        if (spot == "fields" && FarmFields.Instance != null && FarmFields.Instance.Fields.Count > 1) return FarmFields.Instance.Fields[1].center + FarmFields.Instance.GridRotation * new Vector3(FarmFields.FarmFieldWidth * 0.5f + 3f, 0, -4f);
        return start + new Vector3(-4f, 0, 5f);
    }

    private void Spawn()
    {
        foreach (var v in FarmLife.Data.villagers)
        {
            if (v.old || Npcs.ContainsKey(v.id)) continue;
            Vector3 pos = Build.OnGround(SpotPos(v.spot));
            GameObject go;
            var actor = Actor.Spawn(v.model, 1.55f, true, ActorMode.Wander, pos);
            if (actor != null) { go = actor.gameObject; actor.radius = 1.5f; actor.speed = 0.5f; }
            else { go = new GameObject("Villager " + v.id); go.transform.position = pos; CozyFx.PrimitivePerson(go.transform, Appearance.ThobeColors[0]); }
            go.name = "Villager " + v.id;
            AppearancePanel.DressNpc(go, v.female, v.id.Length);
            var def = v;
            CozyFx.DataLabel(go.transform, new Vector3(0, 2.15f, 0), () => ShortName(def), 2f, new Color(1f, 0.9f, 0.7f));
            CozyFx.MakeInteractable(go, new Vector3(0, 0.9f, 0), new Vector3(0.9f, 1.8f, 0.9f), () => Prompt(def.id), () => Open(def.id));
            Npcs[v.id] = go;
        }
        HookOldVillagers();
    }

    /// <summary>The cozy villagers' E now opens the same talk / gift menu (talking still goes through Villagers.Talk).</summary>
    private void HookOldVillagers()
    {
        if (Villagers.Instance == null) return;
        foreach (var kv in Villagers.Instance.Npcs)
        {
            if (kv.Value == null || Def(kv.Key) == null) continue;
            var prop = kv.Value.GetComponent<CozyProp>();
            string id = kv.Key;
            if (prop != null) prop.use = () => Open(id);
            Npcs[id] = kv.Value;
        }
    }

    private string Prompt(string id) => Loc.Display(Loc.Raw("[E] Talk to") + " " + ShortName(Def(id)) + "  " + FarmLife.Stars(Hearts(id), Villagers.MaxHearts));

    // ---------------- talk / gift menu ----------------

    public void Open(string id)
    {
        var v = Def(id);
        if (v == null || canvas == null) return;
        if (!PanelOpen)
        {
            canvas.gameObject.SetActive(true);
            if (UIManager.Instance != null) UIManager.Instance.SetExternalPanel(true);
        }
        openId = id;
        openedFrame = Time.frameCount;
        list.Show(() => MenuKit.Title(Loc.Display(NameOf(v) + "  " + FarmLife.Stars(Hearts(id), Villagers.MaxHearts))), Rows(v), () => Glyphs.Apply(Loc.T("[Enter] Choose   [Esc] Back")));
    }

    private List<MenuRow> Rows(FLVillager v)
    {
        string id = v.id;
        var rows = new List<MenuRow>
        {
            new MenuRow { label = () => TalkedToday(id) ? Loc.T("Talk (already chatted today)") : Loc.T("Talk"), confirm = () => { Talk(id); Refresh(); } },
        };
        var eco = EconomyManager.Instance;
        if (eco != null)
        {
            var goods = new List<GoodDef>();
            foreach (var g in eco.Data.goods) if (eco.Stored(g.id) >= 1f) goods.Add(g);
            // known favourites first
            goods.Sort((a, b) => Rank(v, b.id).CompareTo(Rank(v, a.id)));
            for (int i = 0; i < goods.Count && i < 8; i++)
            {
                var g = goods[i];
                rows.Add(new MenuRow
                {
                    label = () => Loc.Display(Loc.Raw("Give") + ": " + EconomyManager.GoodName(g) + " (1 " + EconomyManager.Unit(g) + ")" + (KnownReaction(id, g.id) is string k ? "  - " + k : "")),
                    value = () => Mathf.FloorToInt(eco.Stored(g.id)).ToString(),
                    confirm = () => { Gift(id, g.id); Open(id); },
                    enabled = () => !GiftedToday(id) && eco.Stored(g.id) >= 1f,
                });
            }
        }
        rows.Add(MenuRow.Button(() => Loc.T("Close"), Close));
        return rows;
    }

    private int Rank(FLVillager v, string good)
    {
        string k = KnownReaction(v.id, good);
        if (k == null) return 1;
        var r = ReactionTo(v, good);
        return r == Reaction.Loved ? 4 : r == Reaction.Liked ? 3 : r == Reaction.Neutral ? 2 : 0;
    }

    private void Refresh() { if (PanelOpen) list.Refresh(); }

    public void Close()
    {
        if (!PanelOpen) return;
        openId = null;
        if (canvas != null) canvas.gameObject.SetActive(false);
        if (UIManager.Instance != null) UIManager.Instance.SetExternalPanel(false);
    }

    private void Update()
    {
        if (!PanelOpen || Time.frameCount == openedFrame) return;
        if (MenuNav.BackOrTab()) { Close(); return; }
        list.HandleInput();
    }

    /// <summary>Journal lines: hearts, birthday and known favourites per villager.</summary>
    public List<string> Lines()
    {
        var lines = new List<string>();
        foreach (var v in FarmLife.Data.villagers)
        {
            var favs = new List<string>();
            foreach (var k in Entry(v.id).known)
            {
                int c = k.LastIndexOf(':');
                if (c > 0 && k.Substring(c + 1) == ((int)Reaction.Loved).ToString()) favs.Add(FarmLife.GoodName(k.Substring(0, c)));
            }
            string bd = Loc.Raw(FarmLife.ParseSeason(v.bdSeason).ToString()) + " " + Mathf.Min(v.bdDay, FarmLife.DaysPerSeason);
            lines.Add(ShortName(v) + "  " + FarmLife.Stars(Hearts(v.id), Villagers.MaxHearts) + "   " + Loc.Raw("Birthday") + ": " + bd
                + (favs.Count > 0 ? "   " + Loc.Raw("Loves") + ": " + string.Join(", ", favs.ToArray()) : ""));
        }
        return lines;
    }
}
