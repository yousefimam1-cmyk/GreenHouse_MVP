using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Category of the money that moves right now. EconomyManager books every change of the balance in FinanceLedger under
/// the current scope: <c>using (Ledger.As(Ledger.Wages)) eco.Spend(wage);</c>. Without a scope, income is "other",
/// spending is "other" (or "purchases" for shop buys).
/// </summary>
public static class Ledger
{
    public const string Sales = "sales", Contracts = "contracts", Production = "production", Loans = "loans", Rewards = "rewards",
        Purchases = "purchases", Wages = "wages", Interest = "interest", Utilities = "utilities", Fuel = "fuel", Repairs = "repairs", Other = "other";
    /// <summary>Scope that books nothing (starting capital of a new career).</summary>
    public const string Skip = "-";
    public static readonly string[] Categories = { Sales, Contracts, Production, Purchases, Wages, Interest, Loans, Utilities, Fuel, Repairs, Rewards, Other };
    public static readonly string[] Names = { "Sales", "Contracts", "Production", "Purchases", "Wages", "Loan interest", "Loans", "Utilities", "Fuel", "Repairs", "Rewards", "Other" };

    private static string scope;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { scope = null; }

    public struct Scope : IDisposable
    {
        private readonly string prev;
        public Scope(string category) { prev = scope; scope = category; }
        public void Dispose() { scope = prev; }
    }

    public static Scope As(string category) => new Scope(category);
    public static string Current => scope;

    /// <summary>Book a change of the balance (called by EconomyManager).</summary>
    public static void Money(int delta, string fallback)
    {
        if (delta == 0) return;
        string c = scope ?? fallback ?? Other;
        if (c == Skip || FinanceLedger.Instance == null) return;
        FinanceLedger.Instance.Add(c, delta);
    }

    public static string Name(string category)
    {
        int i = Array.IndexOf(Categories, category);
        return Loc.Raw(i >= 0 ? Names[i] : "Other");
    }
}

/// <summary>
/// Farm accounts like the finance screen of big farming sims: income and expenses per category for every day
/// (last 30 kept) and totals for the current season. Loan interest is booked when the bank adds it to the debt.
/// Shown on the Finances page (FarmInfoScreen). Saved as "mazraa.ledger"; cleared on a new game.
/// </summary>
[MazraaSystem(44)]
public class FinanceLedger : MonoBehaviour
{
    public static FinanceLedger Instance;
    public const int KeepDays = 30;
    private const string SaveKey = "mazraa.ledger";

    [Serializable] public class Line { public string cat; public int inc, exp; }
    [Serializable] public class DayBook { public int day; public List<Line> lines = new List<Line>(); }
    [Serializable] private class State { public List<DayBook> days = new List<DayBook>(); public List<Line> season = new List<Line>(); public int seasonNo = -1; }

    private State state = new State();

    public static int Today => WorldClock.Instance != null ? WorldClock.Instance.Day : 1;
    public static int SeasonNo => WorldClock.Instance != null ? (WorldClock.Instance.Day - 1) / Mathf.Max(1, WorldClock.Instance.daysPerSeason) : 0;

    private void Awake() { Instance = this; Load(); }
    private void OnEnable() { GameEvents.NewGameStarted += ResetAll; }
    private void OnDisable() { GameEvents.NewGameStarted -= ResetAll; }
    private void OnDestroy() { if (Instance == this) Instance = null; }

    public void Add(string cat, int delta)
    {
        if (delta == 0 || string.IsNullOrEmpty(cat)) return;
        int day = Today;
        var book = state.days.Find(d => d.day == day);
        if (book == null)
        {
            book = new DayBook { day = day };
            state.days.Add(book);
            state.days.RemoveAll(d => d.day <= day - KeepDays);
        }
        Put(book.lines, cat, delta);
        if (state.seasonNo != SeasonNo) { state.seasonNo = SeasonNo; state.season.Clear(); }
        Put(state.season, cat, delta);
        Save();
    }

    private static void Put(List<Line> lines, string cat, int delta)
    {
        var l = lines.Find(x => x.cat == cat);
        if (l == null) lines.Add(l = new Line { cat = cat });
        if (delta > 0) l.inc += delta; else l.exp -= delta;
    }

    private static Line Get(List<Line> lines, string cat) => lines?.Find(x => x.cat == cat);

    public int Income(int day, string cat) => Get(state.days.Find(d => d.day == day)?.lines, cat)?.inc ?? 0;
    public int Expense(int day, string cat) => Get(state.days.Find(d => d.day == day)?.lines, cat)?.exp ?? 0;
    public int Net(int day, string cat) => Income(day, cat) - Expense(day, cat);

    public int DayIncome(int day) { int s = 0; var b = state.days.Find(d => d.day == day); if (b != null) foreach (var l in b.lines) s += l.inc; return s; }
    public int DayExpense(int day) { int s = 0; var b = state.days.Find(d => d.day == day); if (b != null) foreach (var l in b.lines) s += l.exp; return s; }

    private List<Line> Season => state.seasonNo == SeasonNo ? state.season : null;
    public int SeasonIncome(string cat) => Get(Season, cat)?.inc ?? 0;
    public int SeasonExpense(string cat) => Get(Season, cat)?.exp ?? 0;
    public int SeasonNet(string cat) => SeasonIncome(cat) - SeasonExpense(cat);
    public int SeasonIncomeTotal { get { int s = 0; if (Season != null) foreach (var l in Season) s += l.inc; return s; } }
    public int SeasonExpenseTotal { get { int s = 0; if (Season != null) foreach (var l in Season) s += l.exp; return s; } }

    /// <summary>True if the category has anything in the last 7 days or this season.</summary>
    public bool Used(string cat)
    {
        if (SeasonIncome(cat) != 0 || SeasonExpense(cat) != 0) return true;
        for (int d = Today; d > Today - 7; d--) if (Income(d, cat) != 0 || Expense(d, cat) != 0) return true;
        return false;
    }

    public void ResetAll() { state = new State(); Save(); }

    private void Load()
    {
        try { var s = JsonUtility.FromJson<State>(SaveData.GetString(SaveKey, "")); if (s != null) state = s; } catch { }
        if (state.days == null) state.days = new List<DayBook>();
        if (state.season == null) state.season = new List<Line>();
    }

    private void Save() { SaveData.SetString(SaveKey, JsonUtility.ToJson(state)); SaveData.Save(); }
}
