using UnityEngine;

/// <summary>
/// Career start options chosen when starting a new farm (like the start modes of big farming sims, with Mazraa's own
/// names): how you start (money and help) and the economy difficulty (selling prices). Stored in the save game.
/// </summary>
public static class Career
{
    public enum Start { NewFarmer, FarmManager, FromScratch }
    public enum Economy { Easy, Normal, Hard }

    private const string StartKey = "mazraa.career.start", EconomyKey = "mazraa.career.economy";

    public static Start CurrentStart => (Start)Mathf.Clamp(SaveData.GetInt(StartKey, 0), 0, 2);
    public static Economy CurrentEconomy => (Economy)Mathf.Clamp(SaveData.GetInt(EconomyKey, 1), 0, 2);

    public static string StartName(Start s) => s == Start.NewFarmer ? "New farmer" : s == Start.FarmManager ? "Farm manager" : "Start from scratch";
    public static string StartDesc(Start s) => s == Start.NewFarmer ? "Your family farm, a tractor, a truck and seeds. The academy and the story guide you."
        : s == Start.FarmManager ? "The same farm with 25,000 SAR in the bank to grow fast. For players who know farming games."
        : "Only 150 SAR and the land. Every riyal counts - the hardest start.";
    public static string EconomyName(Economy e) => e == Economy.Easy ? "Easy" : e == Economy.Normal ? "Normal" : "Hard";
    public static string EconomyDesc(Economy e) => e == Economy.Easy ? "Crops sell for 25% more" : e == Economy.Normal ? "Normal market prices" : "Crops sell for 20% less";

    public static int StartMoney(Start s) => s == Start.NewFarmer ? 500 : s == Start.FarmManager ? 25000 : 150;
    public static float SellMultiplier => CurrentEconomy == Economy.Easy ? 1.25f : CurrentEconomy == Economy.Hard ? 0.8f : 1f;

    /// <summary>Apply a career right after the farm was reset for a new game.</summary>
    public static void Apply(Start start, Economy economy)
    {
        SaveData.SetInt(StartKey, (int)start);
        SaveData.SetInt(EconomyKey, (int)economy);
        var eco = EconomyManager.Instance;
        if (eco != null)
        {
            int diff = StartMoney(start) - eco.Money;
            if (diff != 0) eco.AddMoney(diff);
        }
    }
}
