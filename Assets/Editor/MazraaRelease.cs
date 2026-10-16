using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using UnityEditor;
using Debug = UnityEngine.Debug;

/// <summary>
/// Store / distribution packaging for Windows (menu Mazraa > Release):
///   release/Mazraa_&lt;version&gt;_Windows/          clean game folder (no *_DoNotShip debug files) + README, EULA, CREDITS
///   release/Mazraa_&lt;version&gt;_Windows.zip        that folder zipped (itch.io, Epic, direct download, Steam depot upload)
///   release/Mazraa_Setup_&lt;version&gt;.exe          Windows installer (Inno Setup, installer/Mazraa.iss): EN/AR, shortcuts,
///                                                uninstaller. Built when Inno Setup 6 (free, jrsoftware.org) is installed.
/// See installer/STORE_RELEASE.md for what each store needs.
/// </summary>
public static class MazraaRelease
{
    public const string BuildDir = "build/Windows/Mazraa";
    public const string ReleaseDir = "release";

    [MenuItem("Mazraa/Release/Build Windows + package (zip + installer)")]
    public static void BuildAndPackage()
    {
        MazraaBuild.Windows();
        if (File.Exists(Path.Combine(BuildDir, "Mazraa.exe"))) Package();
    }

    [MenuItem("Mazraa/Release/Package last Windows build (zip + installer)")]
    public static void Package()
    {
        string version = PlayerSettings.bundleVersion;
        if (!File.Exists(Path.Combine(BuildDir, "Mazraa.exe")))
        {
            EditorUtility.DisplayDialog("Mazraa release", "No Windows build found. Run Mazraa > Build Windows (.exe) first.", "OK");
            return;
        }
        string name = "Mazraa_" + version + "_Windows";
        string folder = Path.Combine(ReleaseDir, name);
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
        CopyClean(BuildDir, folder);
        foreach (var doc in new[] { "README.txt", "EULA.txt" })
        {
            string src = Path.Combine("installer", doc);
            if (File.Exists(src)) File.Copy(src, Path.Combine(folder, doc), true);
        }
        if (File.Exists("CREDITS.md")) File.Copy("CREDITS.md", Path.Combine(folder, "CREDITS.txt"), true);

        string zip = Path.Combine(ReleaseDir, name + ".zip");
        if (File.Exists(zip)) File.Delete(zip);
        ZipFile.CreateFromDirectory(folder, zip, CompressionLevel.Optimal, true);
        Debug.Log("[Mazraa] Release folder: " + Path.GetFullPath(folder) + "\n[Mazraa] Zip: " + Path.GetFullPath(zip));

        string setup = BuildInstaller(folder, version);
        string msg = "Release ready in " + Path.GetFullPath(ReleaseDir) + ":\n\n- " + name + " (folder)\n- " + name + ".zip";
        msg += setup != null ? "\n- " + Path.GetFileName(setup) + " (installer)"
                             : "\n\nInstaller skipped: install Inno Setup 6 (free, jrsoftware.org/isdl.php) and run Mazraa > Release > Package again.";
        EditorUtility.RevealInFinder(zip);
        EditorUtility.DisplayDialog("Mazraa release", msg, "OK");
    }

    private static void CopyClean(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
        foreach (var d in Directory.GetDirectories(src))
        {
            string n = Path.GetFileName(d);
            if (n.EndsWith("_DoNotShip", StringComparison.OrdinalIgnoreCase) || n.EndsWith("_BackUpThisFolder_ButDontShipItWithYourGame", StringComparison.OrdinalIgnoreCase)) continue;
            CopyClean(d, Path.Combine(dst, n));
        }
    }

    /// <summary>Compile installer/Mazraa.iss with Inno Setup's ISCC.exe. Returns the setup path or null.</summary>
    private static string BuildInstaller(string folder, string version)
    {
        string iss = Path.GetFullPath("installer/Mazraa.iss");
        string iscc = FindIscc();
        if (iscc == null || !File.Exists(iss)) { Debug.LogWarning("[Mazraa] Inno Setup 6 not found: installer skipped (zip and folder are ready)."); return null; }
        var psi = new ProcessStartInfo(iscc,
            "/Q \"/DMyAppVersion=" + version + "\" \"/DSourceDir=" + Path.GetFullPath(folder) + "\" \"/DOutputDir=" + Path.GetFullPath(ReleaseDir) + "\" \"" + iss + "\"")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        try
        {
            using (var p = Process.Start(psi))
            {
                string err = p.StandardError.ReadToEnd();
                p.WaitForExit();
                if (p.ExitCode != 0) { Debug.LogError("[Mazraa] Inno Setup failed (" + p.ExitCode + "): " + err); return null; }
            }
        }
        catch (Exception e) { Debug.LogError("[Mazraa] Could not run Inno Setup: " + e.Message); return null; }
        string setup = Path.Combine(ReleaseDir, "Mazraa_Setup_" + version + ".exe");
        Debug.Log("[Mazraa] Installer: " + Path.GetFullPath(setup));
        return File.Exists(setup) ? setup : null;
    }

    private static string FindIscc()
    {
        string[] dirs =
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
        };
        foreach (var d in dirs)
        {
            if (string.IsNullOrEmpty(d)) continue;
            string p = Path.Combine(d, "Inno Setup 6", "ISCC.exe");
            if (File.Exists(p)) return p;
        }
        return null;
    }
}
