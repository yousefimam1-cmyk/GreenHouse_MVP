using UnityEngine;
using UnityEngine.InputSystem;

public class InteractionController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera playerCamera;

    [Header("Settings")]
    [SerializeField] private float interactDistance = 3f;
    [SerializeField] private LayerMask interactableMask;
    [SerializeField] private InteractionPromptUI promptUI;

    private IInteractable currentInteractable;
    private Collider currentCollider;
    private string shownPrompt;

    private void Awake()
    {
        if (playerCamera == null) playerCamera = Camera.main;
    }

    private void Update()
    {
        // Don't look for or trigger interactions while a panel is open (E used to re-trigger behind the UI).
        if ((UIManager.Instance != null && UIManager.Instance.IsUIOpened) || UIManager.PanelClosedFrame == Time.frameCount || Time.timeScale == 0f)
        {
            ClearCurrent();
            return;
        }
        DetectInteractable();
        DetectInteractInput();
    }

    private void DetectInteractable()
    {
        if (playerCamera == null) return;
        Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f));

        if (Physics.Raycast(ray, out RaycastHit hit, interactDistance, interactableMask))
        {
            // Look on parents too, so colliders on child meshes still work.
            IInteractable interactable = hit.collider.GetComponentInParent<IInteractable>();
            if (interactable != null && !interactable.CanInteract) interactable = null;

            if (interactable != currentInteractable)
            {
                ClearCurrent();
                currentInteractable = interactable;
                currentCollider = hit.collider;
                if (currentInteractable != null)
                {
                    currentInteractable.SetHighlighted(true);
                    shownPrompt = currentInteractable.InteractionPrompt;
                    if (promptUI != null)
                        promptUI.Show(shownPrompt, hit.collider.transform, currentInteractable.PromptOffset);
                }
            }
            else if (currentInteractable != null)
            {
                // Prompt can change while looking (growth %, harvest ready, language switch).
                string prompt = currentInteractable.InteractionPrompt;
                if (prompt != shownPrompt && promptUI != null)
                {
                    shownPrompt = prompt;
                    promptUI.Show(prompt, currentCollider.transform, currentInteractable.PromptOffset);
                }
            }
        }
        else
        {
            ClearCurrent();
        }
    }

    private void DetectInteractInput()
    {
        if (currentInteractable == null) return;
        var kb = Keyboard.current;
        if (kb != null && kb.eKey.wasPressedThisFrame)
        {
            var target = currentInteractable;
            target.Interact();
            if (!target.CanInteract) ClearCurrent();
            else shownPrompt = null; // force prompt refresh
        }
    }

    private void ClearCurrent()
    {
        if (currentInteractable != null)
        {
            currentInteractable.SetHighlighted(false);
            currentInteractable = null;
            currentCollider = null;
            shownPrompt = null;
        }
        if (promptUI != null) promptUI.Hide();
    }
}
