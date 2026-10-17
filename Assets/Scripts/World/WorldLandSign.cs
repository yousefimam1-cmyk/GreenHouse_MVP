using UnityEngine;

/// <summary>"Farmland for sale" sign on an open-world parcel: E buys it.</summary>
public class WorldLandSign : Interactable
{
    public LandParcel Parcel { get; private set; }

    public static WorldLandSign Create(Vector3 pos, Quaternion rot, LandParcel parcel)
    {
        var root = FinSigns.Sign("Farmland Sign " + parcel.id, pos, rot, new Color(0.85f, 0.65f, 0.15f), "Farmland for sale");
        var s = root.AddComponent<WorldLandSign>();
        s.Parcel = parcel;
        return s;
    }

    public override string InteractionPrompt
    {
        get
        {
            if (Parcel == null) return "";
            return Loc.Display(Loc.Raw("[E] Buy farmland") + ": " + Parcel.Plots + " " + Loc.Raw("plots") + " - " + Parcel.price.ToString("N0") + " " + Loc.Raw("SAR"));
        }
    }

    public override void Interact()
    {
        base.Interact();
        if (WorldLand.Instance != null) WorldLand.Instance.Buy(Parcel);
    }
}
