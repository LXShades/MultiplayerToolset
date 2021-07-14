﻿using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// The Playtest menu and tools for quickly building & playtesting networked games
/// </summary>
[InitializeOnLoad]
public class PlaytestEditorTools : MonoBehaviour
{
    private enum EditorRole
    {
        None = 0,
        Host = 1,
        Server = 2,
        Client = 3
    }

    public static int numTestPlayers
    {
        get => Mathf.Clamp(EditorPrefs.GetInt("netNumTestPlayers"), 1, 4);
        set => EditorPrefs.SetInt("netNumTestPlayers", value);
    }

    public static bool onlyBuildCurrentScene
    {
        get => EditorPrefs.GetBool("netOnlyBuildCurrentScene", false);
        set => EditorPrefs.SetBool("netOnlyBuildCurrentScene", value);
    }

    public static bool onlyBuildScripts
    {
        get => EditorPrefs.GetBool("netOnlyBuildScripts", false);
        set => EditorPrefs.SetBool("netOnlyBuildScripts", value);
    }

    public static string playtestBuildPath => $"{Application.dataPath.Substring(0, Application.dataPath.LastIndexOf('/'))}/Builds/Playtest/{Application.productName}";

    public static string webGlBuildPath => $"{Application.dataPath.Substring(0, Application.dataPath.LastIndexOf('/'))}/Builds/WebGL/{Application.productName}";
    public static string linuxBuildPath => $"{Application.dataPath.Substring(0, Application.dataPath.LastIndexOf('/'))}/Builds/Linux/{Application.productName}";

    private static EditorRole editorRole
    {
        get { return (EditorRole)EditorPrefs.GetInt("editorRole"); }
        set { EditorPrefs.SetInt("editorRole", (int)value); }
    }

    [MenuItem("Playtest/Build", priority = 1)]
    public static bool Build()
    {
        // Add the open scene to the list if it's not already in there
        List<string> levels = new List<string>();
        string activeScenePath = EditorSceneManager.GetActiveScene().path;

        if (onlyBuildCurrentScene)
        {
            if (EditorBuildSettings.scenes.Length > 0)
                levels.Add(EditorBuildSettings.scenes[0].path); // add the Boot scene

            levels.Add(activeScenePath);
        }
        else
        {
            bool addOpenScene = true;
            for (int i = 0; i < EditorBuildSettings.scenes.Length; i++)
            {
                if (EditorBuildSettings.scenes[i].enabled)
                {
                    levels.Add(EditorBuildSettings.scenes[i].path);

                    if (EditorBuildSettings.scenes[i].path == activeScenePath)
                        addOpenScene = false;
                }
            }

            // we haven't added this scene in the build settings, but we probably want to test it!
            if (addOpenScene)
                levels.Add(activeScenePath);
        }

        // Build and run the player, preserving the open scene
        string originalScene = EditorSceneManager.GetActiveScene().path;

        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            string buildName = $"{playtestBuildPath}/{Application.productName}.exe";
            BuildOptions buildOptions = BuildOptions.Development 
                | (onlyBuildScripts ? BuildOptions.BuildScriptsOnly : 0);

            UnityEditor.Build.Reporting.BuildReport buildReport = BuildPipeline.BuildPlayer(levels.ToArray(), buildName, BuildTarget.StandaloneWindows64, buildOptions);

            EditorSceneManager.OpenScene(originalScene);

            if (buildReport.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                EditorUtility.DisplayDialog("Someone goofed", $"Build failed ({buildReport.summary.totalErrors} errors)", "OK");
                return false;
            }
            else
            {
                return true;
            }
        }
        else
        {
            return false;
        }
    }

    [MenuItem("Playtest/Build && Run", priority = 20)]
    public static void BuildAndRun()
    {
        if (Build())
            Run();
    }


    [MenuItem("Playtest/Run", priority = 21)]
    public static void Run()
    {
        int playerIndex = 0;
        int numWindowsTotal = numTestPlayers;

        switch (editorRole)
        {
            case EditorRole.Client:
                CommandLine.editorCommands = new string[] { "-connect", "127.0.0.1" };
                RunBuild($"-host -scene {EditorSceneManager.GetActiveScene().path} {MakeDimensionParam(CalculateWindowDimensionsForPlayer(playerIndex++, numWindowsTotal))}");
                break;
            case EditorRole.Server:
                CommandLine.editorCommands = new string[] { "-host", "127.0.0.1", "-scene", EditorSceneManager.GetActiveScene().path };
                RunBuild($"-connect 127.0.0.1 {MakeDimensionParam(CalculateWindowDimensionsForPlayer(playerIndex++, numWindowsTotal))}");
                break;
            case EditorRole.Host:
                CommandLine.editorCommands = new string[] { "-host", "127.0.0.1", "-scene", EditorSceneManager.GetActiveScene().path };
                numWindowsTotal = numTestPlayers - 1;
                break;
            case EditorRole.None:
                numWindowsTotal = numTestPlayers + 1;
                RunBuild($"-host -scene {EditorSceneManager.GetActiveScene().path} {MakeDimensionParam(CalculateWindowDimensionsForPlayer(playerIndex++, numWindowsTotal))}");
                RunBuild($"-connect 127.0.0.1 {MakeDimensionParam(CalculateWindowDimensionsForPlayer(playerIndex++, numWindowsTotal))}");
                break;
        }

        // Connect the remaining players
        for (int i = 0; i < numTestPlayers - 1; i++)
            RunBuild($"-connect 127.0.0.1 {MakeDimensionParam(CalculateWindowDimensionsForPlayer(playerIndex++, numWindowsTotal))}");

        // Start the editor if applicable
        if (editorRole != EditorRole.None)
            EditorApplication.isPlaying = true;
    }


    [MenuItem("Playtest/Final/Server Build")]
    public static void BuildFinalServer()
    {
        string originalScene = EditorSceneManager.GetActiveScene().path;

        EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();

        UnityEditor.Build.Reporting.BuildReport buildReport = BuildPipeline.BuildPlayer(EditorBuildSettings.scenes, $"{linuxBuildPath}/build.x86_64", BuildTarget.StandaloneLinux64, BuildOptions.EnableHeadlessMode);

        EditorSceneManager.OpenScene(originalScene);

        if (buildReport.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
        {
            EditorUtility.DisplayDialog("Someone goofed", $"Build failed ({buildReport.summary.totalErrors} errors)", "OK");
        }
    }

    [MenuItem("Playtest/Final/WebGL Build")]
    public static void BuildFinalWebGL()
    {
        string originalScene = EditorSceneManager.GetActiveScene().path;

        EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();

        UnityEditor.Build.Reporting.BuildReport buildReport = BuildPipeline.BuildPlayer(EditorBuildSettings.scenes, $"{webGlBuildPath}/", BuildTarget.WebGL, BuildOptions.None);

        EditorSceneManager.OpenScene(originalScene);

        if (buildReport.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
        {
            EditorUtility.DisplayDialog("Someone goofed", $"Build failed ({buildReport.summary.totalErrors} errors)", "OK");
        }
    }

    [MenuItem("Playtest/Standalone Only", priority = 40)]
    private static void StandaloneOnly() { editorRole = EditorRole.None; }

    [MenuItem("Playtest/Standalone Only", true)]
    private static bool StandaloneOnlyValidate() { Menu.SetChecked("Playtest/Standalone Only", editorRole == EditorRole.None); return true; }

    [MenuItem("Playtest/Editor is Host", priority = 41)]
    private static void EditorIsHost() { editorRole = EditorRole.Host; }

    [MenuItem("Playtest/Editor is Host", true)]
    private static bool EditorIsHostValidate() { Menu.SetChecked("Playtest/Editor is Host", editorRole == EditorRole.Host); return true; }

    [MenuItem("Playtest/Editor is Server", priority = 42)]
    private static void EditorIsServer() { editorRole = EditorRole.Server; }

    [MenuItem("Playtest/Editor is Server", true)]
    private static bool EditorIsServerValidate() { Menu.SetChecked("Playtest/Editor is Server", editorRole == EditorRole.Server); return true; }

    [MenuItem("Playtest/Editor is Client", priority = 43)]
    private static void EditorIsClient() { editorRole = EditorRole.Client; }

    [MenuItem("Playtest/Editor is Client", true)]
    private static bool EditorIsClientValidate() { Menu.SetChecked("Playtest/Editor is Client", editorRole == EditorRole.Client); return true; }

    [MenuItem("Playtest/1 player", priority = 80)]
    private static void OneTestPlayer() { numTestPlayers = 1; }

    [MenuItem("Playtest/1 player", true)]
    private static bool OneTestPlayerValidate() { Menu.SetChecked("Playtest/1 player", numTestPlayers == 1); return true; }


    [MenuItem("Playtest/2 players", priority = 81)]
    private static void TwoTestPlayers() { numTestPlayers = 2; }

    [MenuItem("Playtest/2 players", true)]
    private static bool TwoTestPlayersValidate() { Menu.SetChecked("Playtest/2 players", numTestPlayers == 2); return true; }


    [MenuItem("Playtest/3 players", priority = 82)]
    private static void ThreeTestPlayers() { numTestPlayers = 3; }

    [MenuItem("Playtest/3 players", true)]
    private static bool ThreeTestPlayersValidate() { Menu.SetChecked("Playtest/3 players", numTestPlayers == 3); return true; }


    [MenuItem("Playtest/4 players", priority = 83)]
    private static void FourTestPlayers() { numTestPlayers = 4; }

    [MenuItem("Playtest/4 players", true)]
    private static bool FourTestPlayersValidate() { Menu.SetChecked("Playtest/4 players", numTestPlayers == 4); return true; }


    [MenuItem("Playtest/Only current scene", priority = 120)]
    private static void OnlyCurrentScene() { onlyBuildCurrentScene = !onlyBuildCurrentScene; }

    [MenuItem("Playtest/Only current scene", true)]
    private static bool OnlyCurrentSceneValidate() { Menu.SetChecked("Playtest/Only current scene", onlyBuildCurrentScene); return true; }

    [MenuItem("Playtest/Only scripts", priority = 121)]
    private static void OnlyScripts() { onlyBuildScripts = !onlyBuildScripts; }

    [MenuItem("Playtest/Only scripts", true)]
    private static bool OnlyScriptsValidate() { Menu.SetChecked("Playtest/Only scripts", onlyBuildScripts); return true; }

    private static void RunBuild(string arguments = "")
    {
        // Run another instance of the game
        System.Diagnostics.Process process = new System.Diagnostics.Process();

        process.StartInfo.FileName = $"{playtestBuildPath}/{Application.productName}.exe";
        process.StartInfo.WorkingDirectory = playtestBuildPath;
        process.StartInfo.Arguments = arguments;

        process.Start();
    }

    private static string MakeDimensionParam(RectInt dimensions) => $"" +
        $"-pos {dimensions.x} {dimensions.y} " +
        $"-screen-fullscreen 0 -screen-width {dimensions.width} -screen-height {dimensions.height}";

    private static RectInt CalculateWindowDimensionsForPlayer(int playerIndex, int numPlayers)
    {
        RectInt screen = new RectInt(0, 0, Screen.currentResolution.width, Screen.currentResolution.height);

        if (numPlayers == 1 || numPlayers > 4)
            return new RectInt(screen.width / 4, screen.height / 4, screen.width / 2, screen.height / 2);
        else if (numPlayers == 2)
            return new RectInt(screen.width / 2 * playerIndex, screen.height / 4, screen.width / 2, screen.height / 2);
        else if (numPlayers <= 4)
            return new RectInt(screen.width / 2 * (playerIndex % 2), screen.height / 2 * (playerIndex / 2), screen.width / 2, screen.height / 2);
        return default;
    }
}