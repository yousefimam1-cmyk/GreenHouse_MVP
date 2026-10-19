using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable] public class LearnQuestion { public string qEn, qAr; public string[] aEn, aAr; public int correct; }
[Serializable] public class LearnCard { public string id, titleEn, titleAr, textEn, textAr, unlock; public int reward = 40; public LearnQuestion[] quiz; }
[Serializable] public class LearnFile { public LearnCard[] cards; }

/// <summary>
/// K = learn mode: tip cards (crops, animals, drip irrigation, crop rotation, water saving, date palm care...) from
/// Resources/Data/Learn/*.json in English and Saudi Arabic. Cards unlock while playing (card.unlock = start,
/// wellStarted, fieldPlanted, harvestField, harvestGreenhouse, animal, animal:&lt;id&gt;, heatwave, day:N, dates,
/// villager:neighbour, sponsor). Each card has a 3-question quiz; 2+ right answers pass and pay a small reward once.
/// The panel also holds the analytics opt-in (0). Saved under mazraa.learn.*.
/// </summary>
[MazraaSystem(68)]
public class LearnMode : MonoBehaviour
{
    public static LearnMode Instance;
    public CozyPanel Panel { get; private set; }
    public readonly List<LearnCard> Cards = new List<LearnCard>();

    private HashSet<string> unlocked, passed;
    private int selected;
    private LearnCard quizCard;
    private int quizStep, quizRight;
    private Func<string> lastResult;   // re-translated on each refresh

    private void Awake()
    {
        Instance = this;
        foreach (var f in DataPacks.LoadAs<LearnFile>("Learn"))
            if (f.cards != null) foreach (var c in f.cards) if (c != null && !string.IsNullOrEmpty(c.id)) { Cards.RemoveAll(x => x.id == c.id); Cards.Add(c); }
        LoadState();
    }

    private void LoadState()
    {
        unlocked = CozyFx.LoadSet("mazraa.learn.unlocked");
        passed = CozyFx.LoadSet("mazraa.learn.passed");
        foreach (var c in Cards) if (c.unlock == "start") unlocked.Add(c.id);
    }

    private void Start()
    {
        Panel = new CozyPanel(transform, "Learn UI", 900f);
        GameEvents.WellStarted += OnWell;
        FieldEvents.Planted += OnPlanted;
        FieldEvents.Harvested += OnFieldHarvest;
        GameEvents.Harvested += OnHarvest;
        GameEvents.UpgradeBought += OnBought;
        GameEvents.AnimalBorn += OnBorn;
        WorldClock.WeatherChanged += OnWeather;
        WorldClock.DayStarted += OnDay;
        SponsorManager.Purchased += OnSponsor;
        GameEvents.NewGameStarted += OnNewGame;
        Loc.OnLanguageChanged += Refresh;
        OnDay(CozyFx.Today);
    }

    private void OnDestroy()
    {
        GameEvents.WellStarted -= OnWell;
        FieldEvents.Planted -= OnPlanted;
        FieldEvents.Harvested -= OnFieldHarvest;
        GameEvents.Harvested -= OnHarvest;
        GameEvents.UpgradeBought -= OnBought;
        GameEvents.AnimalBorn -= OnBorn;
        WorldClock.WeatherChanged -= OnWeather;
        WorldClock.DayStarted -= OnDay;
        SponsorManager.Purchased -= OnSponsor;
        GameEvents.NewGameStarted -= OnNewGame;
        Loc.OnLanguageChanged -= Refresh;
        if (Instance == this) Instance = null;
    }

    private void OnNewGame() { quizCard = null; selected = 0; LoadState(); CozyAnalytics.WriteOptIn(); Refresh(); }

    private void OnWell(WellController w) => Trigger("wellStarted");
    private void OnPlanted(FarmField p, string crop) => Trigger("fieldPlanted");
    private void OnFieldHarvest(FarmField p, string crop, float kg) => Trigger("harvestField");
    private void OnHarvest(Cropbed b, float kg) { if (b != null) Trigger("harvestGreenhouse"); }
    private void OnBorn(string id) { Trigger("animal"); Trigger("animal:" + id); }
    private void OnSponsor(string id) => Trigger("sponsor");
    private void OnWeather(Weather w) { if (w == Weather.Heatwave) Trigger("heatwave"); }
    private void OnBought(string id)
    {
        if (EconomyManager.Instance != null && EconomyManager.Instance.Animal(id) != null) { Trigger("animal"); Trigger("animal:" + id); }
    }
    private void OnDay(int day)
    {
        foreach (var c in Cards)
            if (c.unlock != null && c.unlock.StartsWith("day:") && int.TryParse(c.unlock.Substring(4), out var n) && day >= n) Unlock(c);
    }

    /// <summary>Unlock every card waiting for this trigger (other features call this too, e.g. "dates", "villager:neighbour").</summary>
    public static void Trigger(string trigger)
    {
        var m = Instance;
        if (m == null) return;
        foreach (var c in m.Cards) if (c.unlock == trigger) m.Unlock(c);
    }

    private void Unlock(LearnCard c)
    {
        if (!unlocked.Add(c.id)) return;
        CozyFx.SaveSet("mazraa.learn.unlocked", unlocked);
        CozyFx.Alert(Loc.Display(Loc.Raw("New learn card") + ": " + Title(c) + "  (K)"));
        Refresh();
    }

    public bool IsUnlocked(string id) => unlocked.Contains(id);
    public bool IsPassed(string id) => passed.Contains(id);
    public int UnlockedCount => unlocked.Count;
    public bool InQuiz => quizCard != null;

    public static string Title(LearnCard c) => Loc.IsArabic && !string.IsNullOrEmpty(c.titleAr) ? c.titleAr : c.titleEn;
    public static string Text(LearnCard c) => Loc.IsArabic && !string.IsNullOrEmpty(c.textAr) ? c.textAr : c.textEn;
    private static string Q(LearnQuestion q) => Loc.IsArabic && !string.IsNullOrEmpty(q.qAr) ? q.qAr : q.qEn;
    private static string[] A(LearnQuestion q) => Loc.IsArabic && q.aAr != null && q.aAr.Length == q.aEn.Length ? q.aAr : q.aEn;

    // ---------------- quiz ----------------

    public bool StartQuiz(string id)
    {
        var c = Cards.Find(x => x.id == id);
        if (c == null || !IsUnlocked(id) || c.quiz == null || c.quiz.Length == 0) return false;
        quizCard = c; quizStep = 0; quizRight = 0; lastResult = null;
        Refresh();
        return true;
    }

    /// <summary>Answer the current question (option 1..n). Returns true when the answer was right.</summary>
    public bool Answer(int option)
    {
        if (quizCard == null) return false;
        var q = quizCard.quiz[quizStep];
        bool right = option - 1 == q.correct;
        if (right) quizRight++;
        lastResult = () => right ? Loc.Raw("Correct!") : Loc.Raw("Not quite. The answer was") + ": " + A(q)[Mathf.Clamp(q.correct, 0, A(q).Length - 1)];
        quizStep++;
        if (quizStep >= quizCard.quiz.Length) FinishQuiz();
        Refresh();
        return right;
    }

    private void FinishQuiz()
    {
        var c = quizCard;
        quizCard = null;
        int total = c.quiz.Length, need = Mathf.Max(1, total - 1);
        if (quizRight >= need)
        {
            bool first = passed.Add(c.id);
            CozyFx.SaveSet("mazraa.learn.passed", passed);
            int right = quizRight, paid = first && c.reward > 0 && EconomyManager.Instance != null ? c.reward : 0;
            if (paid > 0) EconomyManager.Instance.AddMoney(paid);
            lastResult = () => Loc.Raw("Quiz passed") + " " + right + "/" + total + (paid > 0 ? "  +" + paid + " " + Loc.Raw("SAR") : "");
            CozyFx.Alert(Loc.Display(lastResult()));
            GameEvents.RaiseCustom("quizPassed", 1f);
        }
        else { int right = quizRight; lastResult = () => Loc.Raw("Quiz not passed") + " " + right + "/" + total + " - " + Loc.Raw("read the card and try again"); }
    }

    // ---------------- panel ----------------

    private void Update()
    {
        if (Panel == null) return;
        if (!Panel.IsOpen)
        {
            if (CozyKeys.Free && CozyKeys.Down("k")) Open();
            return;
        }
        if (Panel.CloseRequested || (CozyKeys.Down("k") && Time.frameCount != Panel.OpenedFrame)) { quizCard = null; Panel.Close(); return; }
        if (quizCard != null)
        {
            int d = CozyKeys.Digit();
            if (d > 0 && d <= quizCard.quiz[quizStep].aEn.Length) Answer(d);
            return;
        }
        if (CozyKeys.Down("up")) { selected = (selected + Cards.Count - 1) % Mathf.Max(1, Cards.Count); Refresh(); }
        if (CozyKeys.Down("down")) { selected = (selected + 1) % Mathf.Max(1, Cards.Count); Refresh(); }
        if (CozyKeys.Down("1") && selected < Cards.Count) StartQuiz(Cards[selected].id);
        if (CozyKeys.Down("0")) { CozyAnalytics.SetOptIn(!CozyAnalytics.OptIn); Refresh(); }
    }

    public void Open() { Panel.Open(); Refresh(); }

    private void Refresh()
    {
        if (Panel == null || !Panel.IsOpen) return;
        var lines = new List<string>();
        if (quizCard != null)
        {
            var q = quizCard.quiz[quizStep];
            if (lastResult != null) lines.Add(lastResult());
            lines.Add(Loc.Raw("Question") + " " + (quizStep + 1) + "/" + quizCard.quiz.Length + ": " + Q(q));
            var ans = A(q);
            for (int i = 0; i < ans.Length; i++) lines.Add((i + 1) + ")  " + ans[i]);
            Panel.Show(Loc.Raw("Quiz") + " - " + Title(quizCard), lines, Loc.Raw("[1-3] answer   [K/Esc] close"));
            return;
        }
        if (lastResult != null) lines.Add(lastResult());
        for (int i = 0; i < Cards.Count; i++)
        {
            var c = Cards[i];
            string mark = i == selected ? "> " : "   ";
            string state = !IsUnlocked(c.id) ? Loc.Raw("locked - keep farming") : IsPassed(c.id) ? Loc.Raw("quiz passed") : Loc.Raw("new");
            lines.Add(mark + (IsUnlocked(c.id) ? Title(c) : "???") + "  (" + state + ")");
        }
        if (selected < Cards.Count && IsUnlocked(Cards[selected].id))
        {
            lines.Add("");
            foreach (var para in Text(Cards[selected]).Split('\n')) lines.Add(para);
        }
        lines.Add("");
        lines.Add("0  " + Loc.Raw("Share play stats (saved only on this computer)") + ": " + Loc.Raw(CozyAnalytics.OptIn ? "On" : "Off"));
        Panel.Show(Loc.Raw("Learn") + "  " + UnlockedCount + "/" + Cards.Count, lines, Loc.Raw("[Up/Down] card   [1] quiz   [0] stats   [K/Esc] close"));
    }
}
