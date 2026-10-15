using UnityEngine;
public interface IInteractable
{
    /// <summary>Prompt text, already localized/shaped for display.</summary>
    string InteractionPrompt { get; }
    Vector3 PromptOffset { get; }
    /// <summary>False when the object should be ignored (e.g. a well that is already running).</summary>
    bool CanInteract { get; }
    void SetHighlighted(bool value);
    void Interact();
}
