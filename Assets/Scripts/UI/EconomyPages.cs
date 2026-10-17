using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// The economy pages of the farm info screen (FarmInfoScreen): Prices (every good at every selling point with trend,
/// 30-day range, demand events and the best price), Contracts (board offers and active contracts), Production
/// (production lines of the processing buildings) and Finances (income and expenses by category: last 7 days and
/// this season). Text builders and actions are static so tests can drive them. Controls: Up / Down rows,
/// Left / Right change the value of the row (sell destination, production action), A / Enter acts.
/// </summary>
public static class EconomyPages
{
    public const int PriceRows = 9;
    public static readonly string[] ProductionActions = { "Supply inputs", "Collect outputs", "Sell outputs", "Auto" };

    private static string Sar(float v) => Mathf.RoundToInt(v).ToString("N0") + " " + Loc.Raw("SAR");
    private static string Price(float v) => v.ToString("F1");
    private static string Line(string s) => "\n" + Loc.Display(s);
    private static string Hint(string s) => "\n<size=80%><color=#BBBBBB>" + s + "</color></size>";
    private static string Sel(bool on, string s) => on ? "<color=#FF9A3C>> " + s + "</color>" : "  " + s;

    // ---------------- rows ----------------

    public static int RowCount(FarmInfoScreen.Page page)
    {
        switch (page)
        {
            case FarmInfoScreen.Page.Prices: return EconomyManager.Instance != null ? EconomyManager.Instance.Data.goods.Length : 0;
            case FarmInfoScreen.Page.Contracts: return MarketManager.Instance != null ? MarketManager.Instance.Contracts.Count + MarketManager.Instance.Offers.Count : 0;
            case FarmInfoScreen.Page.Production: return Plants().Count;
            default: return 0;
        }
    }

    /// <summary>Processing buildings shown on the Production page (owned, or usable in this game).</summary>
    public static List<ProcessingYard.Plant> Plants()
    {
        var list = new List<ProcessingYard.Plant>();
        if (ProcessingYard.Instance != null)
            foreach (var p in ProcessingYard.Instance.Plants) if (p.owned || ProcessingYard.Usable(p)) list.Add(p);
        return list;
    }

    /// <summary>Contracts page row: active contracts first, then the board.</summary>
    public static Contract ContractAt(int row, out bool active)
    {
        var m = MarketManager.Instance; active = false;
        if (m == null || row < 0) return null;
        if (row < m.Contracts.Count) { active = true; return m.Contracts[row]; }
        row -= m.Contracts.Count;
        return row < m.Offers.Count ? m.Offers[row] : null;
    }

    // ---------------- actions ----------------

    /// <summary>Left / Right on a page. Returns true if something changed.</summary>
    public static bool Adjust(FarmInfoScreen.Page page, int row, ref int action, int dir)
    {
        if (dir == 0) return false;
        if (page == FarmInfoScreen.Page.Prices && MarketManager.Instance != null) { MarketManager.Instance.CycleDestination(dir); return true; }
        if (page == FarmInfoScreen.Page.Production) { action = ((action + dir) % ProductionActions.Length + ProductionActions.Length) % ProductionActions.Length; return true; }
        return false;
    }

    /// <summary>A / Enter on a page row. Returns the message shown (may be empty).</summary>
    public static string Confirm(FarmInfoScreen.Page page, int row, int action)
    {
        if (page == FarmInfoScreen.Page.Contracts)
        {
            var c = ContractAt(row, out bool active);
            var m = MarketManager.Instance;
            if (c == null || m == null || active) return "";
            m.Accept(c);
            return "";
        }
        if (page != FarmInfoScreen.Page.Production) return "";
        var plants = Plants();
        var yard = ProcessingYard.Instance;
        if (yard == null || row < 0 || row >= plants.Count) return "";
        var p = plants[row];
        var eco = EconomyManager.Instance;
        string msg;
        if (!p.owned)
            msg = eco != null && eco.TryBuy(p.def.id) ? Loc.Display(Loc.Raw("Built") + ": " + Name(p)) : Loc.T("Not enough money");
        else if (action == 0) msg = Loc.Display(Loc.Raw("Supplied") + ": " + yard.Supply(p).ToString("F0"));
        else if (action == 1) msg = Loc.Display(Loc.Raw("Collected to farm storage") + ": " + yard.Collect(p).ToString("F0"));
        else if (action == 2)
        {
            var notes = new List<string>();
            int earned = yard.SellOutputs(p, notes);
            notes.Insert(0, Loc.T("Sold the load for {0} SAR", earned.ToString("N0")));
            msg = string.Join("\n", notes.ToArray());
        }
        else { yard.SetAuto(p, !p.auto); msg = Loc.T(p.auto ? "Auto supply and collect: on" : "Auto supply and collect: off"); }
        if (UIManager.Instance != null && msg.Length > 0) UIManager.Instance.ShowAlertMessage(msg);
        return msg;
    }

    // ---------------- Prices ----------------

    public static string PricesText(int sel)
    {
        var sb = new StringBuilder();
        sb.Append(MenuKit.Title(Loc.T("Market prices")));
        var eco = EconomyManager.Instance; var m = MarketManager.Instance;
        if (eco == null || m == null) return sb.ToString();
        string dest = m.Destination == "" ? Loc.Raw("Auto (best price for each good)") : MarketManager.PointName(m.Point(m.Destination));
        sb.Append(Line(Loc.Raw("Sell truck loads at") + ": < " + dest + " >"));
        foreach (var e in m.Events)
        {
            var g = eco.Good(e.good);
            sb.Append("\n<color=#8CD95A>").Append(Loc.Display(Loc.Raw("High demand") + ": " + EconomyManager.GoodName(g) + " - " + MarketManager.PointName(m.Point(e.point)) +
                " +" + Mathf.RoundToInt((e.mul - 1f) * 100f) + "% (" + Loc.Raw("until day") + " " + e.endDay + ")")).Append("</color>");
        }
        var goods = eco.Data.goods;
        int n = goods.Length;
        sel = Mathf.Clamp(sel, 0, Mathf.Max(0, n - 1));
        int first = Mathf.Clamp(sel - PriceRows / 2, 0, Mathf.Max(0, n - PriceRows));
        sb.Append("\n");
        for (int i = first; i < Mathf.Min(n, first + PriceRows); i++)
        {
            var g = goods[i];
            float p = m.Price(g.id);
            string best = m.BestPoint(g.id);
            string row = EconomyManager.GoodName(g) + ": " + Price(p) + " " + Loc.Raw("SAR") + "/" + EconomyManager.Unit(g) + " " + MarketMath.Arrow(m.Trend(g.id, MarketManager.CentralId));
            if (best != MarketManager.CentralId) row += " · " + Loc.Raw("best") + ": " + MarketManager.PointName(m.Point(best)) + " " + Price(m.NetPrice(g.id, best));
            float have = eco.Stored(g.id);
            if (have >= 0.5f) row += " · " + Loc.Raw("in storage") + " " + have.ToString("F0");
            sb.Append("\n").Append(Sel(i == sel, Loc.Display(row)));
            if (i == sel) sb.Append(PriceDetail(g));
        }
        return sb.ToString();
    }

    /// <summary>Every selling point that buys the good: price, net after transport, trend, supply, demand, 30-day range.</summary>
    public static string PriceDetail(GoodDef g)
    {
        var m = MarketManager.Instance;
        var sb = new StringBuilder("<size=85%>");
        foreach (var p in m.Points)
        {
            if (!MarketMath.Accepts(p, g.id)) continue;
            string s = "    " + MarketManager.PointName(p) + ": " + Price(m.Price(g.id, p.id));
            if (p.haul > 0f) s += " (" + Loc.Raw("net") + " " + Price(m.NetPrice(g.id, p.id)) + ")";
            float glut = m.Glut(p.id, g.id);
            if (glut >= 0.01f) s += " · " + Loc.Raw("oversupplied") + " -" + Mathf.RoundToInt(glut * 100f) + "%";
            float ev = m.EventMul(p.id, g.id);
            if (ev > 1.001f) s += " · " + Loc.Raw("high demand") + " +" + Mathf.RoundToInt((ev - 1f) * 100f) + "%";
            var h = m.History(p.id, g.id);
            MarketMath.Range(h, out float lo, out float hi, out float avg);
            if (h.Count > 1) s += " · " + h.Count + Loc.Raw("d") + ": " + Price(lo) + "-" + Price(hi);
            sb.Append("\n").Append(Loc.Display(s)).Append(" ").Append(MarketMath.Arrow(m.Trend(g.id, p.id)));
        }
        return sb.Append("</size>").ToString();
    }

    // ---------------- Contracts ----------------

    public static string ContractsText(int sel)
    {
        var sb = new StringBuilder();
        sb.Append(MenuKit.Title(Loc.T("Contracts")));
        var m = MarketManager.Instance;
        if (m == null) return sb.ToString();
        int today = WorldClock.Instance != null ? WorldClock.Instance.Day : 1;
        sb.Append("\n").Append(MenuKit.Title(Loc.Display(Loc.Raw("Active contracts") + " (" + m.ActiveBoardContracts + "/" + m.MaxActive + ")")));
        if (m.Contracts.Count == 0) sb.Append(Line("  " + Loc.Raw("None - accept one from the board")));
        int row = 0;
        foreach (var c in m.Contracts) sb.Append("\n").Append(Sel(row++ == sel, Loc.Display(ContractLine(c, true, today))));
        sb.Append("\n\n").Append(MenuKit.Title(Loc.T("Contracts board")));
        if (m.Offers.Count == 0) sb.Append(Line("  " + Loc.Raw("No offers today - new clients come every morning")));
        foreach (var c in m.Offers) sb.Append("\n").Append(Sel(row++ == sel, Loc.Display(ContractLine(c, false, today))));
        return sb.ToString();
    }

    public static string ContractLine(Contract c, bool active, int today)
    {
        string s = MarketManager.BuyerName(c) + ": " + MarketManager.ContractWhat(c);
        if (active) s += " · " + c.delivered.ToString("F0") + "/" + c.amount.ToString("F0");
        if (!c.Field()) s += " · " + (c.unitPrice > 0f ? Price(c.unitPrice) + " " + Loc.Raw("SAR") + "/" + EconomyManager.Unit(EconomyManager.Instance?.Good(c.good)) : "+" + Mathf.RoundToInt((c.bonus - 1f) * 100f) + "%");
        if (c.reward > 0) s += " · " + Loc.Raw("bonus") + " " + Sar(c.reward);
        if (c.penalty > 0) s += " · " + Loc.Raw("penalty") + " " + Sar(c.penalty);
        s += " · " + (active ? Loc.Raw("due day") + " " + c.dueDay + " (" + Mathf.Max(0, c.dueDay - today) + " " + Loc.Raw("days left") + ")" : c.days + " " + Loc.Raw("days"));
        return s;
    }

    // ---------------- Production ----------------

    public static string Name(ProcessingYard.Plant p) => FishRules.Name(p.def.nameEn, p.def.nameAr);

    private static string Store(Dictionary<string, float> d)
    {
        var eco = EconomyManager.Instance;
        if (d.Count == 0) return Loc.Raw("empty");
        var parts = new List<string>();
        foreach (var kv in d) parts.Add(kv.Value.ToString("F0") + " " + EconomyManager.GoodName(eco?.Good(kv.Key)));
        return string.Join(", ", parts.ToArray());
    }

    public static string ProductionText(int sel, int action)
    {
        var sb = new StringBuilder();
        sb.Append(MenuKit.Title(Loc.T("Production")));
        var plants = Plants();
        var eco = EconomyManager.Instance;
        if (plants.Count == 0) { sb.Append(Line(Loc.Raw("No production buildings in this game"))); return sb.ToString(); }
        for (int i = 0; i < plants.Count; i++)
        {
            var p = plants[i];
            var outs = new List<string>(); var ins = new List<string>();
            foreach (var g in ProcessingYard.Inputs(p)) ins.Add(EconomyManager.GoodName(eco?.Good(g)));
            if (p.def.recipes != null) foreach (var r in p.def.recipes) { var og = eco?.Good(r.output); if (og != null && !outs.Contains(EconomyManager.GoodName(og))) outs.Add(EconomyManager.GoodName(og)); }
            string head = Name(p) + ": " + string.Join("/", ins.ToArray()) + " -> " + string.Join("/", outs.ToArray());
            sb.Append("\n").Append(Sel(i == sel, Loc.Display(head)));
            if (!p.owned)
            {
                sb.Append(Line("    " + Loc.Raw("Not built") + " · " + Loc.Raw("price") + " " + Sar(p.def.price) + " · " + Loc.Raw("running cost") + " " + Sar(p.def.dailyCost) + "/" + Loc.Raw("day")));
                if (i == sel) sb.Append(Hint(Glyphs.Apply(Loc.T("[Enter] Buy and build"))));
                continue;
            }
            string state = p.busy ? Loc.Raw("Working") + " " + Mathf.CeilToInt(p.timeLeft) + "s" : p.inStore.Count > 0 ? Loc.Raw("Waiting") : Loc.Raw("Idle - needs inputs");
            sb.Append(Line("    " + state + " · " + ProcessingYard.RatePerMinute(p).ToString("F0") + "/" + Loc.Raw("min") + " · " + Loc.Raw("running cost") + " " + Sar(p.def.dailyCost) + "/" + Loc.Raw("day") +
                (p.auto ? " · " + Loc.Raw("Auto") : "")));
            sb.Append(Line("    " + Loc.Raw("Input") + " " + p.InTotal.ToString("F0") + "/" + p.def.inputCapacity.ToString("F0") + ": " + Store(p.inStore)));
            sb.Append(Line("    " + Loc.Raw("Output") + " " + p.OutTotal.ToString("F0") + "/" + p.def.outputCapacity.ToString("F0") + ": " + Store(p.outStore)));
            if (i == sel)
            {
                var acts = new StringBuilder("    ");
                for (int a = 0; a < ProductionActions.Length; a++)
                {
                    string label = Loc.Raw(ProductionActions[a]) + (a == 3 ? ": " + Loc.Raw(p.auto ? "on" : "off") : "");
                    acts.Append(a == action ? "<color=#FF9A3C>[" + Loc.Display(label) + "]</color>  " : Loc.Display(label) + "  ");
                }
                sb.Append("\n").Append(acts);
            }
        }
        return sb.ToString();
    }

    // ---------------- Finances ----------------

    private static string Num(int v) => v == 0 ? "-" : v.ToString("N0");

    public static string FinancesText()
    {
        var sb = new StringBuilder();
        sb.Append(MenuKit.Title(Loc.T("Finances")));
        var led = FinanceLedger.Instance; var eco = EconomyManager.Instance;
        if (led == null) return sb.ToString();
        int today = FinanceLedger.Today;
        const float first = 24f, step = 9.4f;
        var head = new StringBuilder("<size=80%><b>").Append(Loc.T("Category"));
        for (int i = 6; i >= 0; i--)
        {
            int d = today - i;
            head.Append("<pos=").Append((first + (6 - i) * step).ToString("F1", System.Globalization.CultureInfo.InvariantCulture)).Append("%>")
                .Append(d < 1 ? "" : i == 0 ? Loc.T("Today") : Loc.Raw("D") + d);
        }
        head.Append("<pos=").Append((first + 7 * step).ToString("F1", System.Globalization.CultureInfo.InvariantCulture)).Append("%>").Append(Loc.T("Season")).Append("</b>");
        sb.Append("\n").Append(head);
        bool any = false;
        foreach (var cat in Ledger.Categories)
        {
            if (!led.Used(cat)) continue;
            any = true;
            sb.Append("\n").Append(Loc.Display(Ledger.Name(cat)));
            for (int i = 6; i >= 0; i--) sb.Append(Cell(first + (6 - i) * step, led.Net(today - i, cat)));
            sb.Append(Cell(first + 7 * step, led.SeasonNet(cat)));
        }
        if (!any) sb.Append(Line(Loc.Raw("No income or expenses yet")));
        sb.Append("\n<b>").Append(Loc.T("Income"));
        for (int i = 6; i >= 0; i--) sb.Append(Cell(first + (6 - i) * step, led.DayIncome(today - i)));
        sb.Append(Cell(first + 7 * step, led.SeasonIncomeTotal));
        sb.Append("\n").Append(Loc.T("Expenses"));
        for (int i = 6; i >= 0; i--) sb.Append(Cell(first + (6 - i) * step, -led.DayExpense(today - i)));
        sb.Append(Cell(first + 7 * step, -led.SeasonExpenseTotal));
        sb.Append("\n").Append(Loc.T("Net"));
        for (int i = 6; i >= 0; i--) sb.Append(Cell(first + (6 - i) * step, led.DayIncome(today - i) - led.DayExpense(today - i)));
        sb.Append(Cell(first + 7 * step, led.SeasonIncomeTotal - led.SeasonExpenseTotal)).Append("</b></size>");
        if (eco != null) sb.Append("\n").Append(Line(Loc.Raw("Money now") + ": " + Sar(eco.Money)));
        if (BankManager.Instance != null && BankManager.Instance.Debt > 0)
            sb.Append(Line(Loc.Raw("Debt") + ": " + Sar(BankManager.Instance.Debt) + " · " + Loc.Raw("interest paid so far") + " " + Sar(BankManager.Instance.InterestPaid)));
        return sb.ToString();
    }

    private static string Cell(float pos, int v)
    {
        string c = v > 0 ? "#8CD95A" : v < 0 ? "#FF7B6B" : "#888888";
        return "<pos=" + pos.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "%><color=" + c + ">" + Num(v) + "</color>";
    }
}
