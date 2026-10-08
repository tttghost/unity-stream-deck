using UnityEditor;
using UnityEngine;

namespace UnityStreamDeck.Editor
{
    /// <summary>
    /// Maps protocol command names to Unity Editor operations. Transport and
    /// WebSocket frame handling remain in UnityStreamDeckCommandServer.
    /// </summary>
    internal static class UnityStreamDeckCommandDispatcher
    {
        internal static void Execute(string command, string argument, string argument2, string path)
        {
            switch (command)
            {
                case "play":
                    EditorApplication.isPlaying = true;
                    break;
                case "pause":
                    EditorApplication.isPaused = !EditorApplication.isPaused;
                    break;
                case "stop":
                    EditorApplication.isPlaying = false;
                    EditorApplication.isPaused = false;
                    break;
                case "play.toggle":
                    if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
                    {
                        EditorApplication.isPaused = false;
                        EditorApplication.isPlaying = false;
                    }
                    else
                    {
                        EditorApplication.isPlaying = true;
                    }
                    break;
                case "console.clear":
                    UnityStreamDeckCommandServer.ClearConsoleCommand();
                    break;
                case "build":
                    UnityStreamDeckCommandServer.BuildPlayerCommand();
                    break;
                case "menu.execute":
                    UnityStreamDeckCommandServer.ExecuteMenuCommand(argument);
                    break;
                case "window.open":
                    UnityStreamDeckCommandServer.OpenEditorWindowCommand(argument);
                    break;
                case "scene.open":
                    UnityStreamDeckCommandServer.OpenSceneCommand(argument);
                    break;
                case "method.invoke":
                    UnityStreamDeckCommandServer.InvokeStaticMethodCommand(argument, argument2);
                    break;
                case "screenshot":
                    UnityStreamDeckCommandServer.CaptureGameViewCommand(argument, path);
                    break;
                case "screenshot.copy":
                    UnityStreamDeckCommandServer.CopyGameViewToClipboardCommand(argument, argument2);
                    break;
                case "screenshot.choose":
                    UnityStreamDeckCommandServer.ChooseScreenshotPathCommand(path);
                    break;
                case "screenshot.open-folder":
                    UnityStreamDeckCommandServer.OpenScreenshotFolderCommand(path);
                    break;
                default:
                    Debug.LogWarning($"[UnityStreamDeck] Unknown command: {command}");
                    return;
            }

            Debug.Log($"[UnityStreamDeck] Command Executed: {command}");
        }
    }
}
