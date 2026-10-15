using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Small text panel built in code (title, lines, footer) used by the cozy features (look, quests, book, learn).
/// Lines are passed already translated (Loc.Raw) and shaped here line by line; only one cozy panel is open at a time.
/// </summary>
public class CozyPanel
{
    private static readonly List<CozyPanel> all = new List<CozyPanel>();

    public bool IsOpen { get; private set; }
    public int OpenedFrame { get; private set; }
    /// <summary>Last shown text (unshaped), for tests and logs.</summary>
    public string LastText { get; private set; } = "";

    private readonly GameObject root;
    private readonly TextMeshProUGUI title, body, footer;

    public CozyPanel(Transform host, string name, float width = 780f)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.transform.SetParent(host, false);
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 61;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        var panel = NewRect("Panel", go.transform, typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(width, 0);
        panel.GetComponent<Image>().color = new Color(0.08f, 0.06f, 0.04f, 0.94f);
        var vl = panel.GetComponent<VerticalLayoutGroup>();
        vl.padding = new RectOffset(28, 28, 22, 22); vl.spacing = 12;
        vl.childControlWidth = vl.childControlHeight = true; vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
        panel.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        title = NewText(panel, 32, new Color(0.98f, 0.78f, 0.35f));
        body = NewText(panel, 22, Color.white);
        footer = NewText(panel, 19, new Color(0.8f, 0.78f, 0.72f));
        root = go;
        root.SetActive(false);
        all.Add(this);
    }

    private static RectTransform NewRect(string name, Transform parent, params System.Type[] comps)
    {
        var list = new List<System.Type> { typeof(RectTransform) }; list.AddRange(comps);
        var go = new GameObject(name, list.ToArray());
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static TextMeshProUGUI NewText(Transform parent, float size, Color c)
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<TextMeshProUGUI>();
        t.fontSize = size; t.color = c; t.richText = false; t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.Normal;
        Loc.ExcludeFromScene(t);
        return t;
    }

    public void Open()
    {
        if (IsOpen || root == null) return;
        all.RemoveAll(p => p.root == null);
        foreach (var p in all.ToArray()) if (p != this && p.IsOpen) p.Close();
        IsOpen = true;
        OpenedFrame = Time.frameCount;
        root.SetActive(true);
        if (UIManager.Instance != null) UIManager.Instance.SetExternalPanel(true);
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        if (root != null) root.SetActive(false);
        if (UIManager.Instance != null) UIManager.Instance.SetExternalPanel(false);
    }

    /// <summary>Esc / Tab pressed (not on the frame the panel opened).</summary>
    public bool CloseRequested => IsOpen && Time.frameCount != OpenedFrame && CozyKeys.Down("esc");

    /// <summary>Show translated (unshaped) text.</summary>
    public void Show(string titleText, List<string> lines, string footerText)
    {
        LastText = titleText + "\n" + string.Join("\n", lines.ToArray()) + "\n" + footerText;
        if (title == null) return;
        foreach (var t in new[] { title, body, footer }) Loc.ApplyFont(t);
        title.text = Loc.Display(titleText);
        var shaped = new List<string>();
        foreach (var l in lines) shaped.Add(Loc.Display(l));
        body.text = string.Join("\n", shaped.ToArray());
        footer.text = Loc.Display(footerText);
        var a = Loc.IsArabic ? TextAlignmentOptions.TopRight : TextAlignmentOptions.TopLeft;
        title.alignment = body.alignment = footer.alignment = a;
    }
}
