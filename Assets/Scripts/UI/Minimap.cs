using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Round minimap at the top centre of the HUD: an orthographic camera looks straight down (UI layer culled) into a
/// RenderTexture, turning with the player's view; icons are drawn as UI on top (player arrow, vehicles, greenhouses,
/// field plots, pen, shop stalls, loading bay, market, current objective, plus any MinimapIcon a feature adds).
/// N (View / Share on a gamepad) opens a full-screen north-up map with a legend.
/// </summary>
[MazraaSystem(50)]
public class Minimap : MonoBehaviour
{
    public static Minimap Instance { get; private set; }

    public float miniRange = 45f;          // metres from centre to the rim
    public float miniSize = 240f;          // pixels (diameter)
    public float fullSize = 880f;          // pixels
    public int textureSize = 512;

    public bool FullOpen { get; private set; }
    public Camera MapCamera => mapCam;
    public float ViewYaw { get; private set; }
    public Vector3 Center { get; private set; }
    public float FullRange { get; private set; } = 150f;

    private Camera mapCam;
    private RenderTexture rt;
    private Canvas canvas;
    private RectTransform miniRoot, miniIcons, fullRoot, fullIcons, miniArrow, fullArrow, northTag, fullFrame;
    private TextMeshProUGUI fullTitle, fullFooter, legendText;
    private readonly List<RectTransform> miniPool = new List<RectTransform>(), fullPool = new List<RectTransform>();
    private RectTransform miniObjective, fullObjective;
    private Texture2D circleTex;
    private float scanTimer;
    private int openedFrame;

    private static readonly Color Sand = new Color(0.78f, 0.68f, 0.5f);

    private void Awake() => Instance = this;
    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        Loc.OnLanguageChanged -= RefreshTexts;
        if (rt != null) rt.Release();
    }

    private void Start()
    {
        BuildCamera();
        BuildUI();
        Loc.OnLanguageChanged += RefreshTexts;
        RefreshTexts();
        scanTimer = 0.5f;
    }

    // ------------------------------------------------------------------ build

    private void BuildCamera()
    {
        rt = new RenderTexture(textureSize, textureSize, 16);
        rt.name = "Minimap RT";
        var go = new GameObject("Minimap Camera");
        go.transform.SetParent(transform, false);
        mapCam = go.AddComponent<Camera>();
        mapCam.orthographic = true;
        mapCam.orthographicSize = miniRange;
        mapCam.nearClipPlane = 1f;
        mapCam.farClipPlane = 400f;
        mapCam.clearFlags = CameraClearFlags.SolidColor;
        mapCam.backgroundColor = Sand;
        mapCam.cullingMask = ~(1 << 5);        // everything but UI
        mapCam.depth = -10f;
        mapCam.targetTexture = rt;
        // Top-down map needs no shadows or post FX: saves a full shadow pass + SSAO/outlines every frame.
        var urp = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(mapCam);
        if (urp != null) { urp.renderShadows = false; urp.renderPostProcessing = false; }
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
    }

    /// <summary>White disc with soft edge (mask and frame for the round minimap).</summary>
    public static Texture2D CircleTexture(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var px = new Color[size * size];
        float r = size * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - r, dy = y + 0.5f - r;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(r - d));
            }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    private void BuildUI()
    {
        canvas = MenuKit.Canvas("Minimap UI", transform, 41);
        circleTex = CircleTexture(128);

        // Minimap: frame disc, masked disc with the map, icons, arrow.
        miniRoot = MenuKit.Rect("Minimap", canvas.transform);
        miniRoot.anchorMin = miniRoot.anchorMax = miniRoot.pivot = new Vector2(0.5f, 1f);
        miniRoot.anchoredPosition = new Vector2(0, -18);
        miniRoot.sizeDelta = new Vector2(miniSize, miniSize);
        var frame = MenuKit.Rect("Frame", miniRoot, typeof(RawImage));
        MenuKit.Stretch(frame, -6f);
        var frameImg = frame.GetComponent<RawImage>(); frameImg.texture = circleTex; frameImg.color = new Color(0.06f, 0.08f, 0.05f, 0.85f); frameImg.raycastTarget = false;
        var maskRt = MenuKit.Rect("Mask", miniRoot, typeof(RawImage), typeof(Mask));
        MenuKit.Stretch(maskRt);
        var maskImg = maskRt.GetComponent<RawImage>(); maskImg.texture = circleTex; maskImg.raycastTarget = false;
        maskRt.GetComponent<Mask>().showMaskGraphic = false;
        var map = MenuKit.Rect("Map", maskRt, typeof(RawImage));
        MenuKit.Stretch(map);
        var mapImg = map.GetComponent<RawImage>(); mapImg.texture = rt; mapImg.raycastTarget = false;
        miniIcons = MenuKit.Rect("Icons", miniRoot);
        MenuKit.Stretch(miniIcons);
        miniObjective = MakeIcon(miniIcons, new Color(1f, 0.5f, 0.1f), 16f, true);
        miniArrow = MakeArrow(miniRoot, 0.8f);
        northTag = MenuKit.Rect("North", miniRoot);
        northTag.sizeDelta = new Vector2(30, 30);
        var n = MenuKit.Text(northTag, 20, new Color(1f, 0.9f, 0.7f)); MenuKit.Stretch(n.rectTransform); n.alignment = TextAlignmentOptions.Center; n.text = "<b>N</b>";

        // Full map
        fullRoot = MenuKit.Rect("Full Map", canvas.transform);
        MenuKit.Stretch(fullRoot);
        MenuKit.Backdrop(fullRoot, 0.75f);
        fullFrame = MenuKit.Rect("Frame", fullRoot, typeof(Image));
        fullFrame.anchorMin = fullFrame.anchorMax = fullFrame.pivot = new Vector2(0.5f, 0.5f);
        fullFrame.sizeDelta = new Vector2(fullSize + 12, fullSize + 12);
        fullFrame.GetComponent<Image>().color = new Color(0.06f, 0.08f, 0.05f, 0.95f);
        var fmap = MenuKit.Rect("Map", fullFrame, typeof(RawImage));
        fmap.anchorMin = fmap.anchorMax = fmap.pivot = new Vector2(0.5f, 0.5f);
        fmap.sizeDelta = new Vector2(fullSize, fullSize);
        var fimg = fmap.GetComponent<RawImage>(); fimg.texture = rt; fimg.raycastTarget = false;
        fullIcons = MenuKit.Rect("Icons", fmap);
        MenuKit.Stretch(fullIcons);
        fullObjective = MakeIcon(fullIcons, new Color(1f, 0.5f, 0.1f), 22f, true);
        fullArrow = MakeArrow(fullIcons, 1.1f);
        fullTitle = MenuKit.Text(fullFrame, 34, MenuKit.Orange, "Title");
        var trt = fullTitle.rectTransform; trt.anchorMin = new Vector2(0, 1); trt.anchorMax = new Vector2(1, 1); trt.pivot = new Vector2(0.5f, 0); trt.sizeDelta = new Vector2(0, 50); trt.anchoredPosition = new Vector2(0, 8);
        fullFooter = MenuKit.Text(fullFrame, 20, new Color(0.85f, 0.82f, 0.76f), "Footer");
        var frt = fullFooter.rectTransform; frt.anchorMin = new Vector2(0, 0); frt.anchorMax = new Vector2(1, 0); frt.pivot = new Vector2(0.5f, 1); frt.sizeDelta = new Vector2(0, 40); frt.anchoredPosition = new Vector2(0, -8);
        var legend = MenuKit.Panel("Legend", fullRoot, 300);
        legend.anchorMin = legend.anchorMax = legend.pivot = new Vector2(1f, 0.5f);
        legend.anchoredPosition = new Vector2(-30, 0);
        legendText = MenuKit.Text(legend, 21, Color.white);
        fullRoot.gameObject.SetActive(false);
    }

    private static RectTransform MakeIcon(Transform parent, Color c, float size, bool diamond)
    {
        var rt = MenuKit.Rect("Icon", parent, typeof(Image));
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(size, size);
        var img = rt.GetComponent<Image>(); img.color = c; img.raycastTarget = false;
        if (diamond) rt.localRotation = Quaternion.Euler(0, 0, 45f);
        return rt;
    }

    // Chevron made of two bars, pointing up.
    private static RectTransform MakeArrow(Transform parent, float scale)
    {
        var holder = MenuKit.Rect("Player", parent);
        holder.anchorMin = holder.anchorMax = holder.pivot = new Vector2(0.5f, 0.5f);
        holder.sizeDelta = new Vector2(30, 30) * scale;
        foreach (float a in new[] { -35f, 35f })
        {
            var bar = MenuKit.Rect("Bar", holder, typeof(Image));
            bar.sizeDelta = new Vector2(6f, 22f) * scale;
            bar.anchoredPosition = new Vector2(a > 0 ? 5f : -5f, 0) * scale;
            bar.localRotation = Quaternion.Euler(0, 0, a);
            var img = bar.GetComponent<Image>(); img.color = new Color(1f, 1f, 1f, 0.95f); img.raycastTarget = false;
        }
        return holder;
    }

    private void RefreshTexts()
    {
        if (fullTitle == null) return;
        foreach (var t in new[] { fullTitle, fullFooter, legendText }) Loc.ApplyFont(t);
        fullTitle.alignment = TextAlignmentOptions.Center;
        fullFooter.alignment = TextAlignmentOptions.Center;
        legendText.alignment = Loc.IsArabic ? TextAlignmentOptions.TopRight : TextAlignmentOptions.TopLeft;
        fullTitle.text = "<b>" + UiText.T("Farm map") + "</b>";
        fullFooter.text = Glyphs.Apply(UiText.T("[N] or [Esc] close"));
        legendText.text = LegendText();
    }

    public static string LegendText()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(MenuKit.Title(UiText.T("Legend")));
        Entry(sb, new Color(1f, 1f, 1f), UiText.Raw("You"));
        Entry(sb, new Color(1f, 0.5f, 0.1f), UiText.Raw("Objective"));
        Entry(sb, MinimapIcon.DefaultColor(MinimapIconKind.Vehicle), UiText.Raw("Vehicles"));
        Entry(sb, MinimapIcon.DefaultColor(MinimapIconKind.Greenhouse), UiText.Raw("Greenhouses"));
        Entry(sb, MinimapIcon.DefaultColor(MinimapIconKind.Field), UiText.Raw("Field plots"));
        Entry(sb, MinimapIcon.DefaultColor(MinimapIconKind.Pen), UiText.Raw("Animal pen"));
        Entry(sb, MinimapIcon.DefaultColor(MinimapIconKind.Shop), UiText.Raw("Shops"));
        Entry(sb, MinimapIcon.DefaultColor(MinimapIconKind.LoadingBay), UiText.Raw("Loading bay"));
        Entry(sb, MinimapIcon.DefaultColor(MinimapIconKind.Market), UiText.Raw("Market"));
        return sb.ToString();
    }

    private static void Entry(System.Text.StringBuilder sb, Color c, string name)
    {
        string sq = "<color=#" + ColorUtility.ToHtmlStringRGB(c) + ">■</color>";
        sb.Append('\n').Append(Loc.IsArabic ? Loc.Display(name) + "  " + sq : sq + "  " + name);
    }

    // ------------------------------------------------------------------ icons

    /// <summary>Tag the farm's places and vehicles (runs every 2 s; features can add MinimapIcon themselves).</summary>
    public void ScanWorld()
    {
        foreach (var v in FindObjectsByType<Vehicle>(FindObjectsSortMode.None)) Tag(v, MinimapIconKind.Vehicle, 16f);
        foreach (var g in FindObjectsByType<GreenhouseManager>(FindObjectsSortMode.None)) Tag(g, MinimapIconKind.Greenhouse, 22f);
        foreach (var p in FindObjectsByType<FieldPlot>(FindObjectsSortMode.None)) Tag(p, MinimapIconKind.Field, 13f);
        foreach (var s in FindObjectsByType<ShopStall>(FindObjectsSortMode.None)) Tag(s, MinimapIconKind.Shop, 15f);
        if (AnimalPen.Instance != null) Tag(AnimalPen.Instance, MinimapIconKind.Pen, 20f);
        if (DeliveryZone.LoadingBay != null) Tag(DeliveryZone.LoadingBay, MinimapIconKind.LoadingBay, 20f);
        if (DeliveryZone.Market != null) Tag(DeliveryZone.Market, MinimapIconKind.Market, 22f);
    }

    private static void Tag(Component c, MinimapIconKind kind, float size)
    {
        if (c == null || c.GetComponent<MinimapIcon>() != null) return;
        MinimapIcon.Add(c.gameObject, kind, MinimapIcon.DefaultColor(kind), "", size);
    }

    private RectTransform Pooled(List<RectTransform> pool, RectTransform parent, int i)
    {
        while (pool.Count <= i)
        {
            var icon = MakeIcon(parent, Color.white, 16f, false);
            var t = MenuKit.Text(icon, 13, Color.black, "Letter");
            MenuKit.Stretch(t.rectTransform);
            t.alignment = TextAlignmentOptions.Center;
            pool.Add(icon);
        }
        return pool[i];
    }

    // ------------------------------------------------------------------ update

    private static Transform PlayerTransform()
    {
        var inputs = FindAnyObjectByType<StarterAssets.StarterAssetsInputs>();
        return inputs != null && inputs.gameObject.activeInHierarchy ? inputs.transform : null;
    }

    /// <summary>The thing the map follows: the vehicle being driven, else the player.</summary>
    public static Transform Anchor()
    {
        if (Vehicle.Current != null) return Vehicle.Current.transform;
        var p = PlayerTransform();
        if (p != null) return p;
        var cam = Camera.main;
        return cam != null ? cam.transform : null;
    }

    private static float ViewYawNow(Transform anchor)
    {
        var cam = Camera.main;
        if (cam != null && cam.isActiveAndEnabled && Vehicle.Current == null) return cam.transform.eulerAngles.y;
        return anchor != null ? anchor.eulerAngles.y : 0f;
    }

    private void Update()
    {
        scanTimer -= Time.unscaledDeltaTime;
        if (scanTimer <= 0f) { scanTimer = 2f; ScanWorld(); }
        if (miniRoot == null) return;
        if (FullOpen)
        {
            if (GameMenu.IsOpen) return;
            if (Time.frameCount != openedFrame && (GameInput.Pressed(GameAction.Map) || MenuNav.Back())) SetFull(false);
            return;
        }
        bool blocked = Time.timeScale == 0f || (UIManager.Instance != null && UIManager.Instance.IsUIOpened) || UIManager.PanelClosedFrame == Time.frameCount;
        if (!blocked && GameInput.Pressed(GameAction.Map)) SetFull(true);
    }

    public void SetFull(bool open)
    {
        if (fullRoot == null || open == FullOpen) return;
        FullOpen = open;
        openedFrame = Time.frameCount;
        fullRoot.gameObject.SetActive(open);
        miniRoot.gameObject.SetActive(!open);
        if (UIManager.Instance != null) UIManager.Instance.SetExternalPanel(open);
        if (open) { RefreshTexts(); FitFullMap(); }
    }

    // Frame every icon (and the player) on the north-up map.
    private void FitFullMap()
    {
        var anchor = Anchor();
        bool any = false; float minX = 0, maxX = 0, minZ = 0, maxZ = 0;
        System.Action<Vector3> add = p =>
        {
            if (!any) { minX = maxX = p.x; minZ = maxZ = p.z; any = true; return; }
            minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
        };
        if (anchor != null) add(anchor.position);
        foreach (var i in MinimapIcon.All) if (i != null && i.isActiveAndEnabled) add(i.transform.position);
        if (!any) return;
        Center = new Vector3((minX + maxX) * 0.5f, anchor != null ? anchor.position.y : 0f, (minZ + maxZ) * 0.5f);
        FullRange = Mathf.Clamp(Mathf.Max(maxX - minX, maxZ - minZ) * 0.5f + 25f, 60f, 400f);
    }

    private void LateUpdate()
    {
        if (mapCam == null || miniRoot == null) return;
        var anchor = Anchor();
        bool show = anchor != null;
        if (miniRoot.gameObject.activeSelf != (show && !FullOpen)) miniRoot.gameObject.SetActive(show && !FullOpen);
        mapCam.enabled = show;
        if (!show) return;

        float yawAnchor = anchor.eulerAngles.y;
        if (FullOpen)
        {
            ViewYaw = 0f;
            mapCam.orthographicSize = FullRange;
            mapCam.transform.position = new Vector3(Center.x, Center.y + 150f, Center.z);
            mapCam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            float k = fullSize * 0.5f / FullRange;
            PlaceIcons(fullPool, fullIcons, Center, 0f, k, fullSize * 0.5f, false);
            PlaceObjective(fullObjective, Center, 0f, k, fullSize * 0.5f, false);
            fullArrow.anchoredPosition = ToMap(anchor.position, Center, 0f, k);
            fullArrow.localRotation = Quaternion.Euler(0, 0, -yawAnchor);
            fullArrow.SetAsLastSibling();
            return;
        }

        ViewYaw = ViewYawNow(anchor);
        Center = anchor.position;
        mapCam.orthographicSize = miniRange;
        mapCam.transform.position = anchor.position + Vector3.up * 150f;
        mapCam.transform.rotation = Quaternion.Euler(90f, ViewYaw, 0f);   // image top = view direction
        float km = miniSize * 0.5f / miniRange;
        float radius = miniSize * 0.5f;
        PlaceIcons(miniPool, miniIcons, Center, ViewYaw, km, radius, true);
        PlaceObjective(miniObjective, Center, ViewYaw, km, radius, true);
        miniArrow.localRotation = Quaternion.Euler(0, 0, -(yawAnchor - ViewYaw));
        miniArrow.SetAsLastSibling();
        // "N" on the rim, pointing to world north.
        Vector2 north = ToMap(Center + Vector3.forward * miniRange, Center, ViewYaw, km);
        float len = Mathf.Sqrt(north.x * north.x + north.y * north.y);
        if (len > 0.01f) northTag.anchoredPosition = new Vector2(north.x / len * (radius - 4f), north.y / len * (radius - 4f));
    }

    /// <summary>World position -> map pixels (relative to the map centre) for a map turned by viewYaw.</summary>
    public static Vector2 ToMap(Vector3 world, Vector3 center, float viewYaw, float pixelsPerMetre)
    {
        Vector3 off = world - center; off.y = 0f;
        Vector3 local = Quaternion.Euler(0f, -viewYaw, 0f) * off;
        return new Vector2(local.x * pixelsPerMetre, local.z * pixelsPerMetre);
    }

    private void PlaceIcons(List<RectTransform> pool, RectTransform parent, Vector3 center, float yaw, float k, float half, bool round)
    {
        int used = 0;
        foreach (var icon in MinimapIcon.All)
        {
            if (icon == null || !icon.isActiveAndEnabled) continue;
            if (round && !icon.showOnMinimap) continue;
            Vector2 p = ToMap(icon.transform.position, center, yaw, k);
            float edge = half - icon.size * 0.35f;
            bool inside = round ? p.x * p.x + p.y * p.y <= edge * edge : Mathf.Abs(p.x) <= edge && Mathf.Abs(p.y) <= edge;
            if (!inside) continue;
            var rt = Pooled(pool, parent, used++);
            if (!rt.gameObject.activeSelf) rt.gameObject.SetActive(true);
            rt.anchoredPosition = p;
            float s = round ? icon.size : icon.size * 1.3f;
            rt.sizeDelta = new Vector2(s, s);
            rt.GetComponent<Image>().color = GameSettings.Palette(icon.color);   // Settings > Accessibility > Colour-blind mode
            var letter = rt.GetComponentInChildren<TextMeshProUGUI>();
            if (letter != null && letter.text != icon.letter) letter.text = icon.letter;
        }
        for (int i = used; i < pool.Count; i++) if (pool[i].gameObject.activeSelf) pool[i].gameObject.SetActive(false);
    }

    /// <summary>Icons drawn on the minimap right now (for tests).</summary>
    public int VisibleMiniIcons { get { int n = 0; foreach (var p in miniPool) if (p.gameObject.activeSelf) n++; return n; } }
    public RectTransform MiniObjective => miniObjective;

    // The objective stays on the rim of the minimap when it is out of range.
    private static void PlaceObjective(RectTransform rt, Vector3 center, float yaw, float k, float half, bool round)
    {
        var target = ObjectiveMarker.CurrentTarget;
        bool show = target != null && target.gameObject.activeInHierarchy;
        if (rt.gameObject.activeSelf != show) rt.gameObject.SetActive(show);
        if (!show) return;
        Vector2 p = ToMap(target.position, center, yaw, k);
        float edge = half - 8f;
        if (round)
        {
            float len = Mathf.Sqrt(p.x * p.x + p.y * p.y);
            if (len > edge) p = new Vector2(p.x / len * edge, p.y / len * edge);
        }
        else p = new Vector2(Mathf.Clamp(p.x, -edge, edge), Mathf.Clamp(p.y, -edge, edge));
        rt.anchoredPosition = p;
        rt.SetAsLastSibling();
    }
}
