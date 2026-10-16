using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Player-safety tools for co-op chat (needed for store age ratings):
///  - Block: a blocked player's chat, bubbles and emotes are hidden, and they can't join your farm.
///  - Report: saves the player's name and last messages to persistentDataPath/mazraa_reports.txt, blocks them and
///    opens an e-mail to the Mazraa team with the report filled in (nothing is sent without the player pressing Send).
///  - Bad-word filter: English and Arabic profanity is replaced with *** when sending and when receiving.
///  - Chat: Everyone / Friends only / Off. Who can join your farm: Anyone / Friends only. Friends are names you add.
/// Lists and settings are stored with PlayerPrefs (per PC, not in the save game).
/// </summary>
public static class ChatSafety
{
    public enum ChatMode { Everyone, FriendsOnly, Off }
    public enum JoinMode { Anyone, FriendsOnly }

    public const string ReportEmail = "moimam@gmail.com";
    private const string ChatKey = "mazraa.chat.mode", JoinKey = "mazraa.join.mode", BlockedKey = "mazraa.chat.blocked", FriendsKey = "mazraa.chat.friends";

    public static string ReportPath => Path.Combine(Application.persistentDataPath, "mazraa_reports.txt");

    public static ChatMode Chat
    {
        get => (ChatMode)Mathf.Clamp(PlayerPrefs.GetInt(ChatKey, 0), 0, 2);
        set { PlayerPrefs.SetInt(ChatKey, (int)value); PlayerPrefs.Save(); }
    }

    public static JoinMode Join
    {
        get => (JoinMode)Mathf.Clamp(PlayerPrefs.GetInt(JoinKey, 0), 0, 1);
        set { PlayerPrefs.SetInt(JoinKey, (int)value); PlayerPrefs.Save(); }
    }

    public static ChatMode NextChatMode() => Chat = (ChatMode)(((int)Chat + 1) % 3);
    public static JoinMode NextJoinMode() => Join = (JoinMode)(((int)Join + 1) % 2);

    public static string ChatModeLabel(ChatMode m) => m == ChatMode.Everyone ? "Everyone" : m == ChatMode.FriendsOnly ? "Friends only" : "Off";
    public static string JoinModeLabel(JoinMode m) => m == JoinMode.Anyone ? "Anyone" : "Friends only";

    // ------------------------------------------------------------------ friends / blocked lists

    private static string Norm(string name) => (name ?? "").Trim().ToLowerInvariant();

    private static List<string> GetList(string key)
    {
        var list = new List<string>();
        foreach (var s in PlayerPrefs.GetString(key, "").Split('\n')) if (s.Length > 0) list.Add(s);
        return list;
    }

    private static void SetList(string key, List<string> list) { PlayerPrefs.SetString(key, string.Join("\n", list.ToArray())); PlayerPrefs.Save(); }

    private static bool InList(string key, string name)
    {
        string n = Norm(name);
        if (n.Length == 0) return false;
        foreach (var s in GetList(key)) if (s == n) return true;
        return false;
    }

    private static void SetInList(string key, string name, bool on)
    {
        string n = Norm(name);
        if (n.Length == 0) return;
        var list = GetList(key);
        list.Remove(n);
        if (on) list.Add(n);
        SetList(key, list);
    }

    public static bool IsBlocked(string name) => InList(BlockedKey, name);
    public static bool IsFriend(string name) => InList(FriendsKey, name);
    public static void SetBlocked(string name, bool blocked) { SetInList(BlockedKey, name, blocked); if (blocked) SetInList(FriendsKey, name, false); }
    public static void SetFriend(string name, bool friend) { SetInList(FriendsKey, name, friend); if (friend) SetInList(BlockedKey, name, false); }
    public static List<string> Blocked => GetList(BlockedKey);
    public static List<string> Friends => GetList(FriendsKey);

    /// <summary>Should chat / bubbles / emotes from this player be shown?</summary>
    public static bool ShouldShow(string from)
    {
        if (IsBlocked(from)) return false;
        var mode = Chat;
        if (mode == ChatMode.Off) return false;
        return mode == ChatMode.Everyone || IsFriend(from);
    }

    /// <summary>Host: may this player join your farm?</summary>
    public static bool AllowJoin(string name) => !IsBlocked(name) && (Join == JoinMode.Anyone || IsFriend(name));

    // ------------------------------------------------------------------ bad-word filter

    // Whole-word matches (after normalising case, accents/diacritics, repeated letters and common l33t swaps).
    private static readonly HashSet<string> BadWords = new HashSet<string>
    {
        "fuck", "fucking", "fucker", "fck", "fuk", "shit", "bitch", "bastard", "cunt", "dick", "cock", "pussy", "asshole",
        "ass", "whore", "slut", "motherfucker", "wanker", "twat", "prick", "retard", "nigger", "nigga", "faggot", "fag",
        "porn", "sex", "rape",
        "شرموط", "شرموطة", "قحبة", "قحبه", "منيك", "منيوك", "متناك", "كس", "كسمك", "كسختك", "زب", "زبي", "طيز", "خول",
        "عرص", "معرص", "لوطي", "ابن الكلب", "يلعن", "نيك", "انيك", "ينيك", "سكس", "عاهرة", "عاهره", "ديوث",
    };

    /// <summary>Replace bad words with asterisks (keeps the rest of the message).</summary>
    public static string Filter(string text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        var sb = new StringBuilder(text.Length);
        int i = 0;
        while (i < text.Length)
        {
            if (!IsWordChar(text[i])) { sb.Append(text[i]); i++; continue; }
            int start = i;
            while (i < text.Length && IsWordChar(text[i])) i++;
            string word = text.Substring(start, i - start);
            sb.Append(IsBad(word) ? new string('*', word.Length) : word);
        }
        string result = sb.ToString();
        // Two-word phrases
        foreach (var phrase in new[] { "ابن الكلب", "son of a bitch" })
        {
            int at = result.IndexOf(phrase, StringComparison.OrdinalIgnoreCase);
            while (at >= 0) { result = result.Substring(0, at) + new string('*', phrase.Length) + result.Substring(at + phrase.Length); at = result.IndexOf(phrase, StringComparison.OrdinalIgnoreCase); }
        }
        return result;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '@' || c == '$' || (c >= 'ً' && c <= 'ْ');

    public static bool IsBad(string word)
    {
        string w = Normalise(word);
        if (w.Length == 0) return false;
        if (BadWords.Contains(w)) return true;
        // Arabic words with the article or a prefix letter: الشرموط, وقحبة, يا...
        foreach (var pre in new[] { "ال", "و", "يا", "ف", "ب" })
            if (w.StartsWith(pre) && w.Length > pre.Length + 1 && BadWords.Contains(w.Substring(pre.Length))) return true;
        return false;
    }

    private static string Normalise(string word)
    {
        var sb = new StringBuilder(word.Length);
        char last = '\0';
        foreach (char ch in word.ToLowerInvariant())
        {
            char c = ch;
            if (c >= 'ً' && c <= 'ْ') continue;            // Arabic diacritics
            if (c == 'ـ') continue;                                    // tatweel
            switch (c)
            {
                case '0': c = 'o'; break;
                case '1': c = 'i'; break;
                case '3': c = 'e'; break;
                case '4': c = 'a'; break;
                case '5': c = 's'; break;
                case '@': c = 'a'; break;
                case '$': c = 's'; break;
                case 'أ': case 'إ': case 'آ': c = 'ا'; break;
                case 'ى': c = 'ي'; break;
            }
            if (c == last && c < 128) continue;                        // fuuuck -> fuck (Latin only)
            sb.Append(c);
            last = c;
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------------ reports

    /// <summary>Report a player: write the report file, block them, and return a mailto: link with the report.</summary>
    public static string Report(string name, IList<string> recentMessages, string reason)
    {
        var sb = new StringBuilder(512);
        sb.Append("Mazraa player report\n");
        sb.Append("Time (UTC): ").Append(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm")).Append('\n');
        sb.Append("Reported player: ").Append(name).Append('\n');
        sb.Append("Reason: ").Append(string.IsNullOrEmpty(reason) ? "Not given" : reason).Append('\n');
        sb.Append("Recent messages:\n");
        if (recentMessages != null) foreach (var m in recentMessages) sb.Append("  ").Append(m).Append('\n');
        string body = sb.ToString();
        try { File.AppendAllText(ReportPath, body + "----\n"); }
        catch (Exception e) { Debug.LogWarning("[ChatSafety] Could not save the report: " + e.Message); }
        SetBlocked(name, true);
        return "mailto:" + ReportEmail + "?subject=" + Uri.EscapeDataString("Mazraa player report: " + name) + "&body=" + Uri.EscapeDataString(body);
    }
}
