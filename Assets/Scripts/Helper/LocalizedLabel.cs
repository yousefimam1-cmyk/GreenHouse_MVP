using UnityEngine;

/// <summary>Keeps a world label in the current language.</summary>
public class LocalizedLabel : MonoBehaviour
{
    public string key;
    private TMPro.TMP_Text text;
    private void Start() { text = GetComponent<TMPro.TMP_Text>(); Loc.OnLanguageChanged += Apply; Apply(); }
    private void OnDestroy() => Loc.OnLanguageChanged -= Apply;
    private void Apply() { if (text != null) Loc.Set(text, key); }
    /// <summary>Show another English key (e.g. the market renamed "Village souq" on the Nile Delta map).</summary>
    public void SetKey(string newKey)
    {
        key = newKey;
        if (text == null) text = GetComponent<TMPro.TMP_Text>();
        Apply();
    }
}
