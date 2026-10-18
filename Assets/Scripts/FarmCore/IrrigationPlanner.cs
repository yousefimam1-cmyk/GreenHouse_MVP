using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>
/// The irrigation planner: a side panel and a marker where you look, while you keep walking around the farm. Pick a part
/// (wells, deep well, canal intake, tank, junction, soil sensor, centre pivot, linear move, sprinkler, drip lines, flood gate),
/// a pipe or Edit; place it where you aim. Pipes join the part nearest the aim to the next one you pick (cost per metre by
/// diameter). Edit sets the aimed emitter's mode (off / on / schedule / auto from a sensor), schedule start and hours,
/// revolution time (pivot / linear dose) and pump power (grid / solar / diesel). Coverage rings show while it is open, and the
/// panel lists every network's supply against demand, groundwater, the canal turn and shortages.
/// Keyboard: Up / Down part, Left / Right size / diameter / value, Enter place, Delete remove, 1-5 edit row, Esc close.
/// Gamepad: LB / RB part, D-pad left / right value, D-pad up / down edit row, A place, X remove, B close.
/// Opened from the Fields page (Enter on a field).
/// </summary>
[MazraaSystem(57)]
public class IrrigationPlanner : MonoBehaviour
{
    public static IrrigationPlanner Instance { get; private set; }
    public static bool IsOpen => Instance != null && Instance.open;

    public enum Tool { Well, DeepWell, Canal, Tank, Junction, Sensor, Pivot, Linear, Sprinkler, Drip, Flood, Pipe, Edit }
    public static readonly int[] Diameters = { 50, 75, 110, 160 };
    public static readonly string[] EditRows = { "Mode", "Start", "Hours", "Revolution", "Power" };

    public Tool Current { get; private set; } = Tool.Pivot;
    public float Size { get; private set; } = 30f;
    public int DiameterIndex { get; private set; } = 1;
    public int PipeStart { get; private set; } = -1;
    public int EditRow { get; private set; }
    public Vector3 Aim { get; private set; }
    public string LastMessage { get; private set; } = "";

    private bool open;
    private int openedFrame;
    private Canvas canvas;
    private TextMeshProUGUI text;
    private GameObject marker;
    private float refresh;

    private void Awake() => Instance = this;
    private void OnDestroy() { if (Instance == this) Instance = null; }

    private void Start()
    {
        canvas = MenuKit.Canvas("Irrigation Planner UI", transform, 58);
        var panel = MenuKit.Panel("Panel", canvas.transform, 560);
        panel.anchorMin = panel.anchorMax = new Vector2(1f, 0.5f);
        panel.pivot = new Vector2(1f, 0.5f);
        panel.anchoredPosition = new Vector2(-24f, 0f);
        text = MenuKit.Text(panel, 20, Color.white);
        canvas.gameObject.SetActive(false);
    }

    public void Open(FarmField f)
    {
        open = true; openedFrame = Time.frameCount;
        if (canvas != null) canvas.gameObject.SetActive(true);
        if (IrrigationSystem.Instance != null) IrrigationSystem.Instance.SetCoverage(true);
        if (f != null) Aim = f.center;
        LastMessage = "";
        Refresh();
    }

    public void Close()
    {
        open = false; PipeStart = -1;
        if (canvas != null) canvas.gameObject.SetActive(false);
        if (marker != null) marker.SetActive(false);
        if (IrrigationSystem.Instance != null) IrrigationSystem.Instance.SetCoverage(false);
    }

    private static bool K(KeyControl k) => k != null && k.wasPressedThisFrame;
    private static bool B(ButtonControl b) => b != null && b.wasPressedThisFrame;

    private void Update()
    {
        if (!open || Time.frameCount == openedFrame) return;
        if (GameMenu.IsOpen) return;
        UpdateAim();
        var kb = Keyboard.current; var pad = Gamepad.current;
        if (MenuNav.Back()) { Close(); return; }
        int part = 0, adjust = 0, row = 0;
        bool place = false, remove = false;
        if (kb != null)
        {
            if (K(kb.upArrowKey)) part = -1; if (K(kb.downArrowKey)) part = 1;
            if (K(kb.leftArrowKey)) adjust = -1; if (K(kb.rightArrowKey)) adjust = 1;
            if (K(kb.enterKey) || K(kb.numpadEnterKey)) place = true;
            if (K(kb.deleteKey) || K(kb.backspaceKey)) remove = true;
            int d = MenuNav.Digit();
            if (d >= 0 && d < EditRows.Length) EditRow = d;
        }
        if (pad != null)
        {
            if (B(pad.leftShoulder)) part = -1; if (B(pad.rightShoulder)) part = 1;
            if (B(pad.dpad.left)) adjust = -1; if (B(pad.dpad.right)) adjust = 1;
            if (B(pad.dpad.up)) row = -1; if (B(pad.dpad.down)) row = 1;
            if (B(pad.buttonSouth)) place = true;
            if (B(pad.buttonWest)) remove = true;
        }
        if (part != 0) Cycle(part);
        if (row != 0) EditRow = (EditRow + row + EditRows.Length) % EditRows.Length;
        if (adjust != 0) Adjust(adjust);
        if (place) Act();
        if (remove) RemoveAimed();
        refresh -= Time.unscaledDeltaTime;
        if (refresh <= 0f || part != 0 || adjust != 0 || place || remove || row != 0) { refresh = 0.5f; Refresh(); }
    }

    /// <summary>Where the camera looks on the ground (4..40 m ahead).</summary>
    private void UpdateAim()
    {
        var cam = Camera.main;
        if (cam == null) return;
        Ray r = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        float groundY = Build.GroundY(cam.transform.position);
        float t = r.direction.y < -0.02f ? (groundY - r.origin.y) / r.direction.y : 12f;
        t = Mathf.Clamp(t, 4f, 40f);
        Vector3 p = r.origin + r.direction * t;
        p = new Vector3(Mathf.Round(p.x), 0f, Mathf.Round(p.z));   // snap to a 1 m grid
        var irr = IrrigationSystem.Instance;
        var near = irr != null ? irr.Nearest(p, 2f) : null;
        if (near != null) p = near.pos;   // snap onto an existing part
        Aim = Build.OnGround(p);
        ShowMarker();
    }

    private void ShowMarker()
    {
        if (marker == null)
        {
            marker = new GameObject("Planner marker");
            Build.Part(PrimitiveType.Cylinder, marker.transform, new Vector3(0, 0.6f, 0), new Vector3(0.25f, 0.6f, 0.25f), Build.Mat(new Color(0.2f, 0.7f, 1f), 0.3f, 0f, new Color(0.1f, 0.4f, 0.9f)), "Marker");
            foreach (var rr in marker.GetComponentsInChildren<Renderer>(true)) rr.gameObject.layer = 2;
        }
        marker.SetActive(true);
        marker.transform.position = Aim;
    }

    public void Cycle(int dir)
    {
        int n = System.Enum.GetValues(typeof(Tool)).Length;
        Current = (Tool)(((int)Current + dir + n) % n);
        if (Current == Tool.Canal && !FcData.CurrentRegion.canal) Current = (Tool)(((int)Current + dir + n) % n);
        var e = FcData.Emitter(KindOf(Current));
        if (Current == Tool.Pivot || Current == Tool.Sprinkler) Size = Mathf.Clamp(e.size, e.minSize, e.maxSize);
        PipeStart = -1;
    }

    public static IrrKind KindOf(Tool t) => t <= Tool.Flood ? (IrrKind)(int)t : IrrKind.Junction;

    public void Adjust(int dir)
    {
        var irr = IrrigationSystem.Instance;
        if (Current == Tool.Pivot || Current == Tool.Sprinkler)
        {
            var e = FcData.Emitter(KindOf(Current));
            Size = Mathf.Clamp(Size + dir * (Current == Tool.Pivot ? 5f : 1f), Mathf.Max(1f, e.minSize), e.maxSize);
        }
        else if (Current == Tool.Pipe) DiameterIndex = Mathf.Clamp(DiameterIndex + dir, 0, Diameters.Length - 1);
        else if (Current == Tool.Edit && irr != null)
        {
            var n = irr.Nearest(Aim, 6f);
            if (n == null) return;
            IrrMode mode = n.mode; int start = n.startHour, hours = n.hours; float rev = n.revHours; IrrPower power = n.power;
            switch (EditRow)
            {
                case 0: mode = (IrrMode)(((int)mode + dir + 4) % 4); break;
                case 1: start = (start + dir + 24) % 24; break;
                case 2: hours = Mathf.Clamp(hours + dir, 0, 24); break;
                case 3: rev = Mathf.Clamp(rev + dir * 2f, 2f, 96f); break;
                default: power = (IrrPower)(((int)power + dir + 3) % 3); break;
            }
            irr.Configure(n.id, mode, start, hours, rev, power);
        }
    }

    /// <summary>Enter / A: place the chosen part, or pick the pipe ends.</summary>
    public void Act()
    {
        var irr = IrrigationSystem.Instance;
        if (irr == null) return;
        string why;
        if (Current == Tool.Edit) return;
        if (Current == Tool.Pipe)
        {
            var n = irr.Nearest(Aim, 3f);
            if (n == null) { LastMessage = FcText.R("Aim at a part to join"); return; }
            if (PipeStart < 0 || irr.Net.Node(PipeStart) == null) { PipeStart = n.id; LastMessage = FcText.R("Pipe from") + " " + FcText.R(n.kind.ToString()) + " - " + FcText.R("now aim at the other end"); return; }
            var p = irr.Connect(PipeStart, n.id, Diameters[DiameterIndex], out why);
            LastMessage = p != null ? FcText.R("Pipe laid") + ": " + p.length.ToString("F0") + " m, " + p.diameterMm + " mm" : FcText.R(why ?? "Sent to the host");
            PipeStart = n.id;   // keep chaining from here
            return;
        }
        var kind = KindOf(Current);
        var node = irr.Place(kind, Aim, Current == Tool.Pivot || Current == Tool.Sprinkler ? Size : -1f, out why);
        if (node == null) { LastMessage = FcText.R(why ?? "Sent to the host"); return; }
        LastMessage = FcText.R("Placed") + ": " + FcText.R(kind.ToString());
        // join the new part to the last one used for pipes, so a network grows as you place it
        if (PipeStart >= 0 && irr.Net.Node(PipeStart) != null && PipeStart != node.id) irr.Connect(PipeStart, node.id, Diameters[DiameterIndex], out why);
        PipeStart = node.id;
    }

    public void RemoveAimed()
    {
        var irr = IrrigationSystem.Instance;
        var n = irr != null ? irr.Nearest(Aim, 3f) : null;
        if (n == null) return;
        irr.Remove(n.id);
        if (PipeStart == n.id) PipeStart = -1;
        LastMessage = FcText.R("Removed") + ": " + FcText.R(n.kind.ToString());
    }

    private void Refresh()
    {
        if (text == null || !open) return;
        Loc.ApplyFont(text);
        text.alignment = Loc.IsArabic ? TextAlignmentOptions.TopRight : TextAlignmentOptions.TopLeft;
        text.text = PanelText();
    }

    /// <summary>The panel text (also used by tests).</summary>
    public string PanelText()
    {
        var sb = new StringBuilder();
        var irr = IrrigationSystem.Instance;
        var ff = FarmFields.Instance;
        sb.Append(MenuKit.Title(FcText.T("Irrigation planner")));
        string tool = FcText.R(Current == Tool.Pipe ? "Pipe" : Current == Tool.Edit ? "Edit" : KindOf(Current).ToString());
        var field = ff != null ? ff.FieldAt(Aim, 3f) : null;
        if (Current <= Tool.Flood)
        {
            var kind = KindOf(Current);
            float size = Current == Tool.Pivot || Current == Tool.Sprinkler ? Size : kind == IrrKind.Linear && field != null ? field.width : FcData.Emitter(kind).size;
            int price = IrrigationNetwork.Price(kind, size, field != null ? field.Area : 0f);
            tool += (Current == Tool.Pivot || Current == Tool.Sprinkler ? " · " + FcText.R("radius") + " " + Size.ToString("F0") + " m" : "") + " · " + price.ToString("N0") + " " + FcText.R("SAR");
            if (kind >= IrrKind.Pivot && irr != null && ff != null)
            {
                var probe = new IrrNode { kind = kind, pos = Aim, size = size, field = field != null ? field.id : -1 };
                float m2 = irr.Net.CoveredM2(probe, ff.Fields);
                var e = FcData.Emitter(kind);
                tool += "\n" + FcText.R("Covers") + " " + FcText.Area(m2) + " · " + FcText.R("needs") + " " + (e.flowHa * m2 / 10000f).ToString("F1") + " m3/h · " + FcText.R("efficiency") + " " + FcText.Pct(e.eff);
            }
        }
        else if (Current == Tool.Pipe)
        {
            int d = Diameters[DiameterIndex];
            tool += " · " + d + " mm · " + FcData.Pipe(d).capacity.ToString("F0") + " m3/h · " + FcData.Pipe(d).pricePerM.ToString("F0") + " " + FcText.R("SAR") + "/m";
            var start = irr != null ? irr.Net.Node(PipeStart) : null;
            if (start != null) tool += "\n" + FcText.R("From") + " " + FcText.R(start.kind.ToString()) + " · " + IrrigationNetwork.Flat(start.pos - Aim).ToString("F0") + " m";
        }
        sb.Append('\n').Append(Loc.Display(FcText.R("Part") + ": " + tool));
        if (field != null) sb.Append('\n').Append(Loc.Display(FcText.R("Aim") + ": " + FarmFields.FieldName(field) + " · " + FcText.R("Soil water") + " " + FcText.Pct(FarmSim.Summarize(field).moisture)));
        if (Current == Tool.Edit && irr != null)
        {
            var n = irr.Nearest(Aim, 6f);
            if (n == null) sb.Append('\n').Append(FcText.T("Aim at a part to change it"));
            else
            {
                sb.Append('\n').Append(Loc.Display("<b>" + FcText.R(n.kind.ToString()) + "</b>" + (n.IsEmitter ? " · " + (n.running ? FcText.R("running") : FcText.R("stopped")) + " · " + n.delivered.ToString("F1") + "/" + n.demand.ToString("F1") + " m3/h" : "")));
                string[] vals = { FcText.R(n.mode.ToString()), n.startHour.ToString("00") + ":00", n.hours + " h", n.revHours.ToString("F0") + " h", FcText.R(n.power.ToString()) };
                for (int i = 0; i < EditRows.Length; i++)
                    sb.Append('\n').Append(Loc.Display((i == EditRow ? "<color=#FF9A3C>> " : "  ") + (i + 1) + " " + FcText.R(EditRows[i]) + ": " + vals[i] + (i == EditRow ? "</color>" : "")));
                if (n.kind == IrrKind.Sensor) sb.Append('\n').Append(Loc.Display(FcText.R("Soil water") + " " + (n.reading >= 0f ? FcText.Pct(n.reading) : "-")));
            }
        }
        if (!string.IsNullOrEmpty(LastMessage)) sb.Append('\n').Append("<color=#FFD27A>").Append(Loc.Display(LastMessage)).Append("</color>");
        if (irr != null)
        {
            sb.Append("\n\n").Append(MenuKit.Title(FcText.T("Networks")));
            foreach (var line in irr.NetworkLines()) sb.Append('\n').Append(Loc.Display(line));
        }
        sb.Append("\n\n<size=80%><color=#BBBBBB>").Append(Glyphs.Apply(FcText.T("Up / Down: part   Left / Right: size / value   [Enter] place   [Delete] remove   1-5: edit row   [Esc] close"))).Append("</color></size>");
        return sb.ToString();
    }
}
