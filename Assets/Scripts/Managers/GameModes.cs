using System;
using UnityEngine;

/// <summary>
/// Play modes of a farm, chosen in the new-farm wizard (Career > empty slot), in the spirit of big farming sims:
///  - Campaign: the story chapters and the mission chain (the game as before).
///  - Free play: sandbox - no story cards or missions, any start money (also "Unlimited money"), everything unlocked
///    (all farm-level decor / features, all farmland parcels).
///  - Challenge: a fixed hard start with a goal and a deadline (e.g. "Desert start": reach 1,000,000 within 3 in-game
///    years) and a result card when it is won or lost. No story either.
/// Everything lives in the save (SaveData), so a New game (SaveData.DeleteAll) goes back to Campaign. The farm name and
/// the play time shown on the Career slot cards are stored here too.
/// </summary>
public static class GameModes
{
    public enum Mode { Campaign, FreePlay, Challenge }

    public const string ModeKey = "mazraa.mode", UnlimitedKey = "mazraa.mode.unlimited", ChallengeKey = "mazraa.mode.challenge",
                        ResultKey = "mazraa.mode.result", StartDayKey = "mazraa.mode.startDay",
                        FarmKey = "mazraa.farmName", FarmArKey = "mazraa.farmNameAr", PlayKey = "mazraa.playSeconds";
    /// <summary>Balance kept topped up in a free-play farm with unlimited money.</summary>
    public const int UnlimitedAmount = 99999999;

    public static Mode Current => (Mode)Mathf.Clamp(SaveData.GetInt(ModeKey, 0), 0, 2);
    /// <summary>Story cards, the opening story and the mission chain are only part of a campaign.</summary>
    public static bool HasStory => Current == Mode.Campaign;
    /// <summary>Free play: every farm-level unlock and all farmland from the start.</summary>
    public static bool EverythingUnlocked => Current == Mode.FreePlay;
    public static bool UnlimitedMoney => Current == Mode.FreePlay && SaveData.GetInt(UnlimitedKey, 0) == 1;

    public static string ModeName(Mode m) => m == Mode.Campaign ? "Campaign" : m == Mode.FreePlay ? "Free play" : "Challenge";
    public static string ModeDesc(Mode m) => m == Mode.Campaign ? "The story of your family farm: chapters, missions and rewards guide you."
        : m == Mode.FreePlay ? "Sandbox: no story or missions, pick your money, everything unlocked."
        : "A hard start and a goal against the clock. Can you make it?";

    // ------------------------------------------------------------------ free play money

    /// <summary>Start money choices of a free-play farm (-1 = unlimited money).</summary>
    public static readonly int[] FreeMoney = { 1000, 100000, 1000000, -1 };
    public const int DefaultFreeMoney = 1;

    // ------------------------------------------------------------------ challenges

    public class Challenge
    {
        public string id, name, desc;
        public Career.Start start;
        public Career.Economy economy;
        public int goal, years;
    }

    public static readonly Challenge[] Challenges =
    {
        new Challenge { id = "desert_start", name = "Desert start", desc = "Only the land and 150 in the bank, a hard market. Build a fortune.",
                        start = Career.Start.FromScratch, economy = Career.Economy.Hard, goal = 1000000, years = 3 },
        new Challenge { id = "quick_profit", name = "Market master", desc = "A farm manager's start and one year to prove yourself at the market.",
                        start = Career.Start.FarmManager, economy = Career.Economy.Normal, goal = 250000, years = 1 },
    };

    public static Challenge FindChallenge(string id) { foreach (var c in Challenges) if (c.id == id) return c; return null; }
    public static Challenge CurrentChallenge => Current == Mode.Challenge ? FindChallenge(SaveData.GetString(ChallengeKey, "")) ?? Challenges[0] : null;
    /// <summary>0 = running, 1 = won, 2 = lost.</summary>
    public static int Result => SaveData.GetInt(ResultKey, 0);
    public static int DaysPerYear => 4 * Mathf.Max(1, WorldClock.Instance != null ? WorldClock.Instance.daysPerSeason : 5);
    /// <summary>Last in-game day of the running challenge.</summary>
    public static int DeadlineDay { get { var c = CurrentChallenge; return c == null ? 0 : SaveData.GetInt(StartDayKey, 1) + c.years * DaysPerYear - 1; } }

    public static string GoalText(Challenge c) => Loc.T("Goal: {0} {1} within {2} in-game years", c.goal.ToString("N0"), Loc.Raw("SAR"), c.years);

    // ------------------------------------------------------------------ farm names (no on-screen keyboard: pick one)

    public static readonly string[][] FarmNames =
    {
        new[] { "Palm Oasis Farm", "مزرعة واحة النخيل" },
        new[] { "Golden Dunes Farm", "مزرعة الكثبان الذهبية" },
        new[] { "Green Valley Farm", "مزرعة الوادي الأخضر" },
        new[] { "Date Grove Farm", "مزرعة بستان التمر" },
        new[] { "Falcon Ridge Farm", "مزرعة تلة الصقر" },
        new[] { "Morning Dew Farm", "مزرعة ندى الصباح" },
        new[] { "Sweet Water Farm", "مزرعة الماء العذب" },
        new[] { "Desert Rose Farm", "مزرعة وردة الصحراء" },
        new[] { "Nile Breeze Farm", "مزرعة نسيم النيل" },
        new[] { "Barakah Farm", "مزرعة البركة" },
        new[] { "Crescent Fields", "حقول الهلال" },
        new[] { "Abu Saleh's Farm", "مزرعة أبو صالح" },
    };

    /// <summary>Default farm name of a map (the Delta gets its river name).</summary>
    public static int DefaultFarmName(string region) => region == "nile_delta" ? 8 : 0;
    public static string[] FarmNamePair(int index) => FarmNames[((index % FarmNames.Length) + FarmNames.Length) % FarmNames.Length];
    public static string FarmNameRawOf(int index) { var n = FarmNamePair(index); return Loc.IsArabic ? n[1] : n[0]; }
    public static string FarmNameOf(int index) => Loc.Display(FarmNameRawOf(index));

    public static void SetFarmName(string en, string ar) { SaveData.SetString(FarmKey, en ?? ""); SaveData.SetString(FarmArKey, ar ?? ""); }
    /// <summary>Display name of a farm (shaped Arabic in Arabic), falling back to "My farm".</summary>
    public static string FarmName(string en, string ar)
    {
        if (Loc.IsArabic && !string.IsNullOrEmpty(ar)) return Loc.Display(ar);
        if (!string.IsNullOrEmpty(en)) return en;
        return Loc.T("My farm");
    }
    /// <summary>Current farm name before shaping (to build a longer line that is shaped once).</summary>
    public static string CurrentFarmNameRaw
    {
        get
        {
            string en = SaveData.GetString(FarmKey, ""), ar = SaveData.GetString(FarmArKey, "");
            return Loc.IsArabic && !string.IsNullOrEmpty(ar) ? ar : !string.IsNullOrEmpty(en) ? en : Loc.Raw("My farm");
        }
    }
    public static string CurrentFarmName => FarmName(SaveData.GetString(FarmKey, ""), SaveData.GetString(FarmArKey, ""));

    // ------------------------------------------------------------------ play time

    private static float pendingPlay;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { pendingPlay = 0f; }

    internal static void AddPlayTime(float seconds) { if (seconds > 0f) pendingPlay += seconds; }
    public static float PlaySeconds => SaveData.GetFloat(PlayKey, 0f) + pendingPlay;
    public static void FlushPlayTime()
    {
        if (pendingPlay <= 0f) return;
        SaveData.SetFloat(PlayKey, SaveData.GetFloat(PlayKey, 0f) + pendingPlay);
        pendingPlay = 0f;
    }
    internal static void ClearPlayTime() { pendingPlay = 0f; SaveData.SetFloat(PlayKey, 0f); }

    /// <summary>"2 h 5 min" / "12 min", translated but not shaped (to build a longer line).</summary>
    public static string PlayTimeRaw(float seconds)
    {
        int min = Mathf.FloorToInt(Mathf.Max(0f, seconds) / 60f);
        return min >= 60 ? string.Format(Loc.Raw("{0} h {1} min"), min / 60, min % 60) : string.Format(Loc.Raw("{0} min"), min);
    }
    public static string FormatPlayTime(float seconds) => Loc.Display(PlayTimeRaw(seconds));

    // ------------------------------------------------------------------ starting a farm

    /// <summary>
    /// Store the play mode of a new farm. Called right after the farm was reset and before the map is selected, so the
    /// mission chain and the campaign rebuild for this mode.
    /// </summary>
    public static void Begin(Mode mode, string challengeId, int freeMoney)
    {
        SaveData.SetInt(ModeKey, (int)mode);
        SaveData.SetInt(ResultKey, 0);
        SaveData.SetInt(UnlimitedKey, mode == Mode.FreePlay && FreeMoneyAt(freeMoney) < 0 ? 1 : 0);
        SaveData.SetString(ChallengeKey, mode == Mode.Challenge ? (FindChallenge(challengeId) ?? Challenges[0]).id : "");
        SaveData.SetInt(StartDayKey, WorldClock.Instance != null ? WorldClock.Instance.Day : 1);
        if (mode != Mode.Campaign) SaveData.SetInt("mazraa.introSeen", 1);   // no opening story card outside the campaign
        ClearPlayTime();
    }

    public static int FreeMoneyAt(int index) => FreeMoney[Mathf.Clamp(index, 0, FreeMoney.Length - 1)];

    /// <summary>After Career.Apply: free play sets its own money and unlocks all farmland.</summary>
    public static void AfterCareer(int freeMoney)
    {
        if (Current != Mode.FreePlay) return;
        int money = FreeMoneyAt(freeMoney);
        var eco = EconomyManager.Instance;
        if (eco != null)
        {
            int diff = (money < 0 ? UnlimitedAmount : money) - eco.Money;
            if (diff != 0) using (Ledger.As(Ledger.Skip)) eco.AddMoney(diff);
        }
        if (LandOffice.Instance != null) LandOffice.Instance.GrantAll();
    }
}
