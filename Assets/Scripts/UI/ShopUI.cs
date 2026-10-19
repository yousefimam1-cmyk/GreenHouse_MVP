using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Shop panel built in code, one page per category (feed, seeds, animals, upgrades, plus anything features
/// register in ShopCatalog). Click a row or press its number; Left/Right (or A/D) change page; Esc closes.
/// Gamepad: D-pad / stick up-down select a row, A (Cross) buys it, LB / RB (or D-pad left-right) change page, B closes.
/// Opened by a ShopStall. Prices come from EconomyManager (economy.json) and ShopCatalog.
/// </summary>
public class ShopUI : MonoBehaviour
{
    public static ShopUI Instance;
    public bool IsOpen { get; private set; }

    private Canvas canvas;
    private RectTransform panel, list;
    private TextMeshProUGUI title, footer;
    private const int PageSize = 10;
    private readonly List<(Button btn, TextMeshProUGUI label)> rows = new List<(Button, TextMeshProUGUI)>();
    private readonly List<(string category, List<string> ids)> pages = new List<(string, List<string>)>();
    private int page;
    private int openedFrame;
    private int selectedRow;

    /// <summary>Row highlighted for D-pad / Enter buying.</summary>
    public int SelectedRow => selectedRow;

    private void Awake() => Instance = this;

    private void Start()
    {
        Build();
        Loc.OnLanguageChanged += Refresh;
        GameEvents.MoneyChanged += OnMoney;
    }

    private void OnDestroy()
    {
        Loc.OnLanguageChanged -= Refresh;
        GameEvents.MoneyChanged -= OnMoney;
    }

    private void OnMoney(int m) { if (IsOpen) Refresh(); }

    /// <summary>All items grouped by category, in shop order.</summary>
    private List<(string category, string id)> Items()
    {
        var items = new List<(string, string)> { ("Supplies", "feed") };
        var eco = EconomyManager.Instance;
        if (eco != null) foreach (var c in eco.AvailableFieldCrops()) items.Add(("Seeds", "seed_" + c.id));   // this map's crops only
        if (eco != null) foreach (var a in eco.Data.animals) items.Add(("Animals", a.id));
        items.Add(("Upgrades", "capacity"));
        items.Add(("Upgrades", "growth"));
        foreach (var e in ShopCatalog.Entries) items.Add((string.IsNullOrEmpty(e.category) ? "Other" : e.category, e.id));
        return items;
    }

    private void BuildPages()
    {
        pages.Clear();
        var order = new List<string>(); var byCat = new Dictionary<string, List<string>>();
        foreach (var item in Items())
        {
            string cat = item.category, id = item.id;
            if (!byCat.ContainsKey(cat)) { byCat[cat] = new List<string>(); order.Add(cat); }
            if (!byCat[cat].Contains(id)) byCat[cat].Add(id);
        }
        foreach (var cat in order)
            for (int i = 0; i < byCat[cat].Count; i += PageSize)
                pages.Add((cat, byCat[cat].GetRange(i, Mathf.Min(PageSize, byCat[cat].Count - i))));
        page = Mathf.Clamp(page, 0, Mathf.Max(0, pages.Count - 1));
    }

    private List<string> PageIds => page < pages.Count ? pages[page].ids : new List<string>();

    private void Build()
    {
        var go = new GameObject("Shop UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.transform.SetParent(transform, false);
        canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 60;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        panel = NewRect("Panel", canvas.transform, typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(760, 0);
        panel.GetComponent<Image>().color = new Color(0.07f, 0.06f, 0.05f, 0.94f);
        var vl = panel.GetComponent<VerticalLayoutGroup>();
        vl.padding = new RectOffset(28, 28, 22, 22); vl.spacing = 10;
        vl.childControlWidth = vl.childControlHeight = true; vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
        panel.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        title = NewText(panel, 34, new Color(1f, 0.55f, 0.15f));
        list = NewRect("List", panel, typeof(VerticalLayoutGroup));
        var ll = list.GetComponent<VerticalLayoutGroup>();
        ll.spacing = 8; ll.childControlWidth = ll.childControlHeight = true; ll.childForceExpandWidth = true; ll.childForceExpandHeight = false;
        footer = NewText(panel, 20, new Color(0.8f, 0.78f, 0.74f));

        for (int n = 0; n < PageSize; n++)
        {
            var row = NewRect("Row " + n, list, typeof(Image), typeof(Button), typeof(LayoutElement));
            row.GetComponent<LayoutElement>().minHeight = 48;
            row.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.06f);
            var label = NewText(row, 22, Color.white);
            var lrt = label.rectTransform;
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.offsetMin = new Vector2(16, 6); lrt.offsetMax = new Vector2(-16, -6);
            label.alignment = TextAlignmentOptions.MidlineLeft;
            int captured = n;
            row.GetComponent<Button>().onClick.AddListener(() => BuySlot(captured));
            rows.Add((row.GetComponent<Button>(), label));
        }
        canvas.gameObject.SetActive(false);
    }

    private static RectTransform NewRect(string name, Transform parent, params System.Type[] comps)
    {
        var all = new List<System.Type> { typeof(RectTransform) }; all.AddRange(comps);
        var go = new GameObject(name, all.ToArray());
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static TextMeshProUGUI NewText(Transform parent, float size, Color c)
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<TextMeshProUGUI>();
        t.fontSize = size; t.color = c; t.richText = true; t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.Normal;
        Loc.ExcludeFromScene(t);
        return t;
    }

    public void Open()
    {
        if (IsOpen || canvas == null) return;
        IsOpen = true;
        openedFrame = Time.frameCount;
        selectedRow = 0;
        canvas.gameObject.SetActive(true);
        if (UIManager.Instance != null) UIManager.Instance.SetExternalPanel(true);
        BuildPages();
        Refresh();
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        canvas.gameObject.SetActive(false);
        if (UIManager.Instance != null) UIManager.Instance.SetExternalPanel(false);
    }

    private void Update()
    {
        if (!IsOpen || Time.timeScale == 0f) return;
        if (Time.frameCount == openedFrame) return;
        var pad = Gamepad.current;
        bool padConfirm = pad != null && pad.buttonSouth.wasPressedThisFrame;
        // Esc / Tab / B close; E closes too (on a gamepad A buys instead).
        if (MenuNav.BackOrTab() || (GameInput.Pressed(GameAction.Interact) && !padConfirm)) { Close(); return; }
        int digit = MenuNav.Digit();
        if (digit >= 0 && digit < rows.Count) { selectedRow = digit; BuySlot(digit); }
        int ids = PageIds.Count;
        int v = MenuNav.Vertical();
        if (v != 0 && ids > 0) { selectedRow = (selectedRow + v + ids) % ids; Refresh(); }
        if (MenuNav.Confirm()) BuySlot(selectedRow);
        if (pages.Count > 1)
        {
            int h = MenuNav.Horizontal();
            if (MenuNav.PageNext()) h = 1;
            if (MenuNav.PagePrev()) h = -1;
            if (h != 0) { page = (page + h + pages.Count) % pages.Count; selectedRow = 0; Refresh(); }
        }
    }

    /// <summary>Change page (tests, gamepad).</summary>
    public void TurnPage(int dir)
    {
        if (pages.Count == 0) return;
        page = (page + dir + pages.Count) % pages.Count;
        selectedRow = 0;
        Refresh();
    }

    public int Page => page;
    public int PageCount => pages.Count;
    public string CurrentCategory => page < pages.Count ? pages[page].category : "";

    private void BuySlot(int slot) { var ids = PageIds; if (slot >= 0 && slot < ids.Count) Buy(ids[slot]); }

    /// <summary>Show the page that holds this category (e.g. a feature's own stall opening its items).</summary>
    public void OpenCategory(string category)
    {
        Open(); BuildPages();
        int i = pages.FindIndex(p => p.category == category);
        if (i >= 0) { page = i; Refresh(); }
    }

    private void Buy(string id)
    {
        var eco = EconomyManager.Instance;
        if (eco == null) return;
        if (eco.TryBuy(id))
        {
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX();
            if (UIManager.Instance != null) UIManager.Instance.ShowAlertMessage(Loc.Display(Loc.Raw("Bought") + ": " + Name(id)));
        }
        else if (UIManager.Instance != null)
        {
            var a = eco.Animal(id);
            var extra = a == null ? ShopCatalog.Find(id) : null;
            string block = extra != null && extra.blockReason != null ? extra.blockReason() : null;
            string why = !string.IsNullOrEmpty(block) ? block : eco.PriceOf(id) < 0 ? "Fully upgraded" : (a != null && eco.Owned(id) >= a.max) ? "The pen is full for this animal" : "Not enough money";
            UIManager.Instance.ShowAlertMessage(Loc.T(why));
        }
        Refresh();
    }

    private string Name(string id)
    {
        var eco = EconomyManager.Instance;
        if (id == "feed") return Loc.IsArabic ? eco.Data.feed.nameAr : eco.Data.feed.nameEn;
        if (id == "capacity") return Loc.Raw("Bigger truck bed");
        if (id == "growth") return Loc.Raw("Faster growth");
        if (id.StartsWith("seed_")) return Loc.Raw("Seeds") + ": " + FieldCropText.Name(eco.FieldCrop(id.Substring(5)));
        var animal = eco.Animal(id);
        if (animal != null) return EconomyManager.AnimalName(animal);
        var extra = ShopCatalog.Find(id);
        return extra != null && extra.name != null ? extra.name() : id;
    }

    private void Refresh()
    {
        var eco = EconomyManager.Instance;
        if (eco == null || title == null) return;
        foreach (var t in new[] { title, footer }) Loc.ApplyFont(t);
        string cat = page < pages.Count ? pages[page].category : "";
        title.text = Loc.Display(Loc.Raw("Farm Shop") + (cat != "" ? " - " + Loc.Raw(cat) : "") + (pages.Count > 1 ? "  (" + (page + 1) + "/" + pages.Count + ")" : ""));
        title.alignment = Loc.IsArabic ? TextAlignmentOptions.Right : TextAlignmentOptions.Left;
        var ids = PageIds;
        for (int i = 0; i < rows.Count; i++)
        {
            var (btn, label) = rows[i];
            btn.gameObject.SetActive(i < ids.Count);
            if (i >= ids.Count) continue;
            string id = ids[i];
            Loc.ApplyFont(label);
            int price = eco.PriceOf(id);
            string detail = "";
            var a = eco.Animal(id);
            var extra = a == null ? ShopCatalog.Find(id) : null;
            if (a != null) detail = " (" + eco.Owned(id) + "/" + a.max + ")";
            else if (id == "feed") detail = " (" + eco.Feed.ToString("F1") + ")";
            else if (id.StartsWith("seed_")) detail = " (" + eco.Seeds(id.Substring(5)) + ")";
            else if (id == "capacity") detail = " (" + eco.TruckCapacity.ToString("F0") + " kg)";
            else if (id == "growth") detail = " (x" + EconomyManager.GrowthSpeed.ToString("F2") + ")";
            else if (extra != null && extra.detail != null) detail = " " + extra.detail();
            string priceText = price < 0 ? Loc.Raw("Fully upgraded") : price.ToString("N0") + " " + Loc.Raw("SAR");
            label.text = "<color=#F2A93B>" + ((i + 1) % 10) + "</color>   " + Loc.Display(Name(id) + detail + "   " + priceText);
            label.alignment = Loc.IsArabic ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft;
            bool affordable = price >= 0 && eco.Money >= price;
            btn.interactable = affordable;
            label.color = affordable ? Color.white : new Color(1f, 1f, 1f, 0.45f);
            btn.GetComponent<Image>().color = i == selectedRow ? new Color(1f, 0.55f, 0.15f, 0.3f) : new Color(1f, 1f, 1f, 0.06f);
        }
        string keys = GameInput.UsingGamepad
            ? Glyphs.Apply(UiText.T(pages.Count > 1 ? "[Enter] Buy   [LB] [RB] Page   [Esc] Close" : "[Enter] Buy   [Esc] Close"))
            : Loc.T(pages.Count > 1 ? "[0-9] or click to buy   [<- ->] page   [Esc] close" : "[0-9] or click to buy   [Esc] close");
        footer.text = Loc.T("Money: {0} SAR   Feed: {1}", eco.Money.ToString("N0"), eco.Feed.ToString("F1")) + "\n" + keys;
        footer.alignment = title.alignment;
    }
}
