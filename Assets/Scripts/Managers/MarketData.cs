using System;
using System.Collections.Generic;

// Market data: Resources/Data/market.json (+ packs in Resources/Data/Market/*.json). See the notes in market.json.

[Serializable] public class GoodMul { public string good; public float mul = 1f; }

[Serializable]
public class SellPointDef
{
    public string id, nameEn, nameAr;
    public string[] accepts;                 // good ids, "*" = every good
    public float mul = 1f, haul, saturation = 2000f, recovery = 0.12f, maxGlut = 0.4f;
    public GoodMul[] goodMul;
}

[Serializable] public class SeasonCurve { public string good; public float[] mul; }   // spring, summer, autumn, winter
[Serializable] public class DemandEventCfg { public float chancePerDay = 0.45f, minBoost = 1.25f, maxBoost = 1.6f; public int minDays = 2, maxDays = 4; }
[Serializable] public class AiSupplyCfg { public float dailyChance = 0.6f, minGlut = 0.02f, maxGlut = 0.08f, inSeasonGlut = 0.05f, cap = 0.15f; }
[Serializable] public class ClientDef { public string en, ar, region; }

[Serializable]
public class ContractCfg
{
    public int maxOffers = 6, maxActive = 3, offerDays = 3, minDays = 3, maxDays = 7;
    public float offerChance = 0.85f, minPremium = 1.15f, maxPremium = 1.45f, rewardShare = 0.15f, penaltyShare = 0.3f, minValue = 400f, maxValue = 2500f;
    public float fieldChance = 0.25f; public int fieldMin = 4, fieldMax = 10, fieldPayMin = 90, fieldPayMax = 140;
    public ClientDef[] clients, fieldClients;
}

[Serializable]
public class MarketFile
{
    public SellPointDef[] points;
    public SeasonCurve[] seasonCurves;
    public DemandEventCfg events;
    public AiSupplyCfg ai;
    public ContractCfg contracts;
}

/// <summary>A temporary price boost for one good at one selling point ("high demand for onions at the port").</summary>
[Serializable] public class MarketEvent { public string point, good; public float mul = 1.4f; public int endDay; }

[Serializable]
public class Contract
{
    public string buyerEn, buyerAr, good;
    public float amount, delivered;
    public float bonus = 1f;     // older contracts (events, festivals): price multiplier at the market
    public int dueDay;
    // contracts board
    public string id, kind;      // kind: "" / "deliver" (goods) or "field" (tractor work)
    public string task;          // field work: Implement name ("Plow", "Seeder", "Harvester")
    public float unitPrice;      // fixed price per unit delivered (board delivery contracts)
    public int reward, penalty, days, offerUntil;
    public bool board;           // from the contracts board (counts toward the active limit, has a penalty)

    public bool Field() => kind == "field";
    public float Left() => Math.Max(0f, amount - delivered);
}

/// <summary>Pure market maths (tested headless).</summary>
public static class MarketMath
{
    public static bool Accepts(SellPointDef p, string good)
    {
        if (p == null || p.accepts == null || string.IsNullOrEmpty(good)) return false;
        foreach (var a in p.accepts) if (a == "*" || a == good) return true;
        return false;
    }

    public static float GoodMul(SellPointDef p, string good)
    {
        if (p?.goodMul != null) foreach (var m in p.goodMul) if (m != null && m.good == good) return m.mul;
        return 1f;
    }

    /// <summary>Glut after selling 'amount' units at a point.</summary>
    public static float GlutAfter(SellPointDef p, float glut, float amount)
    {
        float sat = p != null && p.saturation > 1f ? p.saturation : 2000f;
        float max = p != null && p.maxGlut > 0f ? p.maxGlut : 0.4f;
        return Math.Min(max, Math.Max(glut, glut + amount / sat));
    }

    /// <summary>Money for selling 'amount' units whose glut-free price is 'priceNoGlut': the price falls while you sell.</summary>
    public static float SaleValue(float priceNoGlut, float glutBefore, float glutAfter, float amount) =>
        amount * priceNoGlut * (1f - (glutBefore + glutAfter) * 0.5f);

    /// <summary>-1 falling, 0 steady, +1 rising: today's price against the average of up to 3 earlier days.</summary>
    public static int Trend(IList<float> history, float now)
    {
        if (history == null || history.Count < 2) return 0;
        float sum = 0f; int n = 0;
        for (int i = history.Count - 2; i >= 0 && n < 3; i--, n++) sum += history[i];
        float avg = sum / n;
        if (avg <= 0f) return 0;
        return now > avg * 1.03f ? 1 : now < avg * 0.97f ? -1 : 0;
    }

    public static string Arrow(int trend) => trend > 0 ? "<color=#8CD95A>▲</color>" : trend < 0 ? "<color=#FF7B6B>▼</color>" : "<color=#BBBBBB>■</color>";

    /// <summary>Lowest, highest and average of a price history.</summary>
    public static void Range(IList<float> h, out float lo, out float hi, out float avg)
    {
        lo = hi = avg = 0f;
        if (h == null || h.Count == 0) return;
        lo = float.MaxValue; hi = float.MinValue; float sum = 0f;
        foreach (var v in h) { lo = Math.Min(lo, v); hi = Math.Max(hi, v); sum += v; }
        avg = sum / h.Count;
    }
}
