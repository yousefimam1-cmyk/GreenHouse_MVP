using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable] class FestivalSave { public List<string> entered = new List<string>(); public List<string> goals = new List<string>(); public List<string> orderCounts = new List<string>(); public int ribbons, wins; }

/// <summary>
/// Seasonal festivals with mini-competitions (like Stardew Valley), on top of the calendar events in SeasonalEvents:
/// Ramadan nights market (iftar orders at the roadside stand; 3 served = a crescent lantern), Eid animal show,
/// spring flower fair (prettiest farm, judged by the beauty rating), watermelon day (biggest watermelon) and the
/// date harvest festival (best dates tasting). Each festival with a contest puts up a festival stand at the market;
/// entering (once per festival per year) scores you against three villagers and prizes go to the top three
/// (unique decor for the winner). Data in farmlife.json "festivals". Saved as mazraa.fl.festivals.
/// </summary>
[MazraaSystem(78)]
public class Festivals : MonoBehaviour
{
    public const string SaveKey = "mazraa.fl.festivals";
    public static Festivals Instance { get; private set; }

    private FestivalSave state = new FestivalSave();
    private int evaluatedDay = -1;

    public FLFestival Today { get; private set; }
    public GameObject Decor { get; private set; }
    public int Ribbons => state.ribbons;
    public int Wins => state.wins;
    public int LastPlace { get; private set; }
    private FLFestival lastFestival;
    private float lastPlayer;
    private readonly List<float> lastRivals = new List<float>();
    private FLReward lastPrize;

    /// <summary>Result of the last contest (translated now, unshaped), for the journal and tests.</summary>
    public string LastResult
    {
        get
        {
            if (lastFestival == null) return "";
            var c = lastFestival.competition;
            var lines = new List<string> { Loc.Raw("You") + ": " + Mathf.RoundToInt(lastPlayer) + (c.kind == "good" && c.good == "watermelon" ? "  (" + (6f + lastPlayer * 0.12f).ToString("0.0") + " kg)" : "") };
            var rivals = FarmLife.Data.rivals;
            for (int i = 0; i < lastRivals.Count && i < rivals.Length; i++) lines.Add(rivals[i].T + ": " + Mathf.RoundToInt(lastRivals[i]));
            string prize = lastPrize != null ? FarmLife.Describe(lastPrize) : "";
            return PlaceTitle(lastFestival, LastPlace) + "\n" + string.Join("\n", lines.ToArray()) + (prize != "" ? "\n" + Loc.Raw("Prize") + ": " + prize : "");
        }
    }

    private static string PlaceTitle(FLFestival f, int place)
        => ContestName(f) + ": " + (place == 1 ? Loc.Raw("1st place!") : place == 2 ? Loc.Raw("2nd place") : place == 3 ? Loc.Raw("3rd place") : Loc.Raw("Thanks for taking part"));

    private void Awake()
    {
        Instance = this;
        state = FarmLife.Load<FestivalSave>(SaveKey);
        if (state.entered == null) state.entered = new List<string>();
        if (state.goals == null) state.goals = new List<string>();
        if (state.orderCounts == null) state.orderCounts = new List<string>();
        HudLines.Status.Remove(HudLine);
        HudLines.Status.Add(HudLine);
    }

    private void Start()
    {
        WorldClock.DayStarted += OnDay;
        GameEvents.NewGameStarted += OnNewGame;
        Invoke(nameof(Evaluate), 1.9f);
    }

    private void OnDestroy()
    {
        WorldClock.DayStarted -= OnDay;
        GameEvents.NewGameStarted -= OnNewGame;
        HudLines.Status.Remove(HudLine);
        if (Instance == this) Instance = null;
    }

    private void OnDay(int day) => Evaluate();
    private void OnNewGame() { state = new FestivalSave(); Save(); evaluatedDay = -1; Evaluate(); }
    private void Save() => FarmLife.Store(SaveKey, state);

    // ---------------- calendar ----------------

    public static FLFestival Find(string id) => Array.Find(FarmLife.Data.festivals, f => f.id == id);

    /// <summary>The festival on a given day (or null).</summary>
    public static FLFestival FestivalOn(int day)
    {
        int dps = FarmLife.DaysPerSeason;
        foreach (var f in FarmLife.Data.festivals)
        {
            if (!string.IsNullOrEmpty(f.linked))
            {
                var e = SeasonalEvents.EventOn(day);
                if (e != null && e.id == f.linked) return f;
                continue;
            }
            if (FarmLife.SeasonOf(day) != FarmLife.ParseSeason(f.season)) continue;
            int from = f.from < 0 ? dps + 1 + f.from : f.from, to = f.to < 0 ? dps + 1 + f.to : f.to;
            int dos = FarmLife.DayOfSeason(day);
            if (dos >= Mathf.Max(1, from) && dos <= Mathf.Min(dps, Mathf.Max(from, to))) return f;
        }
        return null;
    }

    public static int NextDayOf(string id, int after)
    {
        for (int d = after; d < after + 200; d++) { var f = FestivalOn(d); if (f != null && f.id == id) return d; }
        return -1;
    }

    public static string Name(FLFestival f) => f == null ? "" : FarmLife.Pick(f.en, f.ar);

    /// <summary>Festivals and birthdays in the next 'days' days (journal calendar).</summary>
    public static List<string> Upcoming(int days)
    {
        var lines = new List<string>();
        int today = CozyFx.Today;
        FLFestival last = null;
        for (int d = today; d < today + days; d++)
        {
            string when = d == today ? Loc.Raw("Today") : Loc.Raw(FarmLife.SeasonOf(d).ToString()) + " " + FarmLife.DayOfSeason(d) + " (" + Loc.Raw("in") + " " + (d - today) + " " + Loc.Raw("days") + ")";
            var f = FestivalOn(d);
            if (f != null && f != last) lines.Add(when + ": " + Name(f));
            last = f;
            foreach (var v in FarmLife.Data.villagers) if (Friendship.IsBirthday(v, d)) lines.Add(when + ": " + Loc.Raw("Birthday") + " - " + Friendship.ShortName(v));
        }
        return lines;
    }

    // ---------------- running ----------------

    private string YearKey(FLFestival f) => f.id + "." + FarmLife.Year(CozyFx.Today);
    public bool Entered(FLFestival f) => f != null && state.entered.Contains(YearKey(f));

    public void Evaluate()
    {
        var f = FestivalOn(CozyFx.Today);
        if (f == Today && evaluatedDay == CozyFx.Today) return;
        bool changed = f != Today;
        evaluatedDay = CozyFx.Today;
        Today = f;
        if (!changed) return;
        if (Decor != null) Destroy(Decor);
        Decor = null;
        if (f == null) return;
        Decor = BuildDecor(f);
        FarmLife.Card(Loc.Raw("Festival") + ": " + Name(f), FarmLife.Pick(f.blurbEn, f.blurbAr),
            f.HasCompetition ? Loc.Raw("Enter the contest at the festival stand by the market, or in the Farm life journal [Y].") : null);
        if (RoadsideStand.Instance != null) RoadsideStand.Instance.Tick();
    }

    private static string HudLine()
    {
        var m = Instance;
        if (m == null || m.Today == null) return null;
        string s = Loc.Raw("Festival") + ": " + Name(m.Today);
        if (m.Today.HasCompetition && !m.Entered(m.Today)) s += " - " + Loc.Raw("contest open");
        if (m.Today.ordersGoal > 0) s += " - " + Loc.Raw("festival orders") + " " + Mathf.Min(m.OrdersDone(m.Today), m.Today.ordersGoal) + "/" + m.Today.ordersGoal;
        return Loc.Display(s);
    }

    // ---------------- festival orders (Ramadan iftar baskets) ----------------

    public int OrdersDone(FLFestival f)
    {
        string k = YearKey(f) + "=";
        foreach (var s in state.orderCounts) if (s.StartsWith(k)) return int.Parse(s.Substring(k.Length));
        return 0;
    }

    public void OnStandOrder(string festivalId)
    {
        var f = Today;
        if (f == null || f.ordersGoal <= 0 || festivalId != f.id) return;
        int n = OrdersDone(f) + 1;
        string k = YearKey(f) + "=";
        state.orderCounts.RemoveAll(s => s.StartsWith(k));
        state.orderCounts.Add(k + n);
        if (n >= f.ordersGoal && !state.goals.Contains(YearKey(f)))
        {
            state.goals.Add(YearKey(f));
            string reward = FarmLife.Grant(f.goalReward, "festivalGoal");
            FarmLife.Card(Name(f) + " - " + Loc.Raw("goal reached!"), Loc.Raw("Reward") + ": " + reward);
        }
        Save();
    }

    // ---------------- contests ----------------

    /// <summary>Why the player can't enter today's contest now (translated), or null.</summary>
    public string BlockReason()
    {
        var f = Today;
        if (f == null || !f.HasCompetition) return Loc.Raw("No contest today");
        if (Entered(f)) return Loc.Raw("Already entered this year");
        var c = f.competition;
        var eco = EconomyManager.Instance;
        if (c.kind == "good" && (eco == null || eco.Stored(c.good) + 1e-3f < c.amount))
            return Loc.Raw("You need") + " " + c.amount.ToString("0") + " " + FarmLife.GoodUnit(c.good) + " " + FarmLife.GoodName(c.good);
        if (c.kind == "animals" && (AnimalPen.Instance == null || AnimalPen.Instance.AnimalCount == 0)) return Loc.Raw("You need at least one animal");
        return null;
    }

    public static string ContestName(FLFestival f) => f != null && f.competition != null ? FarmLife.Pick(f.competition.en, f.competition.ar) : "";

    /// <summary>Enter today's contest. Returns the place (1-4) or 0 if you can't enter.</summary>
    public int Enter()
    {
        var f = Today;
        string block = BlockReason();
        if (block != null) { CozyFx.Alert(Loc.Display(block)); return 0; }
        var c = f.competition;
        int idx = Array.IndexOf(FarmLife.Data.festivals, f);
        var rnd = new System.Random(FarmLife.Year(CozyFx.Today) * 977 + idx * 131 + 17);
        float player = PlayerScore(c, rnd);
        var rivals = FarmLife.Data.rivals;
        lastRivals.Clear();
        int place = 1;
        for (int i = 0; i < rivals.Length; i++)
        {
            float s = 35f + (float)rnd.NextDouble() * 50f;
            if (s > player) place++;
            lastRivals.Add(s);
        }
        state.entered.Add(YearKey(f));
        lastPrize = place - 1 < f.prizes.Length ? f.prizes[place - 1] : null;
        if (lastPrize != null) { FarmLife.Grant(lastPrize, "festivalPrize"); state.ribbons++; }
        if (place == 1) state.wins++;
        Save();
        if (FarmLevel.Instance != null) FarmLevel.Instance.AddXp(FarmLevel.Instance.Rate("festivalEntry"));
        LastPlace = place; lastFestival = f; lastPlayer = player;
        var lines = LastResult.Split('\n');
        FarmLife.Card(lines[0], string.Join("   ", lines, 1, lines.Length - 1));
        GameEvents.RaiseCustom("festivalEntry", 1f);
        return place;
    }

    private float PlayerScore(FLCompetition c, System.Random rnd)
    {
        float luck = (float)rnd.NextDouble();
        var eco = EconomyManager.Instance;
        switch (c.kind)
        {
            case "good":
            {
                if (eco != null) eco.TakeGood(c.good, c.amount);
                int stars = CropMastery.StarsOf(c.good);
                if (c.good.StartsWith("dates")) stars = Mathf.Max(stars, CropMastery.StarsOf("orchard"));
                return 40f + 15f * stars + luck * 35f;
            }
            case "animals":
            {
                var pen = AnimalPen.Instance;
                int n = pen != null ? pen.AnimalCount : 0;
                float care = pen != null ? pen.Health * 20f + pen.Cleanliness * 10f + (pen.HasFeed ? 5f : 0f) : 0f;
                float vet = Friendship.Instance != null ? Friendship.Instance.Hearts("vet") * 2f : 0f;
                return 25f + Mathf.Min(25f, n * 3f) + care + vet + luck * 15f;
            }
            default:   // beauty
                return 20f + FarmBeauty.Stars * 14f + luck * 12f;
        }
    }

    // ---------------- decorations + festival stand ----------------

    private GameObject BuildDecor(FLFestival f)
    {
        var root = new GameObject("Festival " + f.id);
        Vector3 market = DeliveryZone.Market != null ? DeliveryZone.Market.transform.position : CozyFx.FarmStart + new Vector3(0, 0, 60f);
        if (f.id == "flower_fair")
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI * 2f / 8f;
                FlowerStall(root.transform, Build.OnGround(market + new Vector3(Mathf.Cos(a) * 9f, 0, Mathf.Sin(a) * 9f)), i);
            }
        else if (f.id == "melon_day")
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI * 2f / 6f;
                var p = Build.OnGround(market + new Vector3(Mathf.Cos(a) * 8.5f, 0, Mathf.Sin(a) * 8.5f));
                var go = new GameObject("Melon Pile"); go.transform.SetParent(root.transform, false); go.transform.position = p;
                for (int m = 0; m < 4; m++) Build.Part(PrimitiveType.Sphere, go.transform, new Vector3((m % 2) * 0.5f - 0.25f, 0.25f + (m / 2) * 0.35f, (m % 3) * 0.2f), new Vector3(0.5f, 0.4f, 0.4f), Build.Mat(new Color(0.18f, 0.48f, 0.18f), 0.5f), "Melon");
            }
        if (f.HasCompetition) ContestStand(root.transform, Build.OnGround(market + new Vector3(8f, 0, -6f)), f);
        return root;
    }

    private static void FlowerStall(Transform parent, Vector3 pos, int seed)
    {
        var go = new GameObject("Flower Stall"); go.transform.SetParent(parent, false); go.transform.position = pos;
        Build.Part(PrimitiveType.Cube, go.transform, new Vector3(0, 0.4f, 0), new Vector3(1.2f, 0.8f, 0.6f), Build.Mat(new Color(0.55f, 0.38f, 0.22f), 0.1f), "Table");
        var colors = new[] { new Color(0.95f, 0.35f, 0.55f), new Color(1f, 0.85f, 0.25f), new Color(0.95f, 0.95f, 0.95f), new Color(0.8f, 0.3f, 0.8f) };
        for (int i = 0; i < 6; i++) Build.Part(PrimitiveType.Sphere, go.transform, new Vector3(-0.45f + (i % 3) * 0.45f, 0.95f, i < 3 ? -0.12f : 0.12f), Vector3.one * 0.22f, Build.Mat(colors[(i + seed) % 4], 0.4f), "Bouquet");
    }

    private void ContestStand(Transform parent, Vector3 pos, FLFestival f)
    {
        var go = new GameObject("Festival Stand"); go.transform.SetParent(parent, false); go.transform.position = pos;
        var wood = Build.Mat(new Color(0.5f, 0.33f, 0.19f), 0.15f);
        Build.Part(PrimitiveType.Cube, go.transform, new Vector3(0, 0.5f, 0), new Vector3(2.4f, 1f, 0.8f), wood, "Table");
        Build.Part(PrimitiveType.Cube, go.transform, new Vector3(0, 0.9f, 0.41f), new Vector3(2.4f, 0.2f, 0.02f), Build.Mat(new Color(0.15f, 0.35f, 0.75f), 0.3f), "Ribbon");
        for (int i = 0; i < 2; i++) Build.Part(PrimitiveType.Cylinder, go.transform, new Vector3(i == 0 ? -1.1f : 1.1f, 1.5f, -0.3f), new Vector3(0.07f, 1.5f, 0.07f), wood, "Post");
        Build.Part(PrimitiveType.Cube, go.transform, new Vector3(0, 2.8f, -0.3f), new Vector3(2.4f, 0.6f, 0.05f), Build.Mat(new Color(0.85f, 0.6f, 0.15f), 0.3f), "Sign");
        var def = f;
        CozyFx.DataLabel(go.transform, new Vector3(0, 3.5f, -0.3f), () => ContestName(def), 2.4f, Color.white);
        CozyFx.MakeInteractable(go, new Vector3(0, 1f, 0), new Vector3(2.6f, 2f, 1f), () => Loc.Display(Loc.Raw("[E] Festival contest") + ": " + ContestName(def)),
            () => { if (FarmLifeScreen.Instance != null) FarmLifeScreen.Instance.Show(FarmLifeScreen.Tab.Calendar); });
    }
}
