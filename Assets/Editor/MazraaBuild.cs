using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

/// <summary>Menu Mazraa > Build WebGL / Windows / Android, also used by the GitHub Actions builds.</summary>
public static class MazraaBuild
{
    [MenuItem("Mazraa/Build WebGL")]
    public static void WebGL()
    {
        // GitHub Pages does not send Brotli/Gzip headers, so build uncompressed (or gzip with the JS fallback).
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
        PlayerSettings.WebGL.decompressionFallback = true;
        PlayerSettings.WebGL.dataCaching = true;
        string outDir = "build/WebGL/Mazraa";
        Directory.CreateDirectory(outDir);
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        var report = BuildPipeline.BuildPlayer(scenes, outDir, BuildTarget.WebGL, BuildOptions.None);
        Debug.Log("Mazraa WebGL build: " + report.summary.result + " -> " + outDir);
        if (Application.isBatchMode) EditorApplication.Exit(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
    }

    [MenuItem("Mazraa/Build Windows (.exe)")]
    public static void Windows()
    {
        string outDir = "build/Windows/Mazraa";
        Directory.CreateDirectory(outDir);
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        var report = BuildPipeline.BuildPlayer(scenes, outDir + "/Mazraa.exe", BuildTarget.StandaloneWindows64, BuildOptions.None);
        Debug.Log("Mazraa Windows build: " + report.summary.result + " -> " + outDir);
        if (Application.isBatchMode) EditorApplication.Exit(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
    }

    /// <summary>Android APK: IL2CPP, ARM64 only, debug-signed unless a keystore is set in Player Settings.
    /// Output build/Android/Mazraa.apk. On phones the game shows on-screen controls (TouchControls).</summary>
    [MenuItem("Mazraa/Build Android (.apk)")]
    public static void Android()
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
        var android = NamedBuildTarget.Android;
        PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        string id = PlayerSettings.GetApplicationIdentifier(android);
        if (string.IsNullOrEmpty(id) || id.Contains("unity.template")) PlayerSettings.SetApplicationIdentifier(android, "com.almadarek.mazraa");
        EditorUserBuildSettings.buildAppBundle = false;
        string outDir = "build/Android";
        Directory.CreateDirectory(outDir);
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        var report = BuildPipeline.BuildPlayer(scenes, outDir + "/Mazraa.apk", BuildTarget.Android, BuildOptions.None);
        Debug.Log("Mazraa Android build: " + report.summary.result + " -> " + outDir + "/Mazraa.apk");
        if (Application.isBatchMode) EditorApplication.Exit(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
    }
}
