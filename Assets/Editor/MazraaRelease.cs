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

    [MenuItem("Mazraa/Release/Build Windows + all store packages (zip + installer + MSIX)")]
    public static void BuildAll()
    {
        MazraaBuild.Windows();
        if (!File.Exists(Path.Combine(BuildDir, "Mazraa.exe"))) return;
        Package();
        Msix();
    }

    /// <summary>
    /// Command-line build of everything (no dialogs), e.g.
    /// Unity.exe -batchmode -quit -projectPath . -executeMethod MazraaRelease.BuildAllBatch -logFile release/build.log
    /// Exit code 0 when the exe, zip and MSIX were made (the installer is optional).
    /// </summary>
    public static void BuildAllBatch()
    {
        int code = 1;
        try
        {
            if (MazraaBuild.WindowsNoExit() && File.Exists(Path.Combine(BuildDir, "Mazraa.exe")))
            {
                Package();
                Msix();
                string v = PlayerSettings.bundleVersion;
                bool zip = File.Exists(Path.Combine(ReleaseDir, "Mazraa_" + v + "_Windows.zip"));
                bool msix = File.Exists(Path.Combine(ReleaseDir, "Mazraa_" + v + "_x64.msix"));
                Debug.Log("[Mazraa] Batch release: zip=" + zip + " msix=" + msix + " installer=" + File.Exists(Path.Combine(ReleaseDir, "Mazraa_Setup_" + v + ".exe")));
                if (zip && msix) code = 0;
            }
        }
        catch (Exception e) { Debug.LogError("[Mazraa] Batch release failed: " + e); }
        if (UnityEngine.Application.isBatchMode) EditorApplication.Exit(code);
    }

    private static bool Batch => UnityEngine.Application.isBatchMode;

    /// <summary>
    /// Microsoft Store package: release/Mazraa_&lt;ver&gt;_x64.msix (unsigned; the Store signs it). Identity values come from
    /// installer/msix/identity.json (copy them from Partner Center > Product identity before submitting).
    /// </summary>
    [MenuItem("Mazraa/Release/Microsoft Store package (.msix)")]
    public static void Msix()
    {
        string version = PlayerSettings.bundleVersion;
        string folder = Path.Combine(ReleaseDir, "Mazraa_" + version + "_Windows");
        if (!Directory.Exists(folder)) Package();
        if (!Directory.Exists(folder)) return;
        string stage = Path.Combine(ReleaseDir, "msix_stage");
        if (Directory.Exists(stage)) Directory.Delete(stage, true);
        CopyClean(folder, stage);
        CopyClean("installer/msix/Assets", Path.Combine(stage, "Assets"));
        var id = UnityEngine.JsonUtility.FromJson<MsixIdentity>(File.ReadAllText("installer/msix/identity.json"));
        string v = version; int parts = v.Split('.').Length; while (parts++ < 4) v += ".0";
        string manifest = File.ReadAllText("installer/msix/AppxManifest.template.xml")
            .Replace("{IdentityName}", Esc(id.identityName)).Replace("{Publisher}", Esc(id.publisher))
            .Replace("{PublisherDisplayName}", Esc(id.publisherDisplayName)).Replace("{DisplayName}", Esc(id.displayName))
            .Replace("{Description}", Esc(id.description)).Replace("{Version}", v);
        File.WriteAllText(Path.Combine(stage, "AppxManifest.xml"), manifest);
        string outFile = Path.Combine(ReleaseDir, "Mazraa_" + version + "_x64.msix");
        try
        {
            EditorUtility.DisplayProgressBar("Mazraa", "Packing Microsoft Store package (.msix)...", 0.5f);
            MsixPacker.Pack(stage, outFile);
        }
        finally { EditorUtility.ClearProgressBar(); }
        Directory.Delete(stage, true);
        bool placeholder = id.publisher.Contains("00000000");
        Debug.Log("[Mazraa] MSIX: " + Path.GetFullPath(outFile) + (placeholder ? "  (placeholder identity: set installer/msix/identity.json from Partner Center before submitting)" : ""));
        if (!Batch) EditorUtility.RevealInFinder(outFile);
    }

    [System.Serializable] private class MsixIdentity { public string identityName, publisher, publisherDisplayName, displayName, description; }
    private static string Esc(string s) => (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    [MenuItem("Mazraa/Release/Package last Windows build (zip + installer)")]
    public static void Package()
    {
        string version = PlayerSettings.bundleVersion;
        if (!File.Exists(Path.Combine(BuildDir, "Mazraa.exe")))
        {
            if (Batch) Debug.LogError("[Mazraa] No Windows build found.");
            else EditorUtility.DisplayDialog("Mazraa release", "No Windows build found. Run Mazraa > Build Windows (.exe) first.", "OK");
            return;
        }
        string name = "Mazraa_" + version + "_Windows";
        string folder = Path.Combine(ReleaseDir, name);
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
        CopyClean(BuildDir, folder);
        foreach (var doc in new[] { "README.txt", "EULA.txt", "PRIVACY.txt" })
        {
            string src = Path.Combine("installer", doc);
            if (File.Exists(src)) File.Copy(src, Path.Combine(folder, doc), true);
        }
        foreach (var c in new[] { "CREDITS.md", "Assets/ThirdParty/CREDITS.md" })
            if (File.Exists(c)) { File.Copy(c, Path.Combine(folder, "CREDITS.txt"), true); break; }

        string zip = Path.Combine(ReleaseDir, name + ".zip");
        if (File.Exists(zip)) File.Delete(zip);
        ZipFile.CreateFromDirectory(folder, zip, CompressionLevel.Optimal, true);
        Debug.Log("[Mazraa] Release folder: " + Path.GetFullPath(folder) + "\n[Mazraa] Zip: " + Path.GetFullPath(zip));

        string setup = BuildInstaller(folder, version);
        string msg = "Release ready in " + Path.GetFullPath(ReleaseDir) + ":\n\n- " + name + " (folder)\n- " + name + ".zip";
        msg += setup != null ? "\n- " + Path.GetFileName(setup) + " (installer)"
                             : "\n\nInstaller skipped: " + (lastInstallerError ?? "Inno Setup 6 was not found") + ". See the Console for details.";
        Debug.Log("[Mazraa] " + msg);
        if (Batch) return;
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
    private static string lastInstallerError;

    private static string BuildInstaller(string folder, string version)
    {
        lastInstallerError = null;
        string iss = Path.GetFullPath("installer/Mazraa.iss");
        string iscc = FindIscc();
        if (!File.Exists(iss)) { lastInstallerError = "installer/Mazraa.iss is missing"; Debug.LogWarning("[Mazraa] " + lastInstallerError); return null; }
        if (iscc == null) { lastInstallerError = "Inno Setup 6 (ISCC.exe) was not found - install it from jrsoftware.org/isdl.php or set the INNO_SETUP environment variable to its folder"; Debug.LogWarning("[Mazraa] " + lastInstallerError); return null; }
        var psi = new ProcessStartInfo(iscc,
            "/Q \"/DMyAppVersion=" + version + "\" \"/DSourceDir=" + Path.GetFullPath(folder) + "\" \"/DOutputDir=" + Path.GetFullPath(ReleaseDir) + "\" \"" + iss + "\"")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        try
        {
            using (var p = Process.Start(psi))
            {
                var outTask = p.StandardOutput.ReadToEndAsync();   // read both pipes so ISCC never blocks on a full buffer
                string err = p.StandardError.ReadToEnd();
                p.WaitForExit();
                string output = outTask.Result;
                if (p.ExitCode != 0)
                {
                    lastInstallerError = "Inno Setup failed with code " + p.ExitCode;
                    Debug.LogError("[Mazraa] " + lastInstallerError + ":\n" + err + "\n" + output);
                    return null;
                }
            }
        }
        catch (Exception e) { lastInstallerError = "could not run Inno Setup (" + e.Message + ")"; Debug.LogError("[Mazraa] " + lastInstallerError); return null; }
        string setup = Path.Combine(ReleaseDir, "Mazraa_Setup_" + version + ".exe");
        if (!File.Exists(setup)) { lastInstallerError = "Inno Setup ran but " + Path.GetFileName(setup) + " was not created (check OutputBaseFilename in installer/Mazraa.iss)"; Debug.LogError("[Mazraa] " + lastInstallerError); return null; }
        Debug.Log("[Mazraa] Installer: " + Path.GetFullPath(setup));
        return setup;
    }

    private static string FindIscc()
    {
        string env = Environment.GetEnvironmentVariable("INNO_SETUP");
        if (!string.IsNullOrEmpty(env))
        {
            string e = env.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? env : Path.Combine(env, "ISCC.exe");
            if (File.Exists(e)) return e;
        }
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            try { string e = Path.Combine(dir.Trim('"'), "ISCC.exe"); if (dir.Length > 0 && File.Exists(e)) return e; } catch (ArgumentException) { }
        }
        string[] dirs =
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
            Environment.GetEnvironmentVariable("ProgramW6432"),
            @"C:\Program Files (x86)", @"C:\Program Files",
        };
        foreach (var d in dirs)
        {
            if (string.IsNullOrEmpty(d)) continue;
            foreach (var ver in new[] { "Inno Setup 6", "Inno Setup 7" })
            {
                string p = Path.Combine(d, ver, "ISCC.exe");
                if (File.Exists(p)) return p;
            }
        }
        return null;
    }
}
