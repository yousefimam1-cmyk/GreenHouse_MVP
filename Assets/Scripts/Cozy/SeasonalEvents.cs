using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Seasonal events on the WorldClock calendar (a year = 4 seasons): Ramadan night market (end of winter: goods sell
/// 20% higher after dark, lanterns at the market), Eid al-Fitr (first day of spring: gift + lights), Saudi National Day
/// (first day of autumn: green flags, +10% prices, celebration bonus) and the Buraidah date festival (last two days of
/// summer: date prices x1.6, festival contract). Decorations are built in code and removed when the event ends.
/// Also adds a small date palm grove by the farmhouse (dates in summer/autumn). Saved under mazraa.cozy.event.* / palm*.
/// </summary>
[MazraaSystem(63)]
public class SeasonalEvents : MonoBehaviour
{
    public static SeasonalEvents Instance;

    public class EventDef
    {
        public string id, nameKey;
        public Season season;
        public int from, to;   // day of season; negative = counted from the end (-1 = last day)
    }

    public static readonly EventDef[] Events =
    {
        new EventDef { id = "ramadan", nameKey = "Ramadan night market", season = Season.Winter, from = 2, to = -1 },
        new EventDef { id = "eid", nameKey = "Eid al-Fitr", season = Season.Spring, from = 1, to = 1 },
        new EventDef { id = "dates", nameKey = "Buraidah date festival", season = Season.Summer, from = -2, to = -1 },
        new EventDef { id = "national", nameKey = "Saudi National Day", season = Season.Autumn, from = 1, to = 1 },
    };

    public EventDef Active { get; private set; }
    public GameObject Decor { get; private set; }
    public GameObject Palms { get; private set; }

    private void Awake()
    {
        Instance = this;
        MarketManager.PriceMultipliers.Remove(PriceFor);
        MarketManager.PriceMultipliers.Add(PriceFor);
        HudLines.Status.Remove(HudLine);
        HudLines.Status.Add(HudLine);
    }

    private void Start()
    {
        WorldClock.DayStarted += OnDay;
        GameEvents.NewGameStarted += OnNewGame;
        Invoke(nameof(Setup), 1.7f);
    }

    private void OnDestroy()
    {
        WorldClock.DayStarted -= OnDay;
        GameEvents.NewGameStarted -= OnNewGame;
        MarketManager.PriceMultipliers.Remove(PriceFor);
        HudLines.Status.Remove(HudLine);
        if (Instance == this) Instance = null;
    }

    private void Setup() { BuildPalms(); Evaluate(); }
    private void OnDay(int day) => Evaluate();
    private void OnNewGame() => Evaluate();

    private static int Year(int day) { int dps = WorldClock.Instance != null ? Mathf.Max(1, WorldClock.Instance.daysPerSeason) : 5; return (day - 1) / (dps * 4); }

    /// <summary>The event running on a given day (or null).</summary>
    public static EventDef EventOn(int day)
    {
        int dps = WorldClock.Instance != null ? Mathf.Max(1, WorldClock.Instance.daysPerSeason) : 5;
        var season = (Season)(((day - 1) / dps) % 4);
        int dos = (day - 1) % dps + 1;
        foreach (var e in Events)
        {
            if (e.season != season) continue;
            if (e.id == "eid" && day <= dps * 4) continue;   // Eid follows Ramadan (none on the very first spring)
            int from = e.from < 0 ? dps + 1 + e.from : e.from, to = e.to < 0 ? dps + 1 + e.to : e.to;
            if (dos >= Mathf.Max(1, from) && dos <= Mathf.Min(dps, to)) return e;
        }
        return null;
    }

    /// <summary>Find the first day (from 'after') when this event runs. For tests and the HUD.</summary>
    public static int NextDayOf(string id, int after)
    {
        for (int d = after; d < after + 200; d++) { var e = EventOn(d); if (e != null && e.id == id) return d; }
        return -1;
    }

    public void Evaluate()
    {
        var e = EventOn(CozyFx.Today);
        if (e != Active)
        {
            if (Decor != null) Destroy(Decor);
            Decor = null;
            Active = e;
            if (e != null)
            {
                Decor = BuildDecor(e);
                CozyFx.Alert(Loc.Display(Loc.Raw("Event") + ": " + Loc.Raw(e.nameKey) + " - " + Loc.Raw(Blurb(e.id))));
            }
        }
        if (e != null) Begin(e);
    }

    private static string Blurb(string id)
    {
        switch (id)
        {
            case "ramadan": return "Ramadan Kareem! The market stays open at night with better prices.";
            case "eid": return "Eid Mubarak! A gift is waiting for you.";
            case "dates": return "Dates sell high - check the festival contract.";
            default: return "Happy National Day! Prices are up for the celebration.";
        }
    }

    // One-time rewards per event per year.
    private void Begin(EventDef e)
    {
        string key = "mazraa.cozy.event." + e.id + "." + Year(CozyFx.Today);
        if (SaveData.GetInt(key, 0) == 1) return;
        SaveData.SetInt(key, 1);
        SaveData.Save();
        var eco = EconomyManager.Instance;
        if (e.id == "eid" && eco != null)
        {
            eco.AddMoney(300); eco.AddFeed(5f);
            CozyFx.Alert(Loc.Display(Loc.Raw("Eid gift (eidiya)") + ": +300 " + Loc.Raw("SAR") + ", " + Loc.Raw("Feed") + " +5"));
        }
        else if (e.id == "national" && eco != null)
        {
            eco.AddMoney(250);
            CozyFx.Alert(Loc.Display(Loc.Raw("National Day celebration bonus") + ": +250 " + Loc.Raw("SAR")));
        }
        else if (e.id == "dates" && MarketManager.Instance != null && eco != null && eco.Good("dates") != null)
        {
            int dps = WorldClock.Instance != null ? WorldClock.Instance.daysPerSeason : 5;
            MarketManager.Instance.Contracts.Add(new Contract
            {
                buyerEn = "Buraidah Date Festival", buyerAr = "مهرجان تمور بريدة", good = "dates",
                amount = 30f, bonus = 1.5f, dueDay = CozyFx.Today + 2 + dps / 5,
            });
        }
    }

    public static bool IsNight => WorldClock.Instance != null && WorldClock.Instance.SunHeight < 0f;

    /// <summary>MarketManager price hook.</summary>
    public static float PriceFor(string good)
    {
        var m = Instance;
        if (m == null || m.Active == null) return 1f;
        switch (m.Active.id)
        {
            case "ramadan": return IsNight ? 1.2f : 1f;
            case "dates": return good == "dates" ? 1.6f : 1f;
            case "national": return 1.1f;
        }
        return 1f;
    }

    private static string HudLine()
    {
        var m = Instance;
        if (m == null || m.Active == null) return null;
        return Loc.Display(Loc.Raw("Event") + ": " + Loc.Raw(m.Active.nameKey));
    }

    // ---------------- decorations ----------------

    private GameObject BuildDecor(EventDef e)
    {
        var root = new GameObject("Event Decor " + e.id);
        Vector3 market = DeliveryZone.Market != null ? DeliveryZone.Market.transform.position : CozyFx.FarmStart + new Vector3(0, 0, 60f);
        Vector3 farm = CozyFx.FarmStart;
        switch (e.id)
        {
            case "ramadan":
                for (int i = 0; i < 12; i++) Lantern(root.transform, Ring(market, 7.5f, i, 12), i % 3 == 0 ? new Color(1f, 0.75f, 0.3f) : new Color(0.95f, 0.55f, 0.2f));
                for (int i = 0; i < 6; i++) Lantern(root.transform, Ring(farm, 5f, i, 6), new Color(1f, 0.8f, 0.4f));
                Banner(root.transform, market + new Vector3(0, 0, -7.5f), "Ramadan Kareem", new Color(0.3f, 0.15f, 0.45f));
                break;
            case "eid":
                for (int i = 0; i < 10; i++) LightPole(root.transform, Ring(market, 8f, i, 10), i);
                for (int i = 0; i < 6; i++) LightPole(root.transform, Ring(farm, 6f, i, 6), i);
                Banner(root.transform, market + new Vector3(0, 0, -8f), "Eid Mubarak", new Color(0.85f, 0.6f, 0.15f));
                break;
            case "national":
                for (int i = 0; i < 10; i++) Flag(root.transform, Ring(market, 8f, i, 10));
                for (int i = 0; i < 6; i++) Flag(root.transform, Ring(farm, 6f, i, 6));
                Banner(root.transform, market + new Vector3(0, 0, -8f), "Happy Saudi National Day", new Color(0f, 0.42f, 0.24f));
                break;
            case "dates":
                for (int i = 0; i < 8; i++) DateCrates(root.transform, Ring(market, 7f, i, 8));
                Banner(root.transform, market + new Vector3(0, 0, -7.5f), "Buraidah Date Festival", new Color(0.45f, 0.25f, 0.1f));
                break;
        }
        return root;
    }

    private static Vector3 Ring(Vector3 c, float r, int i, int n)
    {
        float a = i * Mathf.PI * 2f / n;
        return Build.OnGround(c + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r));
    }

    private static void Lantern(Transform parent, Vector3 pos, Color glow)
    {
        var go = new GameObject("Lantern"); go.transform.SetParent(parent, false); go.transform.position = pos;
        var metal = Build.Mat(new Color(0.25f, 0.2f, 0.12f), 0.6f, 0.6f);
        Build.Part(PrimitiveType.Cylinder, go.transform, new Vector3(0, 1.2f, 0), new Vector3(0.06f, 1.2f, 0.06f), metal, "Pole");
        Build.Part(PrimitiveType.Cube, go.transform, new Vector3(0, 2.55f, 0), new Vector3(0.3f, 0.4f, 0.3f), Build.Mat(glow, 0.8f, 0f, glow * 2.2f), "Fanous");
        Build.Part(PrimitiveType.Cylinder, go.transform, new Vector3(0, 2.8f, 0), new Vector3(0.36f, 0.06f, 0.36f), metal, "Cap");
        var l = new GameObject("Light").AddComponent<Light>();
        l.transform.SetParent(go.transform, false); l.transform.localPosition = new Vector3(0, 2.5f, 0);
        l.type = LightType.Point; l.range = 6f; l.intensity = 1.2f; l.color = glow;
    }

    private static readonly Color[] Bulbs = { new Color(1f, 0.3f, 0.3f), new Color(0.3f, 1f, 0.4f), new Color(0.35f, 0.5f, 1f), new Color(1f, 0.85f, 0.3f) };

    private static void LightPole(Transform parent, Vector3 pos, int seed)
    {
        var go = new GameObject("Eid Lights"); go.transform.SetParent(parent, false); go.transform.position = pos;
        Build.Part(PrimitiveType.Cylinder, go.transform, new Vector3(0, 1.5f, 0), new Vector3(0.07f, 1.5f, 0.07f), Build.Mat(new Color(0.9f, 0.9f, 0.9f), 0.4f), "Pole");
        for (int i = 0; i < 6; i++)
        {
            var c = Bulbs[(i + seed) % Bulbs.Length];
            Build.Part(PrimitiveType.Sphere, go.transform, new Vector3(Mathf.Cos(i) * 0.25f, 1.6f + i * 0.25f, Mathf.Sin(i) * 0.25f), Vector3.one * 0.16f, Build.Mat(c, 0.8f, 0f, c * 2f), "Bulb");
        }
        Build.Part(PrimitiveType.Sphere, go.transform, new Vector3(0, 3.2f, 0), Vector3.one * 0.3f, Build.Mat(new Color(1f, 0.85f, 0.3f), 0.8f, 0f, new Color(2f, 1.6f, 0.5f)), "Star");
    }

    private static void Flag(Transform parent, Vector3 pos)
    {
        var go = new GameObject("Green Flag"); go.transform.SetParent(parent, false); go.transform.position = pos;
        Build.Part(PrimitiveType.Cylinder, go.transform, new Vector3(0, 1.8f, 0), new Vector3(0.06f, 1.8f, 0.06f), Build.Mat(new Color(0.85f, 0.85f, 0.85f), 0.5f, 0.4f), "Pole");
        var green = Build.Mat(new Color(0f, 0.42f, 0.24f), 0.3f);
        Build.Part(PrimitiveType.Cube, go.transform, new Vector3(0.55f, 3.1f, 0), new Vector3(1f, 0.65f, 0.02f), green, "Flag");
        // white sword stripe (no script text: the shahada is never put on decorations)
        Build.Part(PrimitiveType.Cube, go.transform, new Vector3(0.55f, 2.95f, 0.015f), new Vector3(0.7f, 0.05f, 0.01f), Build.Mat(Color.white, 0.3f), "Stripe");
        Build.Part(PrimitiveType.Sphere, go.transform, new Vector3(0, 3.65f, 0), Vector3.one * 0.12f, Build.Mat(new Color(0.2f, 0.9f, 0.4f), 0.6f, 0f, new Color(0.2f, 1.5f, 0.5f)), "Light");
    }

    private static void DateCrates(Transform parent, Vector3 pos)
    {
        var go = new GameObject("Date Crates"); go.transform.SetParent(parent, false); go.transform.position = pos;
        var wood = Build.Mat(new Color(0.6f, 0.44f, 0.26f), 0.1f);
        var dates = Build.Mat(new Color(0.35f, 0.16f, 0.06f), 0.6f);
        for (int i = 0; i < 3; i++)
        {
            Build.Part(PrimitiveType.Cube, go.transform, new Vector3((i - 1) * 0.55f, 0.2f + (i == 1 ? 0.4f : 0f), 0), new Vector3(0.5f, 0.38f, 0.4f), wood, "Crate");
            Build.Part(PrimitiveType.Cube, go.transform, new Vector3((i - 1) * 0.55f, 0.4f + (i == 1 ? 0.4f : 0f), 0), new Vector3(0.44f, 0.05f, 0.34f), dates, "Dates");
        }
    }

    private static void Banner(Transform parent, Vector3 pos, string key, Color color)
    {
        var go = new GameObject("Banner"); go.transform.SetParent(parent, false); go.transform.position = Build.OnGround(pos);
        var pole = Build.Mat(new Color(0.85f, 0.85f, 0.85f), 0.4f);
        Build.Part(PrimitiveType.Cylinder, go.transform, new Vector3(-2.2f, 2f, 0), new Vector3(0.08f, 2f, 0.08f), pole);
        Build.Part(PrimitiveType.Cylinder, go.transform, new Vector3(2.2f, 2f, 0), new Vector3(0.08f, 2f, 0.08f), pole);
        Build.Part(PrimitiveType.Cube, go.transform, new Vector3(0, 3.5f, 0), new Vector3(4.4f, 0.8f, 0.04f), Build.Mat(color, 0.3f), "Cloth");
        CozyFx.Label(go.transform, new Vector3(0, 4.4f, 0), key, 3f, Color.white);
    }

    // ---------------- date palms ----------------

    public int PalmReadyDay => SaveData.GetInt("mazraa.cozy.palmNext", 0);
    public static bool DateSeason => WorldClock.Instance == null || WorldClock.Instance.Season == Season.Summer || WorldClock.Instance.Season == Season.Autumn;

    private void BuildPalms()
    {
        var p = CozyFx.Player;
        Vector3 fwd = p != null ? Vector3.ProjectOnPlane(p.forward, Vector3.up).normalized : Vector3.forward;
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        var root = new GameObject("Date Palms");
        root.transform.position = Build.OnGround(CozyFx.FarmStart - right * 9f - fwd * 6f);
        var trunk = Build.Mat(new Color(0.45f, 0.33f, 0.2f), 0.1f);
        var frond = Build.Mat(new Color(0.25f, 0.5f, 0.2f), 0.2f);
        var fruit = Build.Mat(new Color(0.55f, 0.25f, 0.08f), 0.5f);
        for (int i = 0; i < 3; i++)
        {
            var t = new GameObject("Palm").transform; t.SetParent(root.transform, false); t.localPosition = new Vector3(i * 2.6f - 2.6f, 0, (i % 2) * 1.5f);
            Build.Part(PrimitiveType.Cylinder, t, new Vector3(0, 2.5f, 0), new Vector3(0.35f, 2.5f, 0.35f), trunk, "Trunk");
            for (int f = 0; f < 7; f++)
                Build.Part(PrimitiveType.Cube, t, new Vector3(Mathf.Cos(f * 0.9f) * 0.9f, 5.0f, Mathf.Sin(f * 0.9f) * 0.9f), new Vector3(0.35f, 0.05f, 2.2f), frond, "Frond", new Vector3(20f, f * 51.4f, 0));
            Build.Part(PrimitiveType.Sphere, t, new Vector3(0.3f, 4.6f, 0.1f), new Vector3(0.35f, 0.5f, 0.35f), fruit, "Dates");
        }
        CozyFx.Label(root.transform, new Vector3(0, 6.2f, 0), "Date palms", 2.4f, new Color(1f, 0.9f, 0.6f));
        CozyFx.MakeInteractable(root, new Vector3(0, 1.5f, 0.75f), new Vector3(7.5f, 3f, 2.5f), PalmPrompt, () => HarvestDates());
        Palms = root;
    }

    private string PalmPrompt()
    {
        if (!DateSeason) return Loc.T("Date palms (dates ripen in summer and autumn)");
        return CozyFx.Today >= PalmReadyDay ? Loc.T("[E] Pick dates") : Loc.T("Date palms (more dates in a few days)");
    }

    /// <summary>Pick dates (summer/autumn, every 2 days). Returns kg picked.</summary>
    public float HarvestDates()
    {
        var eco = EconomyManager.Instance;
        if (eco == null || eco.Good("dates") == null) return 0f;
        if (!DateSeason || CozyFx.Today < PalmReadyDay) { CozyFx.Alert(PalmPrompt()); return 0f; }
        float kg = Random.Range(8, 16);
        eco.AddGood("dates", kg);
        SaveData.SetInt("mazraa.cozy.palmNext", CozyFx.Today + 2);
        SaveData.Save();
        CozyFx.Alert(Loc.T("Picked {0} kg of dates", kg.ToString("F0")));
        CollectionBook.Record("crops", "dates");
        LearnMode.Trigger("dates");
        return kg;
    }
}
