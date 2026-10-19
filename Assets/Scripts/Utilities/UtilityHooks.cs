using UnityEngine;

/// <summary>
/// Small static hooks between the Utilities grid and the rest of the game (set every in-game hour by UtilityGrid).
/// Defaults keep the original behaviour when the utilities system is absent.
/// - Cropbed growth is multiplied by GreenhouseGrowth (power for cooling pads + water per bed).
/// - FarmCore irrigation pumps on grid power add IrrigationKw to the grid's load and run at PowerRatio.
/// </summary>
public static class UtilityHooks
{
    /// <summary>0..1 multiplier on greenhouse bed growth.</summary>
    public static float GreenhouseGrowth = 1f;
    /// <summary>kW the FarmCore irrigation pumps on grid power draw (IrrigationSystem sets it every step).</summary>
    public static float IrrigationKw;
    /// <summary>0..1 share of the power demand the grid served last hour.</summary>
    public static float PowerRatio = 1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void ResetHooks() { GreenhouseGrowth = 1f; IrrigationKw = 0f; PowerRatio = 1f; }
}
