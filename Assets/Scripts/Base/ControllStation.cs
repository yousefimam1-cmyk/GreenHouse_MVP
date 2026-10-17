using UnityEngine;

public class ControllStation : Interactable
{
    private GreenhouseManager greenhouseManager;
    public bool ActionRequired = false;
    private bool ActionPerformed = false;
    public string ActionText;

    public bool IsLocked => ActionRequired && !ActionPerformed;

    protected override void Awake()
    {
        base.Awake();
        greenhouseManager = GetComponentInParent<GreenhouseManager>();
    }

    public void SetActionPerformed(bool val)
    {
        ActionPerformed = val;
    }

    public override void Interact()
    {
        if (UIManager.Instance == null) return;
        if (IsLocked)
        {
            UIManager.Instance.ShowAlertMessage(Loc.T(string.IsNullOrEmpty(ActionText) ? "Start the well first to optimize modules!" : ActionText));
            return;
        }
        base.Interact();
        UIManager.Instance.ShowControlStationUI(greenhouseManager);
    }
}
