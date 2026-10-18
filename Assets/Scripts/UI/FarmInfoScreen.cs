using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// I = inventory (goods in storage, truck cargo, seeds, feed, animals), O = farm stats (money earned, kg harvested,
/// days played, missions done), Fields = FarmCore's Fields page (FieldsPage: seeder / spreader / sprayer settings, density,
/// map layer, every field's crop, water, soil test, expected yield, limiting factor, irrigation, history). On the Fields page
/// Up / Down picks a row, Left / Right changes a setting and Enter opens the irrigation planner on a field. Prices / Contracts / Production / Finances = the economy pages (EconomyPages: market prices per good and
/// selling point, contracts board, production lines, farm accounts); LB / RB or 1-7 switch pages, Up / Down picks a row,
/// Left / Right changes its value and A / Enter acts. Esc / B or the same key closes.
/// Farming-Simulator parity (MenuPages): one tabbed farm menu - also Map (fields), Calendar, Weather forecast, Vehicles
/// (enter / locate / dealer paint and wheels / sell implements), Animals and Help; LB / RB or Q / E switch tabs like FS,
/// 1-9 / 0 jump to the first ten tabs. Also tracks "money earned" (every increase of the
/// balance), saved as "mazraa.ui.moneyEarned" in the save file, so a New Game resets it.
/// </summary>
[MazraaSystem(55)]
public class FarmInfoScreen : MonoBehaviour
{
    public const string EarnedKey = "mazraa.ui.moneyEarned";
    public enum Page { None, Inventory, Stats, Fields, Prices, Contracts, Production, Finances, Map, Calendar, Weather, Vehicles, Animals, Help }
    public const int PageCount = 13;
    public const int FieldsPerPage = 4;

    public static FarmInfoScreen Instance { get; private set; }
    public Page Open { get; private set; }
    public int MoneyEarned { get; private set; }

    private Canvas canvas;
    private TextMeshProUGUI text, tabTitle, prevKey, nextKey, footer;
    private RectTransform panel, tabBar, graph;
    private readonly Image[] tabBg = new Image[PageCount], tabIcon = new Image[PageCount];
    private readonly Image[] incBars = new Image[7], expBars = new Image[7];
    private readonly TextMeshProUGUI[] dayLabels = new TextMeshProUGUI[7];
    private TextMeshProUGUI graphLegend;
    private const float TabW = 64f, GraphH = 150f;
    private static readonly string[] TabIcons = { "warehouse", "info", "poi_field", "coins", "contract", "poi_barn", "money", "map", "calendar", "weather_sun", "vehicle_tractor", "animal_cow", "help" };
    private int lastMoney = -1;
    private int openedFrame;
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
        panel = MenuKit.Panel("Panel", canvas.transform, 1040);
        BuildTabBar(panel);
        tabTitle = MenuKit.Text(panel, UiTheme.Heading, UiTheme.Text, "Page title");
        tabTitle.fontStyle = FontStyles.Bold;
        UiKit.Rule(tabTitle.rectTransform);
        graph = MenuKit.Rect("Finance graph", panel, typeof(LayoutElement));
        graph.GetComponent<LayoutElement>().preferredHeight = GraphH + 40f;
        BuildGraph(graph);
        text = MenuKit.Text(panel, UiTheme.Lead - 2f, UiTheme.Text);
        footer = MenuKit.Text(panel, UiTheme.Small, UiTheme.TextDim, "Footer");
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
            else if (MenuNav.PageNext() || KeyTab(1)) key = (Page)((int)Open % PageCount + 1);
            else if (MenuNav.PagePrev() || KeyTab(-1)) key = (Page)(((int)Open + PageCount - 2) % PageCount + 1);
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

    /// <summary>Q / E (keyboard) switch tabs like Farming Simulator's menu.</summary>
    private static bool KeyTab(int dir)
    {
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null) return false;
        var k = dir > 0 ? kb.eKey : kb.qKey;
        return k != null && k.wasPressedThisFrame;
    }

    // FarmCore Fields page (FieldsPage): Up / Down rows, Left / Right settings, Enter = irrigation planner on a field
    private void FieldsInput()
    {
        int h = MenuNav.Horizontal(), v = MenuNav.Vertical();
        if (h != 0 && FieldsPage.Adjust(h)) Refresh();
        if (v != 0) { FieldsPage.Move(v); Refresh(); }
        if (MenuNav.Confirm() && FieldsPage.Confirm()) { if (Open != Page.None) Refresh(); return; }
        refreshTimer -= Time.unscaledDeltaTime;
        if (refreshTimer <= 0f) { refreshTimer = 1f; Refresh(); }
    }

    // ---------------- economy pages (EconomyPages): Up / Down rows, Left / Right value, A / Enter acts ----------------
    private readonly int[] econRow = new int[PageCount + 1];
    public int EconAction { get; private set; }
    public int Row => econRow[(int)Open];

    private void EconomyInput()
    {
        int v = MenuNav.Vertical(), h = MenuNav.Horizontal();
        bool parity = MenuPages.Handles(Open);
        if (v != 0) { MoveRow(v); if (parity && Open == Page.Vehicles) { EconAction = 0; Refresh(); } }
        if (h != 0) { int a = EconAction; if (parity ? MenuPages.Adjust(Open, Row, ref a, h) : EconomyPages.Adjust(Open, Row, ref a, h)) { EconAction = a; Refresh(); } }
        if (MenuNav.Confirm()) { if (parity) MenuPages.Confirm(Open, Row, EconAction); else EconomyPages.Confirm(Open, Row, EconAction); Refresh(); }
        refreshTimer -= Time.unscaledDeltaTime;
        if (refreshTimer <= 0f) { refreshTimer = 1f; Refresh(); }
    }

    /// <summary>Move the selected row of an economy page.</summary>
    public void MoveRow(int dir)
    {
        int n = RowCountOf(Open);
        econRow[(int)Open] = Mathf.Clamp(Row + dir, 0, Mathf.Max(0, n - 1));
        Refresh();
    }

    private static int RowCountOf(Page p) => MenuPages.Handles(p) ? MenuPages.RowCount(p) : EconomyPages.RowCount(p);

    public void Show(Page page)
    {
        if (canvas == null || page == Page.None) return;
        if (Open == Page.None)
        {
            canvas.gameObject.SetActive(true);
            if (UIManager.Instance != null) UIManager.Instance.SetExternalPanel(true);
        }
        bool changed = Open != page;
        Open = page;
        openedFrame = Time.frameCount;
        Refresh();
        if (changed) UiMotion.Play(panel, 0.985f);
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
        econRow[(int)Open] = Mathf.Clamp(Row, 0, Mathf.Max(0, RowCountOf(Open) - 1));
        string body;
        switch (Open)
        {
            case Page.Inventory: body = InventoryText(); break;
            case Page.Stats: body = StatsText(); break;
            case Page.Fields: body = "<size=68%>" + FieldsPage.Text() + "</size>"; break;
            case Page.Prices: body = "<size=72%>" + EconomyPages.PricesText(Row) + "</size>"; break;
            case Page.Contracts: body = "<size=72%>" + EconomyPages.ContractsText(Row) + "</size>"; break;
            case Page.Production: body = "<size=72%>" + EconomyPages.ProductionText(Row, EconAction) + "</size>"; break;
            case Page.Map: case Page.Calendar: case Page.Weather: case Page.Vehicles: case Page.Animals: case Page.Help:
                body = "<size=72%>" + MenuPages.Text(Open, Row, EconAction) + "</size>"; break;
            default: body = "<size=80%>" + EconomyPages.FinancesText() + "</size>"; break;
        }
        string hint = Open == Page.Inventory ? "[I] or [Esc] close   [O] Farm stats" : Open == Page.Stats ? "[O] or [Esc] close   [I] Inventory"
            : Open == Page.Fields ? "Up / Down: choose   Left / Right: change   [Enter] irrigation planner   [Esc] close"
            : Open == Page.Prices ? "Up / Down: goods   Left / Right: where to sell   [Esc] close"
            : Open == Page.Contracts ? "Up / Down: contracts   [Enter] accept   [Esc] close"
            : Open == Page.Production ? "Up / Down: buildings   Left / Right: action   [Enter] do it   [Esc] close"
            : MenuPages.Handles(Open) ? MenuPages.Hint(Open) : "[Esc] close";
        // tab bar with icons (the open tab lit), the page name as heading, the page body, one line of keys
        RefreshTabBar();
        Loc.ApplyFont(tabTitle); Loc.ApplyFont(footer);
        tabTitle.text = UiText.T(TabNames[(int)Open - 1]);
        tabTitle.alignment = footer.alignment = Loc.IsArabic ? TextAlignmentOptions.Right : TextAlignmentOptions.Left;
        UiKit.PlaceRule(tabTitle.rectTransform.Find("Rule") as RectTransform, Loc.IsArabic);
        bool fin = Open == Page.Finances;
        if (graph.gameObject.activeSelf != fin) graph.gameObject.SetActive(fin);
        if (fin) RefreshGraph();
        string heading = MenuKit.Title(tabTitle.text);   // the page name is the heading above: drop the body's own copy
        int hi = body.IndexOf(heading, System.StringComparison.Ordinal);
        if (hi >= 0 && hi < 16)
        {
            body = body.Remove(hi, heading.Length);
            while (hi < body.Length && body[hi] == '\n') body = body.Remove(hi, 1);
        }
        text.text = body;
        footer.text = UiKit.Keycaps(Glyphs.Apply(UiText.T(hint)));
    }

    private static readonly string[] TabNames = { "Inventory", "Stats", "Fields", "Prices", "Contracts", "Production", "Finances", "Map", "Calendar", "Weather", "Vehicles", "Animals", "Help" };

    // ---------------------------------------------------------------- tab bar (icons, LB / RB at the ends)

    private void BuildTabBar(RectTransform parent)
    {
        tabBar = MenuKit.Rect("Tabs", parent, typeof(LayoutElement));
        tabBar.GetComponent<LayoutElement>().preferredHeight = 56f;
        for (int i = 0; i < PageCount; i++)
        {
            int page = i + 1;
            Image bg;
            var b = MenuKit.Button(tabBar, "Tab " + TabNames[i], () => Show((Page)page), out bg);
            var rt = (RectTransform)b.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(TabW - 6f, 52f);
            tabBg[i] = bg;
            tabIcon[i] = UiKit.IconImage(rt, TabIcons[i], 28f, UiTheme.TextDim);
        }
        prevKey = KeyLabel("Prev"); nextKey = KeyLabel("Next");
    }

    private TextMeshProUGUI KeyLabel(string name)
    {
        var t = MenuKit.Text(tabBar, UiTheme.Small, UiTheme.Text, name);
        t.alignment = TextAlignmentOptions.Center;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        var rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(70f, 40f);
        return t;
    }

    private void RefreshTabBar()
    {
        bool rtl = Loc.IsArabic;
        float span = PageCount * TabW, x0 = -span / 2f + TabW / 2f;
        for (int i = 0; i < PageCount; i++)
        {
            bool on = (int)Open == i + 1;
            var rt = tabBg[i].rectTransform;
            float x = x0 + i * TabW;
            rt.anchoredPosition = new Vector2(rtl ? -x : x, 0f);   // Arabic: first tab on the right
            if (!UiKit.Sliced(tabBg[i], on ? "tab_active" : "tab")) tabBg[i].color = on ? UiTheme.RowHover : new Color(0f, 0f, 0f, 0f);
            tabIcon[i].color = on ? UiTheme.Accent : UiTheme.TextMuted;
        }
        // previous / next keys at the ends (LB / RB on a pad, Q / E on the keyboard)
        string prev = UiKit.Keycaps(GameInput.UsingGamepad ? "[" + Glyphs.PadLabel(Glyphs.Pad.LB) + "]" : "[Q]");
        string next = UiKit.Keycaps(GameInput.UsingGamepad ? "[" + Glyphs.PadLabel(Glyphs.Pad.RB) + "]" : "[E]");
        prevKey.text = rtl ? next : prev; nextKey.text = rtl ? prev : next;
        prevKey.rectTransform.anchoredPosition = new Vector2(-span / 2f - 44f, 0f);
        nextKey.rectTransform.anchoredPosition = new Vector2(span / 2f + 44f, 0f);
    }

    // ---------------------------------------------------------------- finances graph (last 7 days, income vs expenses)

    private void BuildGraph(RectTransform g)
    {
        var baseLine = MenuKit.Rect("Baseline", g, typeof(Image));
        baseLine.anchorMin = new Vector2(0f, 0f); baseLine.anchorMax = new Vector2(1f, 0f); baseLine.pivot = new Vector2(0.5f, 0f);
        baseLine.sizeDelta = new Vector2(0f, 1f); baseLine.anchoredPosition = new Vector2(0f, 28f);
        var bi = baseLine.GetComponent<Image>(); bi.color = UiTheme.Stroke; bi.raycastTarget = false;
        for (int i = 0; i < 7; i++)
        {
            incBars[i] = GraphBar(g, "Income " + i, UiTheme.Good);
            expBars[i] = GraphBar(g, "Expense " + i, UiTheme.Bad);
            dayLabels[i] = MenuKit.Text(g, UiTheme.Caption, UiTheme.TextMuted, "Day " + i);
            dayLabels[i].alignment = TextAlignmentOptions.Center;
            dayLabels[i].textWrappingMode = TextWrappingModes.NoWrap;
            var lr = dayLabels[i].rectTransform;
            lr.anchorMin = lr.anchorMax = lr.pivot = new Vector2(0f, 0f);
            lr.sizeDelta = new Vector2(80f, 24f);
        }
        graphLegend = MenuKit.Text(g, UiTheme.Caption, UiTheme.TextDim, "Legend");
        var gl = graphLegend.rectTransform;
        gl.anchorMin = gl.anchorMax = gl.pivot = new Vector2(1f, 1f);
        gl.sizeDelta = new Vector2(360f, 24f); gl.anchoredPosition = Vector2.zero;
        graphLegend.alignment = TextAlignmentOptions.Right;
    }

    private static Image GraphBar(RectTransform g, string name, Color c)
    {
        var rt = MenuKit.Rect(name, g, typeof(Image));
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0f);
        var img = rt.GetComponent<Image>(); img.raycastTarget = false;
        if (!UiKit.Sliced(img, "bar_fill_white", c)) img.color = c;
        img.pixelsPerUnitMultiplier = 3f;
        return img;
    }

    private void RefreshGraph()
    {
        var led = FinanceLedger.Instance;
        int today = FinanceLedger.Today;
        int max = 1;
        for (int i = 0; i < 7; i++) if (led != null) max = Mathf.Max(max, Mathf.Max(led.DayIncome(today - 6 + i), led.DayExpense(today - 6 + i)));
        float width = 1040f - 2f * (UiTheme.ShadowInset + UiTheme.S5), col = width / 7f;
        bool rtl = Loc.IsArabic;
        for (int i = 0; i < 7; i++)
        {
            int d = today - 6 + i;
            int inc = led != null ? led.DayIncome(d) : 0, exp = led != null ? led.DayExpense(d) : 0;
            float cx = (rtl ? 6 - i : i) * col + col / 2f;
            SetGraphBar(incBars[i], cx - 15f, inc / (float)max);
            SetGraphBar(expBars[i], cx + 15f, exp / (float)max);
            Loc.ApplyFont(dayLabels[i]);
            dayLabels[i].text = d < 1 ? "" : i == 6 ? UiText.T("Today") : Loc.Display(Loc.Raw("D") + d);
            dayLabels[i].rectTransform.anchoredPosition = new Vector2(cx - 40f, 0f);
        }
        Loc.ApplyFont(graphLegend);
        graphLegend.text = "<mark=" + UiTheme.GoodHex + "FF>\u00A0\u00A0</mark> " + UiText.T("Income") + "     <mark=" + UiTheme.BadHex + "FF>\u00A0\u00A0</mark> " + UiText.T("Expenses");
    }

    private static void SetGraphBar(Image bar, float x, float t)
    {
        var rt = bar.rectTransform;
        rt.sizeDelta = new Vector2(24f, Mathf.Max(2f, GraphH * Mathf.Clamp01(t)));
        rt.anchoredPosition = new Vector2(x - 12f, 30f);
    }

    /// <summary>Tab strip across the top (FS-style): the open tab highlighted.</summary>
    public string TabStrip()
    {
        var sb = new StringBuilder("<size=60%>");
        for (int i = 0; i < TabNames.Length; i++)
        {
            if (i > 0) sb.Append("  ");
            string n = UiText.T(TabNames[i]);
            sb.Append((int)Open == i + 1 ? "<color=#F2A93B><b>" + n + "</b></color>" : "<color=#9A968C>" + n + "</color>");
        }
        return sb.Append("</size>").ToString();
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
        var sw = VehicleSwitcher.Instance;
        if (sw != null) sb.Append(Line(UiText.Raw("Distance driven") + ": " + (sw.TotalDistance / 1000f).ToString("0.0") + " " + Loc.Raw("km")));
        if (mm != null) sb.Append(Line(UiText.Raw("Missions done") + ": " + Mathf.Min(mm.Index, mm.Missions.Length) + " / " + mm.Missions.Length));
        if (eco != null)
        {
            int animals = 0; foreach (var a in eco.Data.animals) animals += eco.Owned(a.id);
            sb.Append(Line(UiText.Raw("Animals") + ": " + animals));
        }
        return sb.ToString();
    }
}
