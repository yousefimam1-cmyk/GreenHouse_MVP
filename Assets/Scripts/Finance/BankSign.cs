using UnityEngine;

/// <summary>Bank sign by the market: E repays what you can, or opens the Bank page when there is no debt (or no cash).</summary>
public class BankSign : Interactable
{
    public static BankSign Create(Vector3 pos, Quaternion rot)
    {
        var root = FinSigns.Sign("Bank Sign", pos, rot, new Color(0.13f, 0.45f, 0.3f), "Bank");
        return root.AddComponent<BankSign>();
    }

    public override string InteractionPrompt
    {
        get
        {
            var b = BankManager.Instance; var eco = EconomyManager.Instance;
            if (b != null && b.Debt > 0 && eco != null && eco.Money > 0)
                return Loc.Display(FinText.R("[E] Bank - repay") + " " + FinText.Sar(Mathf.Min(b.Debt, eco.Money)));
            return FinText.T("[E] Bank - loans");
        }
    }

    public override void Interact()
    {
        base.Interact();
        var b = BankManager.Instance; var eco = EconomyManager.Instance;
        if (b != null && b.Debt > 0 && eco != null && eco.Money > 0) { b.RepayWhatYouCan(); return; }
        if (ShopUI.Instance != null) ShopUI.Instance.OpenCategory("Bank");
    }
}
