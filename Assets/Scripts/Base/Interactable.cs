using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Interactable : MonoBehaviour, IInteractable
{
    [Header("Prompt")]
    [SerializeField] private string interactionPrompt = "[E] Use";
    [SerializeField] private Vector3 promptOffset;

    private const int outlineLayer = 6;
    private const int initLayer = 7;

    /// <summary>English prompt as set in the inspector (used as the localization key).</summary>
    protected string PromptKey => interactionPrompt;
    public virtual string InteractionPrompt => Loc.T(interactionPrompt);
    public Vector3 PromptOffset => promptOffset;
    public virtual bool CanInteract => true;

    [SerializeField] protected WebGLOutline outline;
    private readonly List<MeshRenderer> meshRenderers = new List<MeshRenderer>();
    private bool highlighted;
    protected bool IsInitialized { get; private set; }

    protected virtual void Awake() { }

    private IEnumerator Start()
    {
        // Wait so the greenhouse managers have set up their modules first.
        yield return new WaitForSeconds(1.0f);
        Initialize();
        IsInitialized = true;
        if (highlighted) SetHighlighted(true);
    }

    protected virtual void Initialize()
    {
        meshRenderers.AddRange(GetComponentsInChildren<MeshRenderer>(true));
    }

    public virtual void SetHighlighted(bool value)
    {
        highlighted = value;
        for (int i = 0; i < meshRenderers.Count; i++)
        {
            if (meshRenderers[i] != null)
                meshRenderers[i].gameObject.layer = value ? outlineLayer : initLayer;
        }
    }

    public virtual void Interact()
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX();
    }
}
