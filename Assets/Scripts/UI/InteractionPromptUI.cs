using TMPro;
using UnityEngine;

public class InteractionPromptUI : MonoBehaviour
{
    [SerializeField] private RectTransform root;
    [SerializeField] private TMP_Text promptText;
    [SerializeField] private Camera playerCamera;

    private Transform target;
    private Vector3 offset;
    private string rawPrompt;
    private int glyphVersion = -1;

    private void LateUpdate()
    {
        if (target == null || root == null)
            return;
        // Re-find the camera when the view changes (third person camera, vehicle camera).
        if (playerCamera == null || !playerCamera.isActiveAndEnabled) playerCamera = Camera.main;
        if (playerCamera == null) return;
        if (glyphVersion != GameInput.Version && promptText != null && rawPrompt != null) { glyphVersion = GameInput.Version; promptText.text = Glyphs.Apply(rawPrompt); }

        Vector3 screenPos = playerCamera.WorldToScreenPoint(target.position + offset);
        // Hide when the target is behind the camera instead of drawing a mirrored prompt.
        root.position = screenPos.z > 0f ? screenPos : new Vector3(-10000f, -10000f, 0f);
    }

    /// <summary>Show a prompt. Text should already be localized (IInteractable.InteractionPrompt is).</summary>
    public void Show(string prompt, Transform targetTransform, Vector3 promptOffset)
    {
        target = targetTransform;
        offset = promptOffset;

        if (promptText != null)
        {
            Loc.ApplyFont(promptText);
            rawPrompt = prompt;
            glyphVersion = GameInput.Version;
            promptText.text = Glyphs.Apply(prompt);   // [E] -> [A] / [cross] / rebound key
        }
        if (root != null) root.gameObject.SetActive(true);
    }

    public void Hide()
    {
        target = null;
        if (root != null && root.gameObject.activeSelf) root.gameObject.SetActive(false);
    }
}
