using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class Contract
{
    public string buyerEn, buyerAr, good;
    public float amount, delivered;
    public float bonus;          // price multiplier, e.g. 1.35
    public int dueDay;
}

/// <summary>
/// Living market: each good's price moves every in-game day (random drift + season), selling a lot pushes the
/// price down until it recovers, and buyers post contracts that pay a bonus for a set amount before a deadline.
/// Contracts are filled automatically when you sell at the market. M shows the price board.
/// </summary>
public class MarketManager : MonoBehaviour
{
    public static MarketManager Instance;

    private readonly Dictionary<string, float> trend = new Dictionary<string, float>();   // daily multiplier
    private readonly Dictionary<string, float> glut = new Dictionary<string, float>();    // supply pressure 0..0.4
    public readonly List<Contract> Contracts = new List<Contract>();

    private static readonly (string en, string ar)[] Buyers =
    {
        ("Cairo Fresh Market", "سوق القاهرة للخضار"), ("Nile Hotel Kitchen", "مطبخ فندق النيل"),
        ("Delta Food Factory", "مصنع الدلتا للأغذية"), ("Alexandria Exporters", "مصدّرين إسكندرية"),
        ("School Meals Program", "برنامج وجبات المدارس"), ("Giza Supermarket", "سوبرماركت الجيزة"),
    };

    private void Awake() => Instance = this;

    private void Start()
    {
        var eco = EconomyManager.Instance;
        if (eco != null)
            foreach (var g in eco.Data.goods)
            {
                trend[g.id] = SaveData.GetFloat("mazraa.price." + g.id, 1f);
                glut[g.id] = SaveData.GetFloat("mazraa.glut." + g.id, 0f);
            }
        LoadContracts();
        WorldClock.DayStarted += OnNewDay;
        if (Contracts.Count == 0) OfferContract();
    }

    private void OnDestroy() { WorldClock.DayStarted -= OnNewDay; if (Instance == this) Instance = null; }

    /// <summary>Today's price per unit (EGP).</summary>
    public float Price(string goodId)
    {
        var g = EconomyManager.Instance?.Good(goodId);
        if (g == null) return 0f;
        float t = trend.TryGetValue(goodId, out var v) ? v : 1f;
        float s = glut.TryGetValue(goodId, out var q) ? q : 0f;
        return g.price * t * SeasonFactor(goodId) * (1f - s);
    }

    // Out-of-season produce is scarcer and sells higher.
    private static float SeasonFactor(string goodId)
    {
        var eco = EconomyManager.Instance;
        var crop = eco != null ? eco.FieldCrop(goodId) : null;
        if (crop == null || crop.seasons == null || WorldClock.Instance == null) return 1f;
        string now = WorldClock.SeasonId(WorldClock.Instance.Season);
        foreach (var s in crop.seasons) if (s == now) return 0.9f;
        return 1.3f;
    }

    /// <summary>Sell goods: contracts first (bonus price), the rest at today's price. Returns EGP earned.</summary>
    public int Sell(Dictionary<string, float> cargo, List<string> messages)
    {
        float earned = 0f;
        foreach (var kv in new List<KeyValuePair<string, float>>(cargo))
        {
            float left = kv.Value;
            float unit = Price(kv.Key);
            foreach (var c in Contracts)
            {
                if (c.good != kv.Key || left <= 0f) continue;
                float take = Mathf.Min(left, c.amount - c.delivered);
                if (take <= 0f) continue;
                c.delivered += take;
                left -= take;
                earned += take * unit * c.bonus;
            }
            earned += left * unit;
            // Supply pressure: every 100 units sold lowers the price ~5%, up to 40%.
            glut[kv.Key] = Mathf.Min(0.4f, (glut.TryGetValue(kv.Key, out var q) ? q : 0f) + kv.Value / 2000f);
        }
        for (int i = Contracts.Count - 1; i >= 0; i--)
        {
            var c = Contracts[i];
            if (c.delivered >= c.amount - 0.01f)
            {
                messages.Add(Loc.Display(Loc.Raw("Contract complete") + ": " + BuyerName(c)));
                GameEvents.RaiseContractCompleted();
                Contracts.RemoveAt(i);
            }
        }
        SavePrices();
        SaveContracts();
        return Mathf.RoundToInt(earned);
    }

    private void OnNewDay(int day)
    {
        var eco = EconomyManager.Instance;
        if (eco == null) return;
        foreach (var g in eco.Data.goods)
        {
            float t = trend.TryGetValue(g.id, out var v) ? v : 1f;
            t = Mathf.Clamp(t * UnityEngine.Random.Range(0.88f, 1.13f) + (1f - t) * 0.15f, 0.65f, 1.5f);
            trend[g.id] = t;
            glut[g.id] = Mathf.Max(0f, (glut.TryGetValue(g.id, out var q) ? q : 0f) - 0.12f);
        }
        for (int i = Contracts.Count - 1; i >= 0; i--)
            if (day > Contracts[i].dueDay)
            {
                if (UIManager.Instance != null) UIManager.Instance.ShowAlertMessage(Loc.Display(Loc.Raw("Contract expired") + ": " + BuyerName(Contracts[i])));
                Contracts.RemoveAt(i);
            }
        if (Contracts.Count < 3 && UnityEngine.Random.value < 0.7f) OfferContract();
        SavePrices();
        SaveContracts();
    }

    private void OfferContract()
    {
        var eco = EconomyManager.Instance;
        if (eco == null || eco.Data.goods.Length == 0) return;
        var g = eco.Data.goods[UnityEngine.Random.Range(0, eco.Data.goods.Length)];
        var b = Buyers[UnityEngine.Random.Range(0, Buyers.Length)];
        float amount = g.id == "eggs" ? 60 * UnityEngine.Random.Range(1, 5) : 40 * UnityEngine.Random.Range(1, 6);
        int today = WorldClock.Instance != null ? WorldClock.Instance.Day : 1;
        Contracts.Add(new Contract { buyerEn = b.en, buyerAr = b.ar, good = g.id, amount = amount, bonus = UnityEngine.Random.Range(1.25f, 1.6f), dueDay = today + UnityEngine.Random.Range(2, 5) });
        if (UIManager.Instance != null) UIManager.Instance.ShowAlertMessage(Loc.Display(Loc.Raw("New contract") + ": " + BuyerName(Contracts[Contracts.Count - 1])));
    }

    public static string BuyerName(Contract c) => Loc.IsArabic && !string.IsNullOrEmpty(c.buyerAr) ? c.buyerAr : c.buyerEn;

    [Serializable] private class ContractList { public List<Contract> items = new List<Contract>(); }
    private void SaveContracts() { SaveData.SetString("mazraa.contracts", JsonUtility.ToJson(new ContractList { items = Contracts })); SaveData.Save(); }
    private void LoadContracts()
    {
        try
        {
            var l = JsonUtility.FromJson<ContractList>(SaveData.GetString("mazraa.contracts", "{}"));
            if (l?.items != null) Contracts.AddRange(l.items);
        }
        catch { }
    }
    private void SavePrices()
    {
        foreach (var kv in trend) SaveData.SetFloat("mazraa.price." + kv.Key, kv.Value);
        foreach (var kv in glut) SaveData.SetFloat("mazraa.glut." + kv.Key, kv.Value);
        SaveData.Save();
    }

    public void ResetMarket()
    {
        Contracts.Clear();
        foreach (var k in new List<string>(trend.Keys)) { trend[k] = 1f; glut[k] = 0f; }
        OfferContract();
        SavePrices(); SaveContracts();
    }
}
