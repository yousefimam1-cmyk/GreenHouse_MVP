using UnityEngine;

public enum UtilityPartKind { Info, Solar, Generator, Biogas, Tank, Well }

/// <summary>
/// E-interaction on a utility object: clean the solar panels, refuel the generator (per-litre price),
/// or open the utilities panel for everything else.
/// </summary>
public class UtilityPart : Interactable
{
    public UtilityPartKind kind;
    public string label = "Utilities";
    public const float RefuelLitres = 100f;

    public override string InteractionPrompt
    {
        get
        {
            var g = UtilityGrid.Instance;
            var m = g != null ? g.Model : null;
            if (m == null) return Loc.T(label);
            switch (kind)
            {
                case UtilityPartKind.Solar:
                    return m.Dust > 0.01f ? Loc.T("[E] Clean solar panels (dust {0}%)", Mathf.RoundToInt(m.Dust * 100f)) : Loc.T("Solar panels are clean");
                case UtilityPartKind.Generator:
                    return Loc.T("[E] Refuel generator: 100 L for {0} SAR (tank {1} L)", Mathf.CeilToInt(RefuelLitres * UtilityModel.DieselPrice), m.DieselL.ToString("F0"));
                case UtilityPartKind.Biogas:
                    return Loc.T("Biogas digester - manure {0} kg  [E] Utilities", (EconomyManager.Instance != null ? EconomyManager.Instance.Stored("manure") : 0f).ToString("F0"));
                case UtilityPartKind.Tank:
                    return Loc.T("Water stored {0}/{1} m3  [E] Utilities", m.Stored.ToString("F0"), m.StorageCapacity.ToString("F0"));
                default:
                    return Loc.Display(Loc.Raw(label) + " - " + Loc.Raw("[E] Utilities"));
            }
        }
    }

    public override void Interact()
    {
        base.Interact();
        var g = UtilityGrid.Instance;
        if (g == null) return;
        switch (kind)
        {
            case UtilityPartKind.Solar:
                if (g.Model.Dust > 0.01f) { g.CleanPanels(); return; }
                break;
            case UtilityPartKind.Generator:
                g.BuyDiesel(RefuelLitres);
                return;
        }
        if (UtilitiesUI.Instance != null) UtilitiesUI.Instance.Open();
    }
}
