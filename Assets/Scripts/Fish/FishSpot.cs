using System;
using UnityEngine;

/// <summary>
/// E-interaction point for the fish feature group (reservoir, fish tank, processing buildings, apiary, dovecote).
/// The owner supplies the prompt and the action, so one small Interactable serves every building.
/// </summary>
public class FishSpot : Interactable
{
    public Func<string> prompt;
    public Action onInteract;

    public override string InteractionPrompt => prompt != null ? prompt() : base.InteractionPrompt;

    public override void Interact()
    {
        base.Interact();
        if (onInteract != null) onInteract();
    }

    /// <summary>A layer-7 child with a box collider; visuals parented under it are outlined when looked at.</summary>
    public static FishSpot Create(string name, Transform parent, Vector3 localPos, Vector3 colliderCenter, Vector3 colliderSize, Func<string> prompt, Action act)
    {
        var go = new GameObject(name);
        go.layer = 7;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        var c = go.AddComponent<BoxCollider>();
        c.center = colliderCenter; c.size = colliderSize;
        var s = go.AddComponent<FishSpot>();
        s.prompt = prompt; s.onInteract = act;
        return s;
    }
}
