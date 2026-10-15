using UnityEngine;

/// <summary>Keeps a world label in the current language.</summary>
public class LocalizedLabel : MonoBehaviour
{
    public string key;
    private TMPro.TMP_Text text;
    private void Start() { text = GetComponent<TMPro.TMP_Text>(); Loc.OnLanguageChanged += Apply; Apply(); }
    private void OnDestroy() => Loc.OnLanguageChanged -= Apply;
    private void Apply() { if (text != null) Loc.Set(text, key); }
}
