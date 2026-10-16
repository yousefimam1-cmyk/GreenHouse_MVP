using UnityEngine;

/// <summary>Records board at the pen gate: [E] / A opens the animal records panel (needs, health, breeding, sell, vet).</summary>
public class PenBoard : Interactable
{
    public override string InteractionPrompt
    {
        get
        {
            var h = HerdManager.Instance;
            int sick = h != null ? h.SickCount : 0;
            return Loc.Display(Loc.Raw("[E] Animal records") + (sick > 0 ? " (" + Loc.Raw("Sick") + " " + sick + ")" : ""));
        }
    }

    public override void Interact()
    {
        base.Interact();
        if (HerdManager.Instance != null) HerdManager.Instance.OpenPanel();
    }
}
