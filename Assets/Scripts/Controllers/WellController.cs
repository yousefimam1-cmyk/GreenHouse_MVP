using DG.Tweening;
using UnityEngine;

public class WellController : Interactable
{
    [SerializeField] private Transform handleT;
    [SerializeField] private ControllStation controlStation;

    private bool started;
    public bool IsRunning => started;
    public override bool CanInteract => !started;

    public override void Interact()
    {
        if (started) return;   // previously each press stacked another endless rotation tween
        base.Interact();
        StartWell();
        if (outline != null) outline.EnableOutline(false);
    }

    public void StartWell()
    {
        started = true;
        if (AudioManager.Instance != null) AudioManager.Instance.StartWaterSound();
        if (handleT != null)
        {
            handleT.DORotate(new Vector3(360f, 0f, 0f), 2.0f, RotateMode.FastBeyond360)
                .SetRelative(true)
                .SetEase(Ease.Linear)
                .SetLoops(-1, LoopType.Restart)
                .SetLink(handleT.gameObject);
        }
        if (controlStation != null) controlStation.SetActionPerformed(true);
        GameEvents.RaiseWellStarted(this);
    }
}
