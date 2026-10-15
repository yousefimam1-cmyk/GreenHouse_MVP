using System;
using UnityEngine;

/// <summary>Interactable built in code: prompt and action are delegates (saluki, falcon perch, quest board, villagers, decor...).</summary>
public class CozyProp : Interactable
{
    public Func<string> prompt;          // already translated + shaped
    public Action use;
    public Func<bool> available;

    public override string InteractionPrompt => prompt != null ? prompt() : base.InteractionPrompt;
    public override bool CanInteract => available == null || available();

    public override void Interact()
    {
        base.Interact();
        if (use != null) use();
    }
}
