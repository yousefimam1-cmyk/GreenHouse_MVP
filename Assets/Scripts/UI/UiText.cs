/// <summary>
/// Translation calls for the UI / input / menu screens. Same as Loc.T / Loc.Raw; the Arabic for these keys
/// lives in Resources/Data/Loc/50_ui.json (the headless tests check every UiText key has a translation).
/// </summary>
public static class UiText
{
    public static string T(string english) => Loc.T(english);
    public static string T(string english, params object[] args) => Loc.T(english, args);
    public static string Raw(string english) => Loc.Raw(english);
}
