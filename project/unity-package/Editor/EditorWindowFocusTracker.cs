using UnityEditor;
using UnityEngine;

namespace UnityStreamDeck.Editor
{
    /// <summary>
    /// Converts the currently focused Unity editor window into a stable context name.
    /// This class is editor-only because it lives in an Editor assembly.
    /// </summary>
    [InitializeOnLoad]
    internal static class EditorWindowFocusTracker
    {
        private const string LogPrefix = "[UnityStreamDeck]";

        private static EditorWindowKind lastWindowKind = EditorWindowKind.None;

        internal static string CurrentWindowName => lastWindowKind.ToString();

        static EditorWindowFocusTracker()
        {
            if (AssetDatabase.IsAssetImportWorkerProcess() || Application.isBatchMode)
                return;

            EditorWindow.windowFocusChanged -= OnWindowFocusChanged;
            EditorWindow.windowFocusChanged += OnWindowFocusChanged;

            // focusedWindow is not guaranteed to be ready inside a static constructor.
            EditorApplication.delayCall += OnWindowFocusChanged;
        }

        private static void OnWindowFocusChanged()
        {
            EditorWindowKind windowKind = Classify(EditorWindow.focusedWindow);

            if (windowKind == EditorWindowKind.None)
            {
                // Reset so returning from an untracked window logs the context again.
                lastWindowKind = EditorWindowKind.None;
                return;
            }

            if (windowKind == lastWindowKind)
            {
                return;
            }

            lastWindowKind = windowKind;
            Debug.Log($"{LogPrefix} Focus Changed: {windowKind}");
        }

        private static EditorWindowKind Classify(EditorWindow window)
        {
            if (window == null)
            {
                return EditorWindowKind.None;
            }

            // Most built-in windows except SceneView are internal UnityEditor types.
            // Matching their concrete CLR type names avoids localized tab-title checks.
            switch (window.GetType().Name)
            {
                case nameof(SceneView):
                    return EditorWindowKind.SceneView;
                case "GameView":
                    return EditorWindowKind.GameView;
                case "InspectorWindow":
                    return EditorWindowKind.Inspector;
                case "ConsoleWindow":
                    return EditorWindowKind.Console;
                case "ProjectBrowser":
                    return EditorWindowKind.Project;
                case "SceneHierarchyWindow":
                    return EditorWindowKind.Hierarchy;
                default:
                    return EditorWindowKind.None;
            }
        }

        private enum EditorWindowKind
        {
            None,
            SceneView,
            GameView,
            Inspector,
            Console,
            Project,
            Hierarchy
        }
    }
}
