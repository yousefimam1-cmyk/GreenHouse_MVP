using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Screen-space canvas with one rich-text label, built in code (tool wheel HUD, records panel).</summary>
public static class LifeCanvas
{
    public static TextMeshProUGUI Make(Transform host, string name, int sorting, Vector2 anchor, Vector2 size, float fontSize, bool backdrop, out GameObject root)
    {
        root = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        root.transform.SetParent(host, false);
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sorting;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        var panelGo = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        panelGo.transform.SetParent(root.transform, false);
        var panel = (RectTransform)panelGo.transform;
        panel.anchorMin = panel.anchorMax = panel.pivot = anchor;
        panel.sizeDelta = size;
        var img = panelGo.GetComponent<Image>();
        img.color = backdrop ? UiTheme.GlassStrong : new Color(0f, 0f, 0f, 0.35f);
        img.raycastTarget = false;
        var textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(panel, false);
        var tr = (RectTransform)textGo.transform;
        tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = new Vector2(24, 14); tr.offsetMax = new Vector2(-24, -14);
        var t = textGo.GetComponent<TextMeshProUGUI>();
        t.fontSize = fontSize; t.color = Color.white; t.richText = true; t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.Normal;
        Loc.ExcludeFromScene(t);
        return t;
    }
}

/// <summary>
/// Animal records for the pen (opened at the records board): every animal with age, sex, health, illness and pregnancy,
/// the selected one in detail. Up / Down (D-pad, stick, arrows) select, Enter / A gives medicine, R / X sells
/// (press twice), Q / Y calls the vet, Esc / B closes. Prompts follow the device in use.
/// </summary>
public class HerdPanel
{
    public bool IsOpen { get; private set; }
    public int Selected { get; private set; }
    public string LastText { get; private set; } = "";
    private readonly GameObject root;
    private readonly TextMeshProUGUI text;
    private int openedFrame = -1, sellArmed = -1;
    private float refresh;
    private string message = "";

    public HerdPanel(Transform host)
    {
        text = LifeCanvas.Make(host, "Animal Records", 62, new Vector2(0.5f, 0.5f), new Vector2(1100f, 760f), 22f, true, out root);
        root.SetActive(false);
    }

    public void Open(HerdManager h)
    {
        if (IsOpen) return;
        IsOpen = true; openedFrame = Time.frameCount; sellArmed = -1; message = "";
        Selected = Mathf.Clamp(Selected, 0, Mathf.Max(0, h.Animals.Count - 1));
        if (root != null) root.SetActive(true);
        if (UIManager.Instance != null) UIManager.Instance.SetExternalPanel(true);
        Refresh(h);
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        if (root != null) root.SetActive(false);
        if (UIManager.Instance != null) UIManager.Instance.SetExternalPanel(false);
    }

    public void Select(HerdManager h, int i) { Selected = Mathf.Clamp(i, 0, Mathf.Max(0, h.Animals.Count - 1)); sellArmed = -1; Refresh(h); }

    public void Tick(HerdManager h)
    {
        if (Time.frameCount == openedFrame) return;
        if (MenuNav.BackOrTab()) { Close(); return; }
        int v = MenuNav.Vertical();
        if (v != 0 && h.Animals.Count > 0) { Select(h, (Selected + v + h.Animals.Count) % h.Animals.Count); return; }
        var a = Current(h);
        if (MenuNav.Confirm()) { message = h.Treat(a); sellArmed = -1; Refresh(h); return; }
        if (GameInput.Pressed(GameAction.VehicleAction) && a != null) { SellPressed(h, a); return; }
        if (GameInput.Pressed(GameAction.Implement)) { message = h.CallVet(); Refresh(h); return; }
        refresh -= Time.unscaledDeltaTime;
        if (refresh <= 0f) Refresh(h);
    }

    /// <summary>First press asks, second press sells.</summary>
    public void SellPressed(HerdManager h, AnimalRecord a)
    {
        if (sellArmed != a.uid) { sellArmed = a.uid; message = Loc.Display(Loc.Raw("Press [R] again to sell for") + " " + h.ValueOf(a).ToString("N0") + " " + Loc.Raw("SAR")); Refresh(h); return; }
        int got = h.Sell(a);
        sellArmed = -1;
        message = Loc.Display(Loc.Raw("Sold for") + " " + got.ToString("N0") + " " + Loc.Raw("SAR"));
        Selected = Mathf.Clamp(Selected, 0, Mathf.Max(0, h.Animals.Count - 1));
        Refresh(h);
    }

    private AnimalRecord Current(HerdManager h) => h.Animals.Count == 0 ? null : h.Animals[Mathf.Clamp(Selected, 0, h.Animals.Count - 1)];

    private readonly List<string> raw = new List<string>();     // translated, unshaped (tests / logs)
    private readonly List<string> shown = new List<string>();   // shaped, glyphs applied, colour tags outside the shaped text

    private void Add(string line, string color, string size = null)
    {
        raw.Add(line);
        string t = Glyphs.Apply(Loc.Display(line));
        if (color != null) t = "<color=" + color + ">" + t + "</color>";
        if (size != null) t = "<size=" + size + ">" + t + "</size>";
        shown.Add(t);
    }

    public void Refresh(HerdManager h)
    {
        refresh = 0.5f;
        raw.Clear(); shown.Clear();
        float load = h.HeatLoadToday;
        Add(Loc.Raw("Animal records") + " (" + h.Animals.Count + ")", "#FAC75A", "130%");
        Add(Loc.Raw("Medicine") + " " + h.Medicine + "   " + Loc.Raw("Shade") + ": " + Loc.Raw(h.Shade ? "yes" : "no") + "   "
            + Loc.Raw("Misting fans") + ": " + Loc.Raw(h.Fans ? (h.FansWorking ? "on" : "no water") : "no") + "   " + Loc.Raw("Heat today") + " " + Mathf.RoundToInt(load * 100f) + "%", "#BBD7FF");
        if (h.Animals.Count == 0) Add(Loc.Raw("No animals yet - buy them at the Farm Shop"), null);
        int start = Mathf.Clamp(Selected - 4, 0, Mathf.Max(0, h.Animals.Count - 9));
        for (int i = start; i < Mathf.Min(h.Animals.Count, start + 9); i++)
        {
            var a = h.Animals[i];
            Add((i == Selected ? "> " : "  ") + h.ShortLine(a), i == Selected ? "#FAC75A" : a.Sick ? "#FF8A7A" : a.Pregnant ? "#F5B8E0" : "#FFFFFF");
        }
        var cur = Current(h);
        if (cur != null) { Add("", null); foreach (var l in h.DetailLines(cur)) Add(l, "#D8F0C0"); }
        if (!string.IsNullOrEmpty(message)) { Add("", null); raw.Add(message); shown.Add("<color=#FFE08A>" + Glyphs.Apply(message) + "</color>"); }
        Add("", null);
        Add(Loc.Raw("[Enter] Give medicine   [R] Sell   [Q] Call the vet") + " (" + HerdManager.VetPrice + " " + Loc.Raw("SAR") + ")   " + Loc.Raw("[Esc] Close"), "#CCC8BE", "85%");

        LastText = string.Join("\n", raw.ToArray());
        if (text == null) return;
        Loc.ApplyFont(text);
        text.text = string.Join("\n", shown.ToArray());
        text.alignment = Loc.IsArabic ? TextAlignmentOptions.TopRight : TextAlignmentOptions.TopLeft;
    }
}

/// <summary>Bottom-center on-foot HUD: thirst bar, carried load and the tool wheel (bumpers / , . cycle).</summary>
public class LifeHud
{
    private readonly GameObject root;
    private readonly TextMeshProUGUI text;
    public string LastText { get; private set; } = "";

    public LifeHud(Transform host)
    {
        text = LifeCanvas.Make(host, "On-foot HUD", 40, new Vector2(0.5f, 0.02f), new Vector2(980f, 120f), 21f, false, out root);
    }

    public void SetVisible(bool on) { if (root != null && root.activeSelf != on) root.SetActive(on); }

    private static string Bar(float v, string color)
    {
        int n = Mathf.RoundToInt(Mathf.Clamp01(v) * 10f);
        return "<color=" + color + ">" + new string('■', n) + "</color><color=#555555>" + new string('■', 10 - n) + "</color>";
    }

    /// <summary>wheelOpen: show the whole ring with the current tool in the middle (after cycling).</summary>
    public void Show(StaminaModel m, HandTool tool, bool wheelOpen, float carryKg, float canWater)
    {
        var sb = new System.Text.StringBuilder();
        if (wheelOpen)
        {
            var parts = new List<string>();
            int i = System.Array.IndexOf(HandTools.All, tool);
            for (int k = -2; k <= 2; k++)
            {
                var t = HandTools.All[((i + k) % HandTools.All.Length + HandTools.All.Length) % HandTools.All.Length];
                string n = Loc.T(HandTools.Key(t));
                parts.Add(k == 0 ? "<size=125%><color=#FAC75A>( " + n + " )</color></size>" : "<color=#BBBBBB>" + n + "</color>");
            }
            sb.Append("[,] ‹  ").Append(string.Join("  ·  ", parts.ToArray())).Append("  › [.]\n");
        }
        else
        {
            sb.Append("<color=#FAC75A>").Append(Loc.T(HandTools.Key(tool))).Append("</color>");
            if (tool == HandTool.WateringCan) sb.Append(" ").Append(Mathf.RoundToInt(canWater * 100f)).Append("%");
            if (carryKg > 0f) sb.Append("  (").Append(carryKg.ToString("F0")).Append(" kg)");
            sb.Append('\n');
        }
        sb.Append(Loc.T("Water")).Append(' ').Append(Bar(m.hydration, m.hydration < 0.25f ? "#FF6B6B" : "#5BA8FF"));
        LastText = sb.ToString();
        if (text == null) return;
        Loc.ApplyFont(text);
        text.alignment = TextAlignmentOptions.Center;
        text.text = Glyphs.Apply(LastText);
    }
}
