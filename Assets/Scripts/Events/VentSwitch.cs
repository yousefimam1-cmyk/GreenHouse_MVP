using UnityEngine;

/// <summary>Greenhouse vent switch next to each control station: E closes / opens the vents (closed = safe from dust storms).</summary>
public class VentSwitch : Interactable
{
    public GreenhouseManager greenhouse;
    private MeshRenderer board;

    public static VentSwitch Create(GreenhouseManager gh, Vector3 pos, Quaternion rot)
    {
        var root = FinSigns.Sign("Vent Switch " + gh.name, pos, rot, new Color(0.25f, 0.6f, 0.3f), "Greenhouse vents");
        root.transform.localScale = Vector3.one * 0.6f;
        var v = root.AddComponent<VentSwitch>();
        v.greenhouse = gh;
        var b = root.transform.Find("Board");
        if (b != null) v.board = b.GetComponent<MeshRenderer>();
        v.Refresh();
        return v;
    }

    private bool Closed => EventDirector.Instance != null && EventDirector.Instance.IsClosed(greenhouse);

    public override string InteractionPrompt => FinText.T(Closed ? "[E] Open the greenhouse vents" : "[E] Close the greenhouse vents (dust storms)");

    public override void Interact()
    {
        base.Interact();
        var ed = EventDirector.Instance;
        if (ed == null || greenhouse == null) return;
        ed.SetClosed(greenhouse, !Closed);
        FinText.Alert(FinText.T(Closed ? "Vents closed - the greenhouse is sealed" : "Vents open"));
    }

    public void Refresh()
    {
        if (board != null) board.sharedMaterial = Build.Mat(Closed ? new Color(0.75f, 0.25f, 0.2f) : new Color(0.25f, 0.6f, 0.3f), 0.3f);
    }
}
