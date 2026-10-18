using UnityEngine;

/// <summary>
/// A shop of the open world's town (World/WorldTown): E opens the farm shop on this shop's page (seeds, supplies,
/// animals, vehicles / upgrades) - the same items, prices and money as everywhere else. The cafe sells a glass of tea.
/// </summary>
public class TownShop : Interactable
{
    public string category = "";     // ShopUI page; "" = the whole shop; "Cafe" = tea
    public string nameEn = "", nameAr = "";
    public const int TeaPrice = 5;
    public int TeaServed { get; private set; }

    public override string InteractionPrompt
        => category == "Cafe" ? Loc.T("[E] Tea - {0} SAR", TeaPrice) : Loc.T("[E] {0}", Loc.Raw(nameEn));

    public override void Interact()
    {
        base.Interact();
        if (category == "Cafe") { Tea(); return; }
        var ui = ShopUI.Instance;
        if (ui == null) return;
        if (string.IsNullOrEmpty(category)) ui.Open(); else ui.OpenCategory(category);
    }

    /// <summary>Buy a glass of tea at the cafe (false = not enough money).</summary>
    public bool Tea()
    {
        var eco = EconomyManager.Instance;
        if (eco == null) return false;
        if (eco.Money < TeaPrice) { Say(Loc.T("Not enough money")); return false; }
        eco.AddMoney(-TeaPrice);
        TeaServed++;
        Say(Loc.T("A glass of sweet tea - refreshing!"));
        return true;
    }

    private static void Say(string msg) { if (UIManager.Instance != null) UIManager.Instance.ShowAlertMessage(msg); }
}
