using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// I = inventory (goods in storage, truck cargo, seeds, feed, animals), O = farm stats (money earned, kg harvested,
/// days played, missions done). Esc / B or the same key closes. Also tracks "money earned" (every increase of the
/// balance), saved as "mazraa.ui.moneyEarned" in the save file, so a New Game resets it.
/// </summary>
[MazraaSystem(55)]
public class FarmInfoScreen : MonoBehaviour
{
    public const string EarnedKey = "mazraa.ui.moneyEarned";
    public enum Page { None, Inventory, Stats }

    public static FarmInfoScreen Instance { get; private set; }
    public Page Open { get; private set; }
    public int MoneyEarned { get; private set; }

    private Canvas canvas;
    private TextMeshProUGUI text;
    private int lastMoney = -1;
    private int openedFrame;

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
            if (MenuNav.Back() || key == Open) Close();
            else if (key != Page.None) Show(key);
            return;
        }
        bool blocked = Time.timeScale == 0f || (UIManager.Instance != null && UIManager.Instance.IsUIOpened) || UIManager.PanelClosedFrame == Time.frameCount;
        if (blocked) return;
        if (GameInput.Pressed(GameAction.Inventory)) Show(Page.Inventory);
        else if (GameInput.Pressed(GameAction.Stats)) Show(Page.Stats);
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
        text.text = (Open == Page.Inventory ? InventoryText() : StatsText()) + "\n\n<size=80%><color=#BBBBBB>" +
                    Glyphs.Apply(UiText.T(Open == Page.Inventory ? "[I] or [Esc] close   [O] Farm stats" : "[O] or [Esc] close   [I] Inventory")) + "</color></size>";
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
