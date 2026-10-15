using UnityEngine;

public enum PenTaskKind { Water, Clean }

/// <summary>Water trough ([E] fill, needs the well) or barn door ([E] clean the pen).</summary>
public class PenTask : Interactable
{
    public PenTaskKind kind;
    public override string InteractionPrompt
    {
        get
        {
            var pen = AnimalPen.Instance;
            string pct = pen == null ? "" : " (" + Mathf.RoundToInt((kind == PenTaskKind.Water ? pen.WaterLevel : pen.Cleanliness) * 100f) + "%)";
            return Loc.Display(Loc.Raw(kind == PenTaskKind.Water ? "[E] Fill water trough" : "[E] Clean the pen") + pct);
        }
    }

    public override void Interact()
    {
        base.Interact();
        var pen = AnimalPen.Instance;
        if (pen == null || UIManager.Instance == null) return;
        UIManager.Instance.ShowAlertMessage(kind == PenTaskKind.Water ? pen.FillWater() : pen.Clean());
    }
}
