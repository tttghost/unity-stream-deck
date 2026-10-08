using UnityEditor;
using UnityEngine;

namespace UnityStreamDeck.Editor
{
    internal sealed class UnityStreamDeckConnectionWindow : EditorWindow
    {
        [MenuItem("Window/General/Unity Stream Deck")]
        private static void Open()
        {
            UnityStreamDeckConnectionWindow window = GetWindow<UnityStreamDeckConnectionWindow>();
            window.titleContent = new GUIContent("Unity Stream Deck");
            window.minSize = new Vector2(320f, 190f);
            window.Show();
        }

        private void OnEnable()
        {
            EditorApplication.update += Repaint;
        }

        private void OnDisable()
        {
            EditorApplication.update -= Repaint;
        }

        private void OnGUI()
        {
            GUILayout.Label("Unity Stream Deck 연결", EditorStyles.boldLabel);
            GUILayout.Space(6f);

            bool listening = UnityStreamDeckCommandServer.IsListening;
            GUIStyle statusStyle = new GUIStyle(EditorStyles.helpBox)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleCenter,
                margin = new RectOffset(0, 0, 4, 8)
            };
            statusStyle.normal.textColor = listening
                ? new Color(0.25f, 0.8f, 0.35f)
                : new Color(0.9f, 0.35f, 0.3f);
            GUILayout.Label(listening ? "● 연결됨" : "● 연결 해제됨", statusStyle);

            EditorGUILayout.LabelField("주소", $"ws://127.0.0.1:{UnityStreamDeckCommandServer.Port}/unitystreamdeck/");
            EditorGUILayout.LabelField("접속한 클라이언트", UnityStreamDeckCommandServer.ConnectedClientCount.ToString());
            GUILayout.Space(8f);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!listening))
                {
                    if (GUILayout.Button("연결 해제", GUILayout.Height(28f)))
                        UnityStreamDeckCommandServer.Disconnect();
                }

                using (new EditorGUI.DisabledScope(listening))
                {
                    if (GUILayout.Button("다시 연결", GUILayout.Height(28f)))
                        UnityStreamDeckCommandServer.Reconnect();
                }
            }

            bool autoConnect = EditorGUILayout.ToggleLeft(
                "에디터 시작 시 자동 연결", UnityStreamDeckCommandServer.AutoConnect);
            if (autoConnect != UnityStreamDeckCommandServer.AutoConnect)
            {
                UnityStreamDeckCommandServer.AutoConnect = autoConnect;
                if (autoConnect)
                    UnityStreamDeckCommandServer.Connect();
                else
                    UnityStreamDeckCommandServer.Disconnect();
            }

            EditorGUILayout.HelpBox(
                "자동 연결은 기본값으로 켜져 있습니다. Stream Deck 플러그인은 Unity Editor가 실행되면 자동으로 재연결합니다.",
                MessageType.Info);
        }
    }
}
