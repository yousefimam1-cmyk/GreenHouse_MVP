using System;
using UnityEngine;

/// <summary>
/// Farm bank: fixed loans (10k / 25k / 50k SAR) up to a credit limit based on farm value, daily interest,
/// repayment with E at the bank sign (next to the market) or the Bank shop page, and an auto-repay toggle
/// that pays an instalment every morning. The debt is kept here (EconomyManager money never goes negative).
/// Saved under mazraa.fin.bank.*; cleared on a new game.
/// </summary>
[MazraaSystem(43)]
public class BankManager : MonoBehaviour
{
    public static BankManager Instance;

    public static readonly int[] LoanSizes = { 10000, 25000, 50000 };
    public const float DailyInterest = 0.015f;     // 1.5% per in-game day
    public const float AutoRepayShare = 0.1f;      // auto-repay pays 10% of the debt (min 500) each morning
    public const int RepayChunk = 5000;

    public int Debt { get; private set; }
    public bool AutoRepay { get; private set; }
    public int InterestPaid { get; private set; }

    private BankSign sign;
    private static readonly Func<string> Line = () => Instance != null ? Instance.HudLine() : null;

    private void Awake()
    {
        Instance = this;
        Load();
        for (int i = 0; i < LoanSizes.Length; i++) RegisterLoan(LoanSizes[i]);
        ShopCatalog.Register(new ShopEntry
        {
            id = "bank_repay", category = "Bank",
            name = () => FinText.R("Repay loan") + " " + FinText.Sar(RepayChunk),
            detail = () => Instance != null && Instance.Debt > 0 ? "(" + FinText.R("Debt") + " " + FinText.Sar(Instance.Debt) + ")" : "",
            price = () => Instance == null ? -1 : Mathf.Min(RepayChunk, Instance.Debt),
            blockReason = () => Instance == null || Instance.Debt <= 0 ? "You have no loan" : null,
            onBought = () => { if (Instance != null) Instance.Paid(Mathf.Min(RepayChunk, Instance.Debt)); }
        });
        ShopCatalog.Register(new ShopEntry
        {
            id = "bank_repay_all", category = "Bank",
            name = () => FinText.R("Repay the whole loan"),
            price = () => Instance == null ? -1 : Instance.Debt,
            blockReason = () => Instance == null || Instance.Debt <= 0 ? "You have no loan" : null,
            onBought = () => { if (Instance != null) Instance.Paid(Instance.Debt); }
        });
        ShopCatalog.Register(new ShopEntry
        {
            id = "bank_autorepay", category = "Bank",
            name = () => FinText.R(Instance != null && Instance.AutoRepay ? "Auto-repay: ON (turn off)" : "Auto-repay: OFF (turn on)"),
            price = () => 0,
            onBought = () => { if (Instance != null) Instance.SetAutoRepay(!Instance.AutoRepay); }
        });
        if (!HudLines.Status.Contains(Line)) HudLines.Status.Add(Line);
    }

    private static void RegisterLoan(int size)
    {
        ShopCatalog.Register(new ShopEntry
        {
            id = "bank_loan_" + (size / 1000) + "k", category = "Bank",
            name = () => FinText.R("Loan") + " " + FinText.Sar(size),
            detail = () => Instance != null ? "(" + FinText.R("Credit left") + " " + FinText.Sar(Instance.CreditLeft) + ")" : "",
            price = () => 0,
            blockReason = () => Instance == null ? "Not available" : Instance.LoanBlock(size),
            onBought = () => { if (Instance != null) Instance.TakeLoan(size); }
        });
    }

    private void OnEnable() { WorldClock.DayStarted += OnNewDay; GameEvents.NewGameStarted += OnNewGame; }
    private void OnDisable() { WorldClock.DayStarted -= OnNewDay; GameEvents.NewGameStarted -= OnNewGame; }
    private void OnDestroy() { if (Instance == this) Instance = null; }

    private void Update()
    {
        if (sign == null && DeliveryZone.Market != null)
        {
            var m = DeliveryZone.Market.transform;
            sign = BankSign.Create(m.position + m.forward * 9f - m.right * 7f, Quaternion.LookRotation(-m.forward));
        }
    }

    // ---------------- credit ----------------

    /// <summary>What the bank thinks the farm is worth: cash, stock at base prices, animals, land, upgrades and greenhouses.</summary>
    public int FarmValue
    {
        get
        {
            var eco = EconomyManager.Instance;
            if (eco == null) return 0;
            float v = eco.Money;
            foreach (var g in eco.Data.goods) v += eco.Stored(g.id) * g.price;
            foreach (var a in eco.Data.animals) v += eco.Owned(a.id) * a.price;
            v += (eco.CapacityLevel + eco.GrowthLevel) * 2000f;
            if (FieldManager.Instance != null) v += FieldManager.Instance.Plots.Count * 1500f;
            if (GameManager.Instance != null) v += GameManager.Instance.Greenhouses.Count * 10000f;
            return Mathf.RoundToInt(v);
        }
    }

    public int CreditLimit => 5000 + Mathf.RoundToInt(FarmValue * 0.8f / 1000f) * 1000;
    public int CreditLeft => Mathf.Max(0, CreditLimit - Debt);

    /// <summary>English key why a loan can't be taken, or null.</summary>
    public string LoanBlock(int size)
    {
        var mm = MissionManager.Instance;
        // A loan must not finish the "have X SAR" mission for you.
        if (mm != null && mm.Current != null && string.Equals(mm.Current.type, "haveMoney", StringComparison.OrdinalIgnoreCase)) return "The bank won't lend during a savings goal";
        if (size > CreditLeft) return "Over your credit limit";
        return null;
    }

    public bool TakeLoan(int size)
    {
        if (LoanBlock(size) != null || EconomyManager.Instance == null) return false;
        Debt += size;
        Save();
        using (Ledger.As(Ledger.Loans)) EconomyManager.Instance.AddMoney(size);
        GameEvents.RaiseCustom("takeLoan", 1f);
        FinText.Alert(FinText.T("Loan approved: {0} SAR. Interest 1.5% a day", size.ToString("N0")));
        return true;
    }

    /// <summary>Money already taken from the player (shop) - reduce the debt.</summary>
    private void Paid(int amount)
    {
        if (amount <= 0 || Debt <= 0) return;
        Debt = Mathf.Max(0, Debt - amount);
        Save();
        if (Debt == 0)
        {
            GameEvents.RaiseCustom("repayLoan", 1f);
            FinText.Alert(FinText.T("Loan fully repaid!"));
        }
        else FinText.Alert(FinText.T("Repaid {0} SAR. Still owed: {1} SAR", amount.ToString("N0"), Debt.ToString("N0")));
    }

    /// <summary>Pay as much of the debt as the cash allows (bank sign). Returns the amount paid.</summary>
    public int RepayWhatYouCan(int max = int.MaxValue)
    {
        var eco = EconomyManager.Instance;
        if (eco == null || Debt <= 0) return 0;
        int pay = Mathf.Min(Mathf.Min(Debt, max), eco.Money);
        bool ok; using (Ledger.As(Ledger.Loans)) ok = pay > 0 && eco.Spend(pay);
        if (!ok) return 0;
        Paid(pay);
        return pay;
    }

    public void SetAutoRepay(bool on)
    {
        AutoRepay = on; Save();
        FinText.Alert(FinText.T(on ? "Auto-repay on: an instalment is paid every morning" : "Auto-repay off"));
    }

    private void OnNewDay(int day)
    {
        if (NetSession.IsClient) return; // co-op: the host runs daily costs and events
        if (Debt <= 0) return;
        int interest = Mathf.CeilToInt(Debt * DailyInterest);
        Debt += interest; InterestPaid += interest;
        Save();
        if (FinanceLedger.Instance != null) FinanceLedger.Instance.Add(Ledger.Interest, -interest);   // accrued on the loan, booked daily
        if (AutoRepay) RepayWhatYouCan(Mathf.Max(500, Mathf.CeilToInt(Debt * AutoRepayShare)));
    }

    private string HudLine()
    {
        if (Debt <= 0) return null;
        return Loc.Display(FinText.R("Loan") + ": " + FinText.Sar(Debt) + " (1.5%/" + FinText.R("day") + ")" + (AutoRepay ? " - " + FinText.R("auto-repay") : ""));
    }

    private void OnNewGame() { Debt = 0; AutoRepay = false; InterestPaid = 0; Save(); }

    private void Load()
    {
        Debt = SaveData.GetInt("mazraa.fin.bank.debt", 0);
        AutoRepay = SaveData.GetInt("mazraa.fin.bank.auto", 0) == 1;
        InterestPaid = SaveData.GetInt("mazraa.fin.bank.interest", 0);
    }

    private void Save()
    {
        SaveData.SetInt("mazraa.fin.bank.debt", Debt);
        SaveData.SetInt("mazraa.fin.bank.auto", AutoRepay ? 1 : 0);
        SaveData.SetInt("mazraa.fin.bank.interest", InterestPaid);
        SaveData.Save();
    }
}
