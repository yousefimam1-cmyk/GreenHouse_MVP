using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Mouse click / hover on a code-built menu row. Unlike a uGUI Button it never becomes the EventSystem's
/// selected object, so Enter / A (handled by MenuNav) can't "click" it a second time.
/// </summary>
public class MenuClick : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler
{
    public Action onClick;
    public Action onHover;

    public void OnPointerClick(PointerEventData eventData) { if (onClick != null) onClick(); }
    public void OnPointerEnter(PointerEventData eventData) { if (onHover != null) onHover(); }
}
