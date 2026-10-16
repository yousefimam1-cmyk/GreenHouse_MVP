using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Small helpers to build screen-space UI from code (canvas, panels, texts, buttons).</summary>
public static class MenuKit
{
    public static readonly Color PanelColor = new Color(0.07f, 0.06f, 0.05f, 0.95f);
    public static readonly Color RowColor = new Color(1f, 1f, 1f, 0.06f);
    public static readonly Color RowSelected = new Color(1f, 0.55f, 0.15f, 0.35f);
    public static readonly Color Orange = new Color(1f, 0.55f, 0.15f);

    public static Canvas Canvas(string name, Transform parent, int order)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.transform.SetParent(parent, false);
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = order;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        return canvas;
    }

    public static RectTransform Rect(string name, Transform parent, params Type[] comps)
    {
        var all = new List<Type> { typeof(RectTransform) }; all.AddRange(comps);
        var go = new GameObject(name, all.ToArray());
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    public static void Stretch(RectTransform rt, float inset = 0f)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(inset, inset); rt.offsetMax = new Vector2(-inset, -inset);
    }

    /// <summary>Full-screen dark backdrop that also blocks clicks to the world behind.</summary>
    public static Image Backdrop(Transform parent, float alpha)
    {
        var rt = Rect("Backdrop", parent, typeof(Image));
        Stretch(rt);
        var img = rt.GetComponent<Image>();
        img.color = new Color(0.02f, 0.02f, 0.015f, alpha);
        img.raycastTarget = true;
        return img;
    }

    /// <summary>Centered auto-height panel with a vertical layout.</summary>
    public static RectTransform Panel(string name, Transform parent, float width)
    {
        var rt = Rect(name, parent, typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(width, 0);
        rt.GetComponent<Image>().color = PanelColor;
        var vl = rt.GetComponent<VerticalLayoutGroup>();
        vl.padding = new RectOffset(28, 28, 22, 22); vl.spacing = 8;
        vl.childControlWidth = vl.childControlHeight = true; vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
        rt.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return rt;
    }

    public static TextMeshProUGUI Text(Transform parent, float size, Color color, string name = "Text")
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<TextMeshProUGUI>();
        t.fontSize = size; t.color = color; t.richText = true; t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.Normal;
        Loc.ExcludeFromScene(t);
        return t;
    }

    /// <summary>A clickable row (Image + MenuClick).</summary>
    public static MenuClick Button(Transform parent, string name, Action onClick, out Image bg)
    {
        var rt = Rect(name, parent, typeof(Image), typeof(MenuClick));
        bg = rt.GetComponent<Image>();
        bg.color = RowColor;
        bg.raycastTarget = true;
        var click = rt.GetComponent<MenuClick>();
        click.onClick = onClick;
        return click;
    }

    public static string Title(string s) => "<color=#FF9A3C><b>" + s + "</b></color>";
}

/// <summary>One line of a code-built menu: a button, or a setting changed with left / right.</summary>
public class MenuRow
{
    public Func<string> label;          // already translated
    public Func<string> value;          // optional (shown on the other side), already translated
    public Action confirm;              // Enter / A / click
    public Action<int> adjust;          // Left / Right (-1 / +1); also the < > buttons
    public Func<bool> enabled;          // greyed out and skipped when false

    public static MenuRow Button(Func<string> label, Action confirm) => new MenuRow { label = label, confirm = confirm };
    public static MenuRow Setting(Func<string> label, Func<string> value, Action<int> adjust) => new MenuRow { label = label, value = value, adjust = adjust, confirm = () => adjust(1) };

    internal RectTransform rt;
    internal Image bg;
    internal TextMeshProUGUI text, valueText;
    internal MenuClick button;
    internal RectTransform minus, plus;
    public bool IsEnabled => enabled == null || enabled();
}

/// <summary>
/// A navigable list panel (title, rows, footer) used by the main / pause / settings menus.
/// Up / Down (arrows, W S, D-pad, stick) select, Left / Right adjust, Enter / A confirm, number keys pick a row.
/// </summary>
public class MenuList
{
    public const int MaxVisible = 12;
    public RectTransform Panel { get; private set; }
    public int Selected { get; private set; }
    public IReadOnlyList<MenuRow> Rows => rows;

    private readonly TextMeshProUGUI title, footer;
    private readonly RectTransform list;
    private readonly List<MenuRow> rows = new List<MenuRow>();
    private Func<string> titleText, footerText;

    public MenuList(Transform parent, float width)
    {
        Panel = MenuKit.Panel("Menu", parent, width);
        title = MenuKit.Text(Panel, 36, MenuKit.Orange, "Title");
        list = MenuKit.Rect("Rows", Panel, typeof(VerticalLayoutGroup));
        var ll = list.GetComponent<VerticalLayoutGroup>();
        ll.spacing = 6; ll.childControlWidth = ll.childControlHeight = true; ll.childForceExpandWidth = true; ll.childForceExpandHeight = false;
        footer = MenuKit.Text(Panel, 19, new Color(0.8f, 0.78f, 0.74f), "Footer");
    }

    public void Show(Func<string> titleText, List<MenuRow> newRows, Func<string> footerText, int select = 0)
    {
        this.titleText = titleText; this.footerText = footerText;
        foreach (var r in rows) if (r.rt != null) UnityEngine.Object.Destroy(r.rt.gameObject);
        rows.Clear();
        rows.AddRange(newRows);
        foreach (var r in rows) BuildRow(r);
        Selected = Mathf.Clamp(select, 0, Mathf.Max(0, rows.Count - 1));
        if (rows.Count > 0 && !rows[Selected].IsEnabled) Move(1);
        Refresh();
    }

    private void BuildRow(MenuRow r)
    {
        int index = rows.IndexOf(r);
        r.button = MenuKit.Button(list, "Row " + index, () => { Selected = rows.IndexOf(r); if (r.IsEnabled && r.confirm != null) r.confirm(); Refresh(); }, out r.bg);
        r.button.onHover = () => { int i = rows.IndexOf(r); if (i >= 0 && i != Selected && r.IsEnabled) { Selected = i; Refresh(); } };
        r.rt = (RectTransform)r.button.transform;
        r.rt.gameObject.AddComponent<LayoutElement>().minHeight = r.label != null && r.label().Contains("\n") ? 84 : 50;   // two-line rows (map / start choice)
        r.text = MenuKit.Text(r.rt, 24, Color.white, "Label");
        if (r.value != null || r.adjust != null) r.valueText = MenuKit.Text(r.rt, 24, new Color(1f, 0.85f, 0.6f), "Value");
        if (r.adjust != null)
        {
            r.minus = SmallButton(r.rt, "<", () => { Selected = rows.IndexOf(r); r.adjust(-1); Refresh(); });
            r.plus = SmallButton(r.rt, ">", () => { Selected = rows.IndexOf(r); r.adjust(1); Refresh(); });
        }
    }

    private static RectTransform SmallButton(RectTransform row, string label, Action click)
    {
        Image bg;
        var b = MenuKit.Button(row, "Adjust " + label, click, out bg);
        bg.color = new Color(1f, 1f, 1f, 0.12f);
        var rt = (RectTransform)b.transform;
        var t = MenuKit.Text(rt, 24, Color.white);
        MenuKit.Stretch(t.rectTransform);
        t.alignment = TextAlignmentOptions.Center;
        t.text = label;
        return rt;
    }

    /// <summary>Re-translate and lay out (call after a language change or value change).</summary>
    public void Refresh()
    {
        bool rtl = Loc.IsArabic;
        Loc.ApplyFont(title); Loc.ApplyFont(footer);
        title.text = titleText != null ? titleText() : "";
        title.alignment = rtl ? TextAlignmentOptions.Right : TextAlignmentOptions.Left;
        footer.text = footerText != null ? Glyphs.Apply(footerText()) : "";
        footer.alignment = title.alignment;
        // Long lists (achievements, controls) show a window of rows around the selection, so they always fit on screen
        // and scroll with the keyboard / D-pad / stick.
        int first = rows.Count > MaxVisible ? Mathf.Clamp(Selected - MaxVisible / 2, 0, rows.Count - MaxVisible) : 0;
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            bool visible = i >= first && i < first + MaxVisible;
            if (r.rt != null && r.rt.gameObject.activeSelf != visible) r.rt.gameObject.SetActive(visible);
            bool on = r.IsEnabled;
            Loc.ApplyFont(r.text);
            string num = i < 9 ? "<color=#FF9A3C>" + (i + 1) + "</color>" : "";
            string lbl = r.label != null ? r.label() : "";
            r.text.text = num == "" ? lbl : rtl ? lbl + "   " + num : num + "   " + lbl;
            r.text.color = on ? Color.white : new Color(1f, 1f, 1f, 0.4f);
            r.bg.color = i == Selected ? MenuKit.RowSelected : MenuKit.RowColor;
            var lrt = r.text.rectTransform;
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            if (r.valueText == null) { lrt.offsetMin = new Vector2(16, 4); lrt.offsetMax = new Vector2(-16, -4); }
            else
            {
                // label on the reading side, [<  value  >] on the other side
                lrt.offsetMin = new Vector2(rtl ? 330 : 16, 4); lrt.offsetMax = new Vector2(rtl ? -16 : -330, -4);
                Loc.ApplyFont(r.valueText);
                r.valueText.text = r.value != null ? r.value() : "";
                r.valueText.alignment = TextAlignmentOptions.Center;
                var vrt = r.valueText.rectTransform;
                float side = rtl ? 0f : 1f;
                vrt.anchorMin = new Vector2(side, 0); vrt.anchorMax = new Vector2(side, 1); vrt.pivot = new Vector2(side, 0.5f);
                vrt.sizeDelta = new Vector2(220, 0); vrt.anchoredPosition = new Vector2(rtl ? 55 : -55, 0);
                if (r.minus != null) Place(r.minus, side, rtl ? 6 : -270);
                if (r.plus != null) Place(r.plus, side, rtl ? 270 : -6);
            }
            r.text.alignment = rtl ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft;
        }
    }

    private static void Place(RectTransform b, float side, float x)
    {
        b.anchorMin = new Vector2(side, 0.5f); b.anchorMax = new Vector2(side, 0.5f);
        b.pivot = new Vector2(x > 0 ? 0f : 1f, 0.5f);
        b.sizeDelta = new Vector2(44, 40);
        b.anchoredPosition = new Vector2(x, 0);
    }

    private void Move(int dir)
    {
        if (rows.Count == 0) return;
        for (int n = 0; n < rows.Count; n++)
        {
            Selected = (Selected + dir + rows.Count) % rows.Count;
            if (rows[Selected].IsEnabled) return;
        }
    }

    /// <summary>Handle navigation for this frame. Returns true when something changed.</summary>
    public bool HandleInput()
    {
        if (rows.Count == 0) return false;
        int v = MenuNav.Vertical();
        if (v != 0) { Move(v); Refresh(); return true; }
        var r = rows[Selected];
        int h = MenuNav.Horizontal();
        if (h != 0 && r.adjust != null && r.IsEnabled) { r.adjust(h); Refresh(); return true; }
        int d = MenuNav.Digit();
        if (d >= 0 && d < rows.Count && d < 9 && rows[d].IsEnabled) { Selected = d; Refresh(); if (rows[d].confirm != null) rows[d].confirm(); Refresh(); return true; }
        if (MenuNav.Confirm() && r.IsEnabled && r.confirm != null) { r.confirm(); Refresh(); return true; }
        return false;
    }

    /// <summary>Simulate choosing a row (tests, mouse).</summary>
    public void Choose(int index)
    {
        if (index < 0 || index >= rows.Count || !rows[index].IsEnabled) return;
        Selected = index;
        if (rows[index].confirm != null) rows[index].confirm();
        Refresh();
    }
}
