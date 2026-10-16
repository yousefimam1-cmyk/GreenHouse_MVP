using UnityEngine;

/// <summary>
/// Runs the play mode during the game: tops up unlimited money, checks a challenge's goal and deadline (result card),
/// shows the challenge progress on the HUD and counts the play time shown on the save cards.
/// </summary>
[MazraaSystem(37)]
public class GameModeDirector : MonoBehaviour
{
    public static GameModeDirector Instance { get; private set; }
    /// <summary>Text of the last challenge result card (tests).</summary>
    public string LastResultCard { get; private set; } = "";

    private float flushTimer = 5f;
    private bool topping;

    private void Awake()
    {
        Instance = this;
        HudLines.Status.Remove(HudLine);
        HudLines.Status.Add(HudLine);
    }

    private void OnDestroy()
    {
        HudLines.Status.Remove(HudLine);
        GameModes.FlushPlayTime();
        if (Instance == this) Instance = null;
    }

    private void OnEnable() { GameEvents.MoneyChanged += OnMoney; WorldClock.DayStarted += OnDay; GameEvents.NewGameStarted += OnNewGame; }
    private void OnDisable() { GameEvents.MoneyChanged -= OnMoney; WorldClock.DayStarted -= OnDay; GameEvents.NewGameStarted -= OnNewGame; }

    private void OnNewGame() { GameModes.ClearPlayTime(); LastResultCard = ""; }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        if (!GameMenu.IsOpen && Time.timeScale > 0f) GameModes.AddPlayTime(dt);
        flushTimer -= dt;
        if (flushTimer <= 0f) { flushTimer = 5f; GameModes.FlushPlayTime(); }
        var eco = EconomyManager.Instance;
        if (eco != null && GameModes.UnlimitedMoney && eco.Money < GameModes.UnlimitedAmount) OnMoney(eco.Money);
    }

    private void OnMoney(int balance)
    {
        if (topping) return;
        var eco = EconomyManager.Instance;
        if (eco != null && GameModes.UnlimitedMoney && balance < GameModes.UnlimitedAmount)
        {
            topping = true;
            try { using (Ledger.As(Ledger.Skip)) eco.AddMoney(GameModes.UnlimitedAmount - balance); }
            finally { topping = false; }
            return;
        }
        var c = GameModes.CurrentChallenge;
        if (c != null && GameModes.Result == 0 && balance >= c.goal) Finish(true);
    }

    private void OnDay(int day)
    {
        if (GameModes.CurrentChallenge == null || GameModes.Result != 0) return;
        if (day > GameModes.DeadlineDay) Finish(false);
    }

    /// <summary>End the running challenge (won or lost) and show the result card.</summary>
    public void Finish(bool won)
    {
        var c = GameModes.CurrentChallenge;
        if (c == null || GameModes.Result != 0) return;
        SaveData.SetInt(GameModes.ResultKey, won ? 1 : 2);
        SaveData.Save();
        LastResultCard = ResultCard(won);
        if (CampaignManager.Instance != null) CampaignManager.Instance.Show(LastResultCard, 20f);
        else if (UIManager.Instance != null) UIManager.Instance.ShowAlertMessage(Loc.T(won ? "Challenge complete!" : "Challenge failed"));
    }

    /// <summary>Rich text of the challenge result card.</summary>
    public static string ResultCard(bool won)
    {
        var c = GameModes.CurrentChallenge;
        if (c == null) return "";
        int day = WorldClock.Instance != null ? WorldClock.Instance.Day : 1;
        int used = Mathf.Max(1, day - SaveData.GetInt(GameModes.StartDayKey, 1) + 1);
        int money = EconomyManager.Instance != null ? EconomyManager.Instance.Money : 0;
        var sb = new System.Text.StringBuilder(256);
        sb.Append("<size=70%><color=#FAC240><b>").Append(Loc.T("CHALLENGE")).Append("</b></color></size>\n");
        sb.Append("<size=150%><b>").Append(Loc.T(c.name)).Append("</b></size>\n\n");
        sb.Append(won ? "<color=#9BE07A>" + Loc.T("Challenge complete!") + "</color>" : "<color=#FF8A70>" + Loc.T("Challenge failed") + "</color>").Append('\n');
        sb.Append(GameModes.GoalText(c)).Append('\n');
        sb.Append(Loc.T("Money")).Append(": ").Append(money.ToString("N0")).Append(' ').Append(Loc.T("SAR")).Append("   ")
          .Append(Loc.T("Days used: {0}", used)).Append('\n');
        sb.Append("\n<size=70%><color=#FFFFFF99>").Append(Loc.T("Keep farming as long as you like")).Append("</color></size>");
        return sb.ToString();
    }

    private static string HudLine()
    {
        var c = GameModes.CurrentChallenge;
        if (c == null) return GameModes.UnlimitedMoney ? Loc.T("Free play - unlimited money") : null;
        if (GameModes.Result == 1) return Loc.T(c.name) + ": " + Loc.T("Challenge complete!");
        if (GameModes.Result == 2) return Loc.T(c.name) + ": " + Loc.T("Challenge failed");
        int money = EconomyManager.Instance != null ? EconomyManager.Instance.Money : 0;
        int day = WorldClock.Instance != null ? WorldClock.Instance.Day : 1;
        int left = Mathf.Max(0, GameModes.DeadlineDay - day + 1);
        return Loc.T(c.name) + ": " + money.ToString("N0") + " / " + c.goal.ToString("N0") + "  -  " + Loc.T("{0} days left", left);
    }
}
