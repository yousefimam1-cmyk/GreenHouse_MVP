using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// I = inventory (goods in storage, truck cargo, seeds, feed, animals), O = farm stats (money earned, kg harvested,
/// days played, missions done), Fields = precision farming (SoilManager: every field's soil, expected yield, limiting factor,
/// yield history). LB / RB or 1-3 switch pages; on the Fields page Left / Right sets the sowing density and Up / Down scrolls
/// the fields. Prices / Contracts / Production / Finances = the economy pages (EconomyPages: market prices per good and
/// selling point, contracts board, production lines, farm accounts); LB / RB or 1-7 switch pages, Up / Down picks a row,
/// Left / Right changes its value and A / Enter acts. Esc / B or the same key closes. Also tracks "money earned" (every increase of the
/// balance), saved as "mazraa.ui.moneyEarned" in the save file, so a New Game resets it.
/// </summary>
[MazraaSystem(55)]
public class FarmInfoScreen : MonoBehaviour
{
    public const string EarnedKey = "mazraa.ui.moneyEarned";
    public enum Page { None, Inventory, Stats, Fields, Prices, Contracts, Production, Finances }
    public const int PageCount = 7;
    public const int FieldsPerPage = 4;

    public static FarmInfoScreen Instance { get; private set; }
    public Page Open { get; private set; }
    public int MoneyEarned { get; private set; }

    private Canvas canvas;
    private TextMeshProUGUI text;
    private int lastMoney = -1;
    private int openedFrame;
    private int fieldOffset;
    private float refreshTimer;

    private void Awake()
    {
        Instance = this;
        MoneyEarned = SaveData.GetInt(EarnedKey, 0);
    }

    private void OnEnable()
    {
        GameEvents.MoneyChanged += OnMoney;
        GameEvents.NewGameStarted += OnNewGame;
    }

    private void OnDisable()
    {
        GameEvents.MoneyChanged -= OnMoney;
        GameEvents.NewGameStarted -= OnNewGame;
    }

    private void OnDestroy() { if (Instance == this) Instance = null; Loc.OnLanguageChanged -= Refresh; }

    private void Start()
    {
        if (EconomyManager.Instance != null) lastMoney = EconomyManager.Instance.Money;
        canvas = MenuKit.Canvas("Farm Info UI", transform, 62);
        MenuKit.Backdrop(canvas.transform, 0.55f);
        var panel = MenuKit.Panel("Panel", canvas.transform, 760);
        text = MenuKit.Text(panel, 24, Color.white);
        canvas.gameObject.SetActive(false);
        Loc.OnLanguageChanged += Refresh;
    }

    private void OnMoney(int balance)
    {
        if (lastMoney >= 0 && balance > lastMoney)
        {
            MoneyEarned += balance - lastMoney;
            SaveData.SetInt(EarnedKey, MoneyEarned);
        }
        lastMoney = balance;
    }

    private void OnNewGame()
    {
        MoneyEarned = 0;
        SaveData.SetInt(EarnedKey, 0);
        lastMoney = EconomyManager.Instance != null ? EconomyManager.Instance.Money : -1;
    }

    private void Update()
    {
        if (canvas == null || GameMenu.IsOpen) return;
        if (Open != Page.None)
        {
            if (Time.frameCount == openedFrame) return;
            Page key = GameInput.Pressed(GameAction.Inventory) ? Page.Inventory : GameInput.Pressed(GameAction.Stats) ? Page.Stats : Page.None;
            int digit = MenuNav.Digit();
            if (digit >= 0 && digit < PageCount) key = (Page)(digit + 1);
            else if (MenuNav.PageNext()) key = (Page)((int)Open % PageCount + 1);
            else if (MenuNav.PagePrev()) key = (Page)(((int)Open + PageCount - 2) % PageCount + 1);
            if (MenuNav.Back() || (key == Open && digit < 0 && (key == Page.Inventory || key == Page.Stats))) Close();
            else if (key != Page.None && key != Open) Show(key);
            else if (Open == Page.Fields) FieldsInput();
            else if (Open >= Page.Prices) EconomyInput();
            return;
        }
        bool blocked = Time.timeScale == 0f || (UIManager.Instance != null && UIManager.Instance.IsUIOpened) || UIManager.PanelClosedFrame == Time.frameCount;
        if (blocked) return;
        if (GameInput.Pressed(GameAction.Inventory)) Show(Page.Inventory);
        else if (GameInput.Pressed(GameAction.Stats)) Show(Page.Stats);
    }

    private void FieldsInput()
    {
        int h = MenuNav.Horizontal(), v = MenuNav.Vertical();
        if (h != 0 && SoilManager.Instance != null) { SoilManager.Instance.CycleDensity(h); Refresh(); }
        if (v != 0) ScrollFields(v);
        refreshTimer -= Time.unscaledDeltaTime;
        if (refreshTimer <= 0f) { refreshTimer = 1f; Refresh(); }
    }

    /// <summary>Move the Fields page by one field (Up / Down).</summary>
    public void ScrollFields(int dir)
    {
        int n = FieldManager.Instance != null ? FieldManager.Instance.Plots.Count : 0;
        fieldOffset = Mathf.Clamp(fieldOffset + dir, 0, Mathf.Max(0, n - FieldsPerPage));
        Refresh();
    }

    public int FieldOffset => fieldOffset;

    // ---------------- economy pages (EconomyPages): Up / Down rows, Left / Right value, A / Enter acts ----------------
    private readonly int[] econRow = new int[PageCount + 1];
    public int EconAction { get; private set; }
    public int Row => econRow[(int)Open];

    private void EconomyInput()
    {
        int v = MenuNav.Vertical(), h = MenuNav.Horizontal();
        if (v != 0) MoveRow(v);
        if (h != 0) { int a = EconAction; if (EconomyPages.Adjust(Open, Row, ref a, h)) { EconAction = a; Refresh(); } }
        if (MenuNav.Confirm()) { EconomyPages.Confirm(Open, Row, EconAction); Refresh(); }
        refreshTimer -= Time.unscaledDeltaTime;
        if (refreshTimer <= 0f) { refreshTimer = 1f; Refresh(); }
    }

    /// <summary>Move the selected row of an economy page.</summary>
    public void MoveRow(int dir)
    {
        int n = EconomyPages.RowCount(Open);
        econRow[(int)Open] = Mathf.Clamp(Row + dir, 0, Mathf.Max(0, n - 1));
        Refresh();
    }

    public void Show(Page page)
    {
        if (canvas == null || page == Page.None) return;
        if (Open == Page.None)
        {
            canvas.gameObject.SetActive(true);
            if (UIManager.Instance != null) UIManager.Instance.SetExternalPanel(true);
        }
        Open = page;
        openedFrame = Time.frameCount;
        Refresh();
    }

    public void Close()
    {
        if (Open == Page.None) return;
        Open = Page.None;
        canvas.gameObject.SetActive(false);
        if (UIManager.Instance != null) UIManager.Instance.SetExternalPanel(false);
    }

    private void Refresh()
    {
        if (text == null || Open == Page.None) return;
        Loc.ApplyFont(text);
        text.alignment = Loc.IsArabic ? TextAlignmentOptions.TopRight : TextAlignmentOptions.TopLeft;
        econRow[(int)Open] = Mathf.Clamp(Row, 0, Mathf.Max(0, EconomyPages.RowCount(Open) - 1));
        string body;
        switch (Open)
        {
            case Page.Inventory: body = InventoryText(); break;
            case Page.Stats: body = StatsText(); break;
            case Page.Fields: body = "<size=72%>" + SoilManager.FieldsText(fieldOffset, FieldsPerPage) + "</size>"; break;
            case Page.Prices: body = "<size=72%>" + EconomyPages.PricesText(Row) + "</size>"; break;
            case Page.Contracts: body = "<size=72%>" + EconomyPages.ContractsText(Row) + "</size>"; break;
            case Page.Production: body = "<size=72%>" + EconomyPages.ProductionText(Row, EconAction) + "</size>"; break;
            default: body = "<size=80%>" + EconomyPages.FinancesText() + "</size>"; break;
        }
        string hint = Open == Page.Inventory ? "[I] or [Esc] close   [O] Farm stats" : Open == Page.Stats ? "[O] or [Esc] close   [I] Inventory"
            : Open == Page.Fields ? "Left / Right: sowing density   Up / Down: more fields   [Esc] close"
            : Open == Page.Prices ? "Up / Down: goods   Left / Right: where to sell   [Esc] close"
            : Open == Page.Contracts ? "Up / Down: contracts   [Enter] accept   [Esc] close"
            : Open == Page.Production ? "Up / Down: buildings   Left / Right: action   [Enter] do it   [Esc] close" : "[Esc] close";
        text.text = body + "\n\n<size=80%><color=#BBBBBB>" + Glyphs.Apply(UiText.T(hint)) + "\n" + Glyphs.Apply(UiText.T("[LB] [RB] or 1-7: Inventory / Stats / Fields / Prices / Contracts / Production / Finances")) + "</color></size>";
    }

    private static string Line(string s) => "\n" + Loc.Display(s);

    /// <summary>Inventory page text (also used by tests).</summary>
    public static string InventoryText()
    {
        var sb = new StringBuilder();
        sb.Append(MenuKit.Title(UiText.T("Inventory")));
        var eco = EconomyManager.Instance;
        if (eco == null) return sb.ToString();
        sb.Append("\n\n").Append(MenuKit.Title(UiText.T("Goods in storage")));
        bool any = false;
        foreach (var g in eco.Data.goods)
        {
            float n = eco.Stored(g.id);
            if (n < 0.05f) continue;
            any = true;
            sb.Append(Line(EconomyManager.GoodName(g) + ": " + n.ToString("F1") + " " + EconomyManager.Unit(g)));
        }
        if (!any) sb.Append(Line(UiText.Raw("Nothing stored")));
        var truck = FarmTruck.Instance;
        if (truck != null && truck.CargoWeight > 0.05f)
            sb.Append(Line(UiText.Raw("Truck cargo") + ": " + truck.CargoWeight.ToString("F0") + " kg"));

        sb.Append("\n\n").Append(MenuKit.Title(UiText.T("Seeds")));
        any = false;
        foreach (var c in eco.Data.fieldCrops)
        {
            int n = eco.Seeds(c.id);
            if (n <= 0) continue;
            any = true;
            sb.Append(Line(FieldCropText.Name(c) + ": " + n));
        }
        if (!any) sb.Append(Line(UiText.Raw("No seeds")));

        sb.Append("\n\n").Append(MenuKit.Title(UiText.T("Feed"))).Append(Line(UiText.Raw("Feed") + ": " + eco.Feed.ToString("F1")));

        sb.Append("\n\n").Append(MenuKit.Title(UiText.T("Animals")));
        any = false;
        foreach (var a in eco.Data.animals)
        {
            int n = eco.Owned(a.id);
            if (n <= 0) continue;
            any = true;
            sb.Append(Line(EconomyManager.AnimalName(a) + ": " + n + " / " + a.max));
        }
        if (!any) sb.Append(Line(UiText.Raw("No animals yet")));
        return sb.ToString();
    }

    /// <summary>Farm stats page text (also used by tests).</summary>
    public static string StatsText()
    {
        var sb = new StringBuilder();
        sb.Append(MenuKit.Title(UiText.T("Farm stats")));
        var info = Instance;
        var eco = EconomyManager.Instance;
        var gm = GameManager.Instance;
        var mm = MissionManager.Instance;
        var clock = WorldClock.Instance;
        sb.Append("\n");
        if (info != null) sb.Append(Line(UiText.Raw("Money earned") + ": " + info.MoneyEarned.ToString("N0") + " " + Loc.Raw("SAR")));
        if (eco != null) sb.Append(Line(UiText.Raw("Money now") + ": " + eco.Money.ToString("N0") + " " + Loc.Raw("SAR")));
        if (gm != null) sb.Append(Line(UiText.Raw("Harvested") + ": " + gm.TotalHarvestKg.ToString("F0") + " kg (" + gm.HarvestCount + ")"));
        if (clock != null) sb.Append(Line(UiText.Raw("Days played") + ": " + clock.Day));
        if (mm != null) sb.Append(Line(UiText.Raw("Missions done") + ": " + Mathf.Min(mm.Index, mm.Missions.Length) + " / " + mm.Missions.Length));
        if (eco != null)
        {
            int animals = 0; foreach (var a in eco.Data.animals) animals += eco.Owned(a.id);
            sb.Append(Line(UiText.Raw("Animals") + ": " + animals));
        }
        return sb.ToString();
    }
}
