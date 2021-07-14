﻿using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools to decide how the editor behaves and loads in playmode
/// 
/// Typically this loads the Boot scene and passes a fake command line for CommandLine to pick up in play
/// </summary>
[InitializeOnLoad]
public static class PlaymodeTools
{
    private enum PlayModeCommands
    {
        None,
        Host,
        Connect,
        Custom
    }

    private static PlayModeCommands playModeCommandType
    {
        get => (PlayModeCommands)EditorPrefs.GetInt("_playModeCommands", (int)PlayModeCommands.Host);
        set => EditorPrefs.SetInt("_playModeCommands", (int)value);
    }

    private static string playModeCommandLine
    {
        get => EditorPrefs.GetString("_playModeCommandLineParms", "");
        set => EditorPrefs.SetString("_playModeCommandLineParms", value);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void OnInit()
    {

        // Be prepared to set editor commands on play mode
        EditorApplication.playModeStateChanged += OnPlayStateChanged;

        // Set default command parameters
        SetEditorCommands();
    }

    private static void OnPlayStateChanged(PlayModeStateChange change)
    {
        // before starting, make sure the boot scene loads first
        if (EditorBuildSettings.scenes.Length > 0)
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(EditorBuildSettings.scenes[0].path);

        // when finished playing, do not let previous editor commands persist to the next test
        if (change == PlayModeStateChange.ExitingPlayMode)
            CommandLine.editorCommands = new string[0];
    }

    private static void SetEditorCommands()
    {
        if (CommandLine.editorCommands.Length == 0 || (CommandLine.editorCommands.Length == 1 && CommandLine.editorCommands[0] == ""))
        {
            string[] editorCommands = new string[0];
            switch (playModeCommandType)
            {
                case PlayModeCommands.Host:
                    editorCommands = new string[] { "-host" };
                    break;
                case PlayModeCommands.Connect:
                    editorCommands = new string[] { "-connect", "127.0.0.1" };
                    break;
                case PlayModeCommands.Custom:
                    editorCommands = playModeCommandLine.Split(' ');
                    break;
            }

            Debug.Log($"Setting PlayMode command line: {string.Join(" ", editorCommands)}");
            CommandLine.editorCommands = editorCommands;
        }
    }

    [MenuItem("Playtest/Autohost in Playmode", false, 100)]
    static void AutoHostOutsideBoot()
    {
        playModeCommandType = PlayModeCommands.Host;
    }

    [MenuItem("Playtest/Autohost in Playmode", validate = true)]
    static bool AutoHostOutsideBootValidate()
    {
        Menu.SetChecked("Playtest/Autohost in Playmode", playModeCommandType == PlayModeCommands.Host);
        return true;
    }


    [MenuItem("Playtest/Autoconnect in Playmode", false, 101)]
    static void AutoConnectOutsideBoot()
    {
        playModeCommandType = PlayModeCommands.Connect;
    }

    [MenuItem("Playtest/Autoconnect in Playmode", validate = true)]
    static bool AutoConnectOutsideBootValidate()
    {
        Menu.SetChecked("Playtest/Autoconnect in Playmode", playModeCommandType == PlayModeCommands.Connect);
        return true;
    }

    [MenuItem("Playtest/Custom Playmode Commands...", false, 102)]
    static void SetDefaultCommands()
    {
        DefaultCommandLineBox window = DefaultCommandLineBox.CreateInstance<DefaultCommandLineBox>();
        window.position = new Rect(Screen.width / 2, Screen.height / 2, 250, 150);
        window.tempCommands = playModeCommandLine;
        playModeCommandType = PlayModeCommands.Custom;
        window.ShowUtility();
    }

    [MenuItem("Playtest/Custom Playmode Commands...", validate = true)]
    static bool SetDefaultCommandsValidate()
    {
        Menu.SetChecked("Playtest/Custom Playmode Commands...", playModeCommandType == PlayModeCommands.Custom);
        return true;
    }

    /// <summary>
    /// Window to let the user assign custom command line parameters
    /// </summary>
    private class DefaultCommandLineBox : EditorWindow
    {
        public string tempCommands = "";

        void OnGUI()
        {
            GUILayout.Space(10);
            EditorGUILayout.LabelField("Type commands here!\n-host: Hosts a server with a local player\n-server: Hosts a server only\n-connect [ip]: Connects to the given IP address", EditorStyles.wordWrappedLabel);
            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            tempCommands = EditorGUILayout.TextField("Commands:", tempCommands);
            GUILayout.EndHorizontal();
            GUILayout.Space(20);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Done"))
            {
                PlaymodeTools.playModeCommandLine = tempCommands;
                Close();
            }
            if (GUILayout.Button("Cancel"))
            {
                Close();
            }
            EditorGUILayout.EndHorizontal();
        }
    }
}