using UnityEngine;

/// <summary>Land office sign beside the fields: E opens the Land page of the shop.</summary>
public class LandOfficeSign : Interactable
{
    public static LandOfficeSign Create(Vector3 pos, Quaternion rot)
    {
        var root = FinSigns.Sign("Land Office Sign", pos, rot, new Color(0.62f, 0.45f, 0.2f), "Land office");
        return root.AddComponent<LandOfficeSign>();
    }

    public override string InteractionPrompt
    {
        get
        {
            var lo = LandOffice.Instance;
            if (lo != null && lo.Next != null)
                return Loc.Display(FinText.R("[E] Land office - buy") + " " + lo.Next.Value.plots + " " + FinText.R("plots") + " " + FinText.Sar(lo.Next.Value.price));
            return FinText.T("[E] Land office");
        }
    }

    public override void Interact()
    {
        base.Interact();
        if (ShopUI.Instance != null) ShopUI.Instance.OpenCategory("Land");
    }
}
