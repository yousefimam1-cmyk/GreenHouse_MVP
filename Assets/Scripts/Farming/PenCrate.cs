using System.Collections.Generic;
using UnityEngine;

/// <summary>The collection crate at the pen.</summary>
public class PenCrate : Interactable
{
    public override string InteractionPrompt => Loc.T("[E] Collect animal products");
    public override void Interact()
    {
        base.Interact();
        if (AnimalPen.Instance != null && UIManager.Instance != null)
            UIManager.Instance.ShowAlertMessage(AnimalPen.Instance.Collect());
    }
}
