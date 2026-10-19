using UnityEngine;

/// <summary>
/// Screen / panel entrance of the design system (UiTheme.Motion): a quick fade with a slight scale-up, eased out,
/// on unscaled time so it also plays in the paused menus. Presentation only - layout, anchors and positions are
/// never touched, so code that places panels (and the tests that read them) is unaffected.
/// </summary>
public class UiMotion : MonoBehaviour
{
    private CanvasGroup group;
    private float t = 1f, from = 0.97f;

    /// <summary>Play the entrance on rt (adds the component and a CanvasGroup the first time).</summary>
    public static void Play(RectTransform rt, float scaleFrom = 0.97f)
    {
        if (rt == null) return;
        var m = rt.GetComponent<UiMotion>();
        if (m == null) m = rt.gameObject.AddComponent<UiMotion>();
        m.Begin(scaleFrom);
    }

    private void Begin(float scaleFrom)
    {
        if (group == null) group = GetComponent<CanvasGroup>();
        if (group == null) group = gameObject.AddComponent<CanvasGroup>();
        from = scaleFrom;
        t = 0f;
        Apply(0f);
        enabled = true;
    }

    private void Update()
    {
        t += Time.unscaledDeltaTime / UiTheme.Motion;
        Apply(t);
        if (t >= 1f) enabled = false;
    }

    private void OnDisable() { t = 1f; Apply(1f); }   // never leave a half-faded panel behind

    private void Apply(float x)
    {
        float e = 1f - Mathf.Pow(1f - Mathf.Clamp01(x), 3f);   // ease-out cubic
        if (group != null) group.alpha = e;
        float s = Mathf.Lerp(from, 1f, e);
        transform.localScale = new Vector3(s, s, 1f);
    }
}
