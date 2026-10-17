using System.Collections.Generic;
using UnityEngine;

/// <summary>Opens the shop.</summary>
public class ShopStall : Interactable
{
    public override string InteractionPrompt => Loc.T("[E] Open shop");
    public override void Interact()
    {
        base.Interact();
        if (ShopUI.Instance != null) ShopUI.Instance.Open();
    }
}
