using System;
using UnityEngine;

/// <summary>
/// Daily gift and play streak (like Animal Crossing / mobile farm games), by real calendar day: playing on
/// consecutive days climbs a 7-day ladder of small gifts (coins, seeds, feed, XP; day 7 adds a golden lantern the
/// first time). Missing a day starts the ladder again - no penalty, nothing to buy, no real money anywhere.
/// The gift waits until you collect it ([Y] journal). The streak is account-wide (PlayerPrefs
/// "mazraa.account.gift.*"), so starting a new farm the same day doesn't give a second gift.
/// </summary>
[MazraaSystem(79)]
public class DailyGift : MonoBehaviour
{
    public const string LastKey = "mazraa.account.gift.last", StreakKey = "mazraa.account.gift.streak", ClaimedKey = "mazraa.account.gift.claimed", LanternKey = "mazraa.account.gift.lantern";
    public static DailyGift Instance { get; private set; }

    /// <summary>Clock used for the streak (tests replace it).</summary>
    public static Func<DateTime> Clock = () => DateTime.Now;

    private bool announced;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { Clock = () => DateTime.Now; }

    private void Awake() => Instance = this;
    private void OnDestroy() { if (Instance == this) Instance = null; }

    private void Start() => Invoke(nameof(CheckIn), 3f);

    public static int DayNumber(DateTime d) => d.Year * 10000 + d.Month * 100 + d.Day;
    private static int Today => DayNumber(Clock().Date);
    private static int Yesterday => DayNumber(Clock().Date.AddDays(-1));

    public static int Streak => PlayerPrefs.GetInt(StreakKey, 0);
    public static bool Ready => PlayerPrefs.GetInt(LastKey, 0) == Today && PlayerPrefs.GetInt(ClaimedKey, 0) != Today;
    /// <summary>Position on the 7-day ladder (0-6) for the current streak.</summary>
    public static int Step => Mathf.Max(0, Streak - 1) % 7;

    /// <summary>Record today's visit (streak +1 after yesterday, else back to 1). Returns the streak.</summary>
    public int CheckIn()
    {
        int last = PlayerPrefs.GetInt(LastKey, 0);
        if (last != Today)
        {
            int streak = last == Yesterday ? Streak + 1 : 1;
            PlayerPrefs.SetInt(StreakKey, streak);
            PlayerPrefs.SetInt(LastKey, Today);
            PlayerPrefs.Save();
            announced = false;
        }
        if (Ready && !announced)
        {
            announced = true;
            FarmLife.Card(Loc.Raw("Daily gift") + " - " + Loc.Raw("day") + " " + Streak + " " + Loc.Raw("of your streak"),
                Loc.Raw("Today") + ": " + FarmLife.Describe(Reward(Step)), Loc.Raw("[Y] / D-pad up: open the Farm life journal to collect"));
        }
        return Streak;
    }

    public static FLReward Reward(int step)
    {
        var l = FarmLife.Data.login;
        if (l == null || l.Length == 0) return new FLReward { money = 50 };
        var r = l[Mathf.Clamp(step, 0, l.Length - 1)];
        // the golden lantern is a one-time prize; later 7th days give coins only
        if (!string.IsNullOrEmpty(r.decor) && PlayerPrefs.GetInt(LanternKey, 0) == 1) return new FLReward { money = r.money, feed = r.feed, xp = r.xp, seeds = r.seeds, seedCount = r.seedCount };
        return r;
    }

    /// <summary>Collect today's gift. Returns the summary ("" when nothing is waiting).</summary>
    public string Claim()
    {
        if (!Ready) return "";
        var r = Reward(Step);
        PlayerPrefs.SetInt(ClaimedKey, Today);
        if (!string.IsNullOrEmpty(r.decor)) PlayerPrefs.SetInt(LanternKey, 1);
        PlayerPrefs.Save();
        string s = FarmLife.Grant(r, "dailyGift");
        CozyFx.Alert(Loc.Display(Loc.Raw("Daily gift") + ": " + s));
        GameEvents.RaiseCustom("dailyGift", 1f);
        return s;
    }

    /// <summary>The 7-day ladder for the journal (current day marked).</summary>
    public static string Ladder()
    {
        var parts = new System.Collections.Generic.List<string>();
        for (int i = 0; i < 7; i++) parts.Add((i == Step ? ">" : "") + (i + 1) + ": " + FarmLife.Describe(Reward(i)));
        return string.Join("\n", parts.ToArray());
    }

    /// <summary>Forget the streak (tests / settings).</summary>
    public static void ResetStreak() { foreach (var k in new[] { LastKey, StreakKey, ClaimedKey, LanternKey }) PlayerPrefs.DeleteKey(k); PlayerPrefs.Save(); }
}
