using System;
using UnityEngine;

/// <summary>World label whose text comes from data (e.g. sponsor names); re-applied when the language changes.</summary>
public class CozyLabel : MonoBehaviour
{
    public Func<string> text;     // translated, unshaped
    private TMPro.TMP_Text tmp;

    private void Start() { tmp = GetComponent<TMPro.TMP_Text>(); Loc.OnLanguageChanged += Apply; Apply(); }
    private void OnDestroy() => Loc.OnLanguageChanged -= Apply;

    public void Apply()
    {
        if (tmp == null || text == null) return;
        Loc.ApplyFont(tmp);
        tmp.text = Loc.Display(text());
    }
}
