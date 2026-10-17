using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Navigation in the open world: the full map (N) frames the whole world and shows a list of places beside it
/// (owned / for-sale land, discovered places, fast travel fee). W / S or the D-pad pick a place, Enter / A sets (or
/// clears) a waypoint there, E / X fast-travels to it (Farming-Simulator style: only places you have visited, for a
/// small fee by distance). Clicking on the map sets a waypoint (snaps to a place), right click clears it, the mouse
/// wheel zooms. The waypoint shows as a beam with the distance in the world, a "W" on the minimap and a HUD line.
/// </summary>
[MazraaSystem(52)]
public class WorldMap : MonoBehaviour
{
    public static WorldMap Instance { get; private set; }

    public Vector3? Waypoint { get; private set; }
    public WorldPoi WaypointPoi { get; private set; }
    public int Selected { get; private set; }
    public bool PanelVisible => panel != null && panel.gameObject.activeSelf;
    public string ListText => listText != null ? listText.text : "";

    public const int FeeBase = 20;
    public const float FeePerMetre = 0.05f;
    public const float ReachedRadius = 12f;

    private Canvas canvas;
    private RectTransform panel;
    private TextMeshProUGUI listText, hintText;
    private GameObject marker;
    private TextMeshPro markerText;
    private float listTimer;

    private void Awake() => Instance = this;

    private void Start()
    {
        HudLines.Status.Add(HudLine);
        OpenWorld.Built += OnWorldBuilt;
        Loc.OnLanguageChanged += Refresh;
        BuildUI();
        BuildMarker();
    }

    private void OnDestroy()
    {
        HudLines.Status.Remove(HudLine);
        OpenWorld.Built -= OnWorldBuilt;
        Loc.OnLanguageChanged -= Refresh;
        if (Instance == this) Instance = null;
    }

    private void OnWorldBuilt() { ClearWaypoint(); Selected = 0; }

    private static OpenWorld World => OpenWorld.Instance;

    /// <summary>Places on the map list: your farm, then every place of the world (service places, neighbours, land).</summary>
    public List<WorldPoi> Entries
    {
        get
        {
            var w = World;
            return w != null && w.Layout != null ? w.Layout.Pois : new List<WorldPoi>();
        }
    }

    // ------------------------------------------------------------------ waypoint

    public void SetWaypoint(Vector3 p, WorldPoi poi = null)
    {
        Waypoint = new Vector3(p.x, World != null ? World.GroundY(p) : p.y, p.z);
        WaypointPoi = poi;
        if (marker != null) { marker.transform.position = Waypoint.Value; marker.SetActive(true); }
        Refresh();
    }

    public void ClearWaypoint()
    {
        Waypoint = null; WaypointPoi = null;
        if (marker != null) marker.SetActive(false);
        Refresh();
    }

    public float WaypointDistance(Vector3 from)
    {
        if (!Waypoint.HasValue) return -1f;
        Vector3 d = Waypoint.Value - from; d.y = 0f;
        return d.magnitude;
    }

    public static string Metres(float m) => m >= 1000f ? (m / 1000f).ToString("0.0") + " " + Loc.Raw("km") : Mathf.RoundToInt(m) + " m";

    /// <summary>Compass direction (N, NE, E...) from a to b, translated.</summary>
    public static string Compass(Vector3 from, Vector3 to)
    {
        Vector3 d = to - from;
        float a = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        if (a < 0f) a += 360f;
        string[] k = { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };
        return Loc.Raw(k[Mathf.RoundToInt(a / 45f) % 8]);
    }

    private string HudLine()
    {
        if (!Waypoint.HasValue) return "";
        var a = Minimap.Anchor();
        if (a == null) return "";
        string name = WaypointPoi != null ? Loc.Raw(WaypointPoi.nameEn) : Loc.Raw("Waypoint");
        return Loc.Display(Loc.Raw("Waypoint") + ": " + name + " - " + Metres(WaypointDistance(a.position)) + " " + Compass(a.position, Waypoint.Value));
    }

    // ------------------------------------------------------------------ fast travel

    public int Fee(WorldPoi p, Vector3 from)
    {
        if (p == null) return 0;
        Vector3 d = p.arrival - from; d.y = 0f;
        return FeeBase + Mathf.RoundToInt(d.magnitude * FeePerMetre / 5f) * 5;
    }

    /// <summary>English Loc key of why fast travel there is not possible now (null = possible).</summary>
    public string FastTravelBlock(WorldPoi p)
    {
        var w = World;
        if (w == null || p == null || w.Layout == null) return "Not available";
        if (!w.Seen(p)) return "Visit it first to unlock fast travel";
        var a = Minimap.Anchor();
        var eco = EconomyManager.Instance;
        if (a != null && Flat(a.position - p.arrival).magnitude < 25f) return "You are already here";
        if (eco == null || eco.Money < Fee(p, a != null ? a.position : p.arrival)) return "Not enough money";
        return null;
    }

    /// <summary>Move the player (or the vehicle being driven, with its load) to a place's arrival spot for a fee.</summary>
    public bool FastTravel(WorldPoi p)
    {
        string why = FastTravelBlock(p);
        if (why != null) { Alert(Loc.T(why)); return false; }
        var a = Minimap.Anchor();
        int fee = Fee(p, a != null ? a.position : p.arrival);
        var w = World;
        Vector3 dest = w.OnGround(p.arrival, 0.2f);
        Vector3 look = p.pos - p.arrival; look.y = 0f;
        Quaternion rot = look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look.normalized) : Quaternion.identity;
        var v = Vehicle.Current;
        if (v != null)
        {
            var rb = v.GetComponent<Rigidbody>();
            if (rb != null) { rb.linearVelocity = Vector3.zero; rb.position = dest + Vector3.up * 0.6f; rb.rotation = rot; }
            v.transform.SetPositionAndRotation(dest + Vector3.up * 0.6f, rot);
            v.RefreshSurface();
        }
        else
        {
            var inputs = FindAnyObjectByType<StarterAssets.StarterAssetsInputs>();
            if (inputs == null) return false;
            var cc = inputs.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;   // the controller would snap the player back
            inputs.transform.SetPositionAndRotation(dest + Vector3.up * 0.1f, rot);
            if (cc != null) cc.enabled = true;
        }
        using (Ledger.As(Ledger.Other)) EconomyManager.Instance.AddMoney(-fee);
        if (Minimap.Instance != null && Minimap.Instance.FullOpen) Minimap.Instance.SetFull(false);
        if (WaypointPoi == p) ClearWaypoint();
        GameEvents.RaiseCustom("fastTravel", 1f);
        Alert(Loc.T("Fast travel to {0} for {1} SAR", Loc.Raw(p.nameEn), fee.ToString("N0")));
        return true;
    }

    // ------------------------------------------------------------------ UI

    private void BuildUI()
    {
        canvas = MenuKit.Canvas("World Map UI", transform, 42);
        panel = MenuKit.Rect("Places", canvas.transform, typeof(UnityEngine.UI.Image));
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0f, 0.5f);
        panel.anchoredPosition = new Vector2(24f, 0f);
        panel.sizeDelta = new Vector2(400f, 860f);
        panel.GetComponent<UnityEngine.UI.Image>().color = MenuKit.PanelColor;
        listText = MenuKit.Text(panel, 19, Color.white, "List");
        var lr = listText.rectTransform;
        lr.anchorMin = new Vector2(0f, 0f); lr.anchorMax = new Vector2(1f, 1f); lr.offsetMin = new Vector2(18f, 120f); lr.offsetMax = new Vector2(-18f, -16f);
        hintText = MenuKit.Text(panel, 16, new Color(0.85f, 0.82f, 0.76f), "Hint");
        var hr = hintText.rectTransform;
        hr.anchorMin = new Vector2(0f, 0f); hr.anchorMax = new Vector2(1f, 0f); hr.pivot = new Vector2(0.5f, 0f); hr.sizeDelta = new Vector2(-36f, 110f); hr.anchoredPosition = new Vector2(0f, 10f);
        panel.gameObject.SetActive(false);
    }

    private void BuildMarker()
    {
        marker = new GameObject("Waypoint");
        var glow = Build.Mat(new Color(0.3f, 0.85f, 1f), 0.6f, 0f, new Color(0.6f, 2.2f, 3f));
        Build.Part(PrimitiveType.Cylinder, marker.transform, new Vector3(0f, 20f, 0f), new Vector3(0.5f, 20f, 0.5f), glow, "Beam");
        var t = new GameObject("Distance");
        t.transform.SetParent(marker.transform, false);
        t.transform.localPosition = new Vector3(0f, 6f, 0f);
        markerText = t.AddComponent<TextMeshPro>();
        markerText.alignment = TextAlignmentOptions.Center;
        markerText.fontSize = 8f;
        markerText.color = new Color(0.7f, 0.95f, 1f);
        Loc.ExcludeFromScene(markerText);
        t.AddComponent<Billboard>();
        var icon = MinimapIcon.Add(marker, MinimapIconKind.Custom, new Color(0.3f, 0.85f, 1f), "W", 18f);
        if (icon != null) icon.showOnMinimap = true;
        marker.SetActive(false);
    }

    private void Refresh() { listTimer = 0f; }

    private void Update()
    {
        var a = Minimap.Anchor();
        if (Waypoint.HasValue && a != null)
        {
            float d = WaypointDistance(a.position);
            if (markerText != null) markerText.text = Metres(d);
            if (marker != null) marker.transform.localScale = Vector3.one * Mathf.Clamp(d / 120f, 1f, 8f);
            if (d < ReachedRadius) { Alert(Loc.T("Waypoint reached")); ClearWaypoint(); }
        }
        var map = Minimap.Instance;
        bool open = map != null && map.FullOpen && World != null && World.IsBuilt;
        if (panel != null && panel.gameObject.activeSelf != open) { panel.gameObject.SetActive(open); listTimer = 0f; }
        if (!open) return;
        var list = Entries;
        if (list.Count == 0) return;
        Selected = Mathf.Clamp(Selected, 0, list.Count - 1);
        int v = MenuNav.Vertical();
        if (v != 0) { Selected = (Selected + v + list.Count) % list.Count; listTimer = 0f; }
        if (MenuNav.Confirm()) ToggleWaypoint(list[Selected]);
        if (GameInput.Pressed(GameAction.Interact)) FastTravel(list[Selected]);
        MouseInput(map, list);
        listTimer -= Time.unscaledDeltaTime;
        if (listTimer <= 0f) { listTimer = 0.5f; UpdateList(list, a); }
    }

    public void ToggleWaypoint(WorldPoi p)
    {
        if (p == null) return;
        if (WaypointPoi == p) ClearWaypoint(); else SetWaypoint(p.pos, p);
    }

    private void MouseInput(Minimap map, List<WorldPoi> list)
    {
        var mouse = Mouse.current;
        if (mouse == null || map.FullMapRect == null) return;
        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) > 0.01f) map.SetFullView(map.Center, map.FullRange * (scroll > 0f ? 0.8f : 1.25f));
        bool left = mouse.leftButton.wasPressedThisFrame, right = mouse.rightButton.wasPressedThisFrame;
        if (!left && !right) return;
        if (right) { ClearWaypoint(); return; }
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(map.FullMapRect, mouse.position.ReadValue(), null, out Vector2 local)) return;
        float k = map.fullSize * 0.5f / map.FullRange;
        if (Mathf.Abs(local.x) > map.fullSize * 0.5f || Mathf.Abs(local.y) > map.fullSize * 0.5f) return;
        MapClick(local, k, map.Center, list);
    }

    /// <summary>A click on the full map at a pixel offset from its centre: waypoint there, snapped to a place within 24 px.</summary>
    public void MapClick(Vector2 local, float pixelsPerMetre, Vector3 center, List<WorldPoi> list)
    {
        Vector3 world = center + new Vector3(local.x / pixelsPerMetre, 0f, local.y / pixelsPerMetre);
        WorldPoi best = null; float bd = 24f / pixelsPerMetre;
        foreach (var p in list) { float d = Flat(p.pos - world).magnitude; if (d < bd) { bd = d; best = p; } }
        if (best != null) { Selected = list.IndexOf(best); SetWaypoint(best.pos, best); }
        else SetWaypoint(world);
    }

    private void UpdateList(List<WorldPoi> list, Transform anchor)
    {
        var w = World;
        var sb = new StringBuilder(1024);
        sb.Append(MenuKit.Title(Loc.T("Places"))).Append('\n');
        int first = Mathf.Clamp(Selected - 8, 0, Mathf.Max(0, list.Count - 17));
        for (int i = first; i < list.Count && i < first + 17; i++)
        {
            var p = list[i];
            string name = Loc.Raw(p.nameEn);
            string state;
            if (p.kind == PoiKind.Land)
            {
                bool own = WorldLand.Instance != null && WorldLand.Instance.Owned(p.parcel);
                state = own ? Loc.Raw("owned") : Loc.Raw("for sale") + " " + p.parcel.price.ToString("N0") + " " + Loc.Raw("SAR");
            }
            else state = w.Seen(p) ? Loc.Raw("fast travel") + " " + Fee(p, anchor != null ? anchor.position : p.arrival).ToString("N0") + " " + Loc.Raw("SAR") : Loc.Raw("not visited");
            string dist = anchor != null ? Metres(Flat(anchor.position - p.pos).magnitude) : "";
            string line = (i == Selected ? "> " : "   ") + name + (WaypointPoi == p ? " [W]" : "") + "\n      " + dist + " - " + state;
            string c = i == Selected ? "#FFB060" : w.Seen(p) || p.kind == PoiKind.Land ? "#FFFFFF" : "#FFFFFF88";
            sb.Append("<color=").Append(c).Append('>').Append(Loc.Display(line)).Append("</color>\n");
        }
        listText.text = sb.ToString();
        Loc.ApplyFont(listText); Loc.ApplyFont(hintText);
        hintText.text = Glyphs.Apply(Loc.T("[W/S] choose  [Enter] waypoint  [E] fast travel") + "\n" + Loc.T("Click: waypoint  Right click: clear  Wheel: zoom"));
    }

    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    private static void Alert(string msg) { if (UIManager.Instance != null) UIManager.Instance.ShowAlertMessage(msg); }
}
