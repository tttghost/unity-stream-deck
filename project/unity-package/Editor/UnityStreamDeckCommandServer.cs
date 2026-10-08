using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityStreamDeck.Editor
{
    /// <summary>
    /// Minimal loopback WebSocket server for Stream Deck commands. It uses only
    /// .NET Standard APIs and dispatches all Unity API calls on the main thread.
    /// </summary>
    [InitializeOnLoad]
    internal static class UnityStreamDeckCommandServer
    {
        internal const int Port = 18765;

        private const string LogPrefix = "[UnityStreamDeck]";
        private const string AutoConnectPreference = "UnityStreamDeck.AutoConnect";
        private const string WebSocketPath = "/unitystreamdeck/";
        private const string WebSocketMagic = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
        private const int MaxHeaderBytes = 8 * 1024;
        private const int MaxMessageBytes = 64 * 1024;

        private static readonly ConcurrentQueue<string> PendingMessages = new ConcurrentQueue<string>();
        private static readonly ConcurrentDictionary<TcpClient, ClientConnection> ActiveClients =
            new ConcurrentDictionary<TcpClient, ClientConnection>();
        private static readonly ConcurrentQueue<bool> PendingStateSync = new ConcurrentQueue<bool>();
        private static string pendingScreenshotPath;
        private static int pendingScreenshotWidth;
        private static int pendingScreenshotHeight;
        private static bool pendingScreenshotCopyToClipboard;
        private static string lastEditorStatePayload;
        private static string buildStatus = "idle";
        private static CancellationTokenSource shutdown;
        private static TcpListener listener;
        private static bool connectionRequested;
        private static double nextStartAttempt;
        private static string lastStartError;

        internal static bool IsListening => listener != null;
        internal static int ConnectedClientCount => ActiveClients.Count;
        internal static bool AutoConnect
        {
            get => EditorPrefs.GetBool(AutoConnectPreference, true);
            set => EditorPrefs.SetBool(AutoConnectPreference, value);
        }

        static UnityStreamDeckCommandServer()
        {
            // InitializeOnLoad also runs in import workers; only the main editor owns the port.
            if (AssetDatabase.IsAssetImportWorkerProcess() || Application.isBatchMode)
                return;

            EditorApplication.update += DispatchPendingMessages;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.pauseStateChanged += OnPauseStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += Stop;
            connectionRequested = AutoConnect;
            if (connectionRequested)
                Start();
        }

        private static void Start()
        {
            if (AssetDatabase.IsAssetImportWorkerProcess() || Application.isBatchMode || listener != null)
                return;

            nextStartAttempt = EditorApplication.timeSinceStartup + 2;
            TcpListener candidate = new TcpListener(IPAddress.Loopback, Port);
            try
            {
                candidate.Start();
                shutdown = new CancellationTokenSource();
                listener = candidate;
                lastStartError = null;
                Debug.Log($"{LogPrefix} Command server listening: ws://127.0.0.1:{Port}{WebSocketPath}");
                CancellationToken token = shutdown.Token;
                _ = Task.Run(() => AcceptClients(token));
            }
            catch (Exception exception)
            {
                candidate.Stop();
                StopServer();
                if (lastStartError != exception.Message)
                {
                    lastStartError = exception.Message;
                    Debug.LogWarning($"{LogPrefix} Command server could not start; retrying: {exception.Message}");
                }
            }
        }

        internal static void Connect()
        {
            AutoConnect = true;
            connectionRequested = true;
            Start();
        }

        internal static void Disconnect()
        {
            connectionRequested = false;
            StopServer();
        }

        internal static void Reconnect()
        {
            AutoConnect = true;
            connectionRequested = true;
            StopServer();
            Start();
        }

        private static async Task AcceptClients(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    TcpClient client = await listener.AcceptTcpClientAsync();
                    _ = HandleClient(client, cancellationToken);
                }
            }
            catch (Exception exception) when (
                cancellationToken.IsCancellationRequested ||
                exception is SocketException ||
                exception is ObjectDisposedException)
            {
                // Listener shutdown is expected during assembly reload and editor quit.
            }
        }

        private static async Task HandleClient(TcpClient client, CancellationToken cancellationToken)
        {
            try
            {
                using (client)
                using (NetworkStream stream = client.GetStream())
                {
                    ClientConnection connection = null;
                    try
                    {
                        if (!await CompleteHandshake(stream, cancellationToken))
                        {
                            return;
                        }

                        connection = new ClientConnection(client, stream);
                        ActiveClients.TryAdd(client, connection);
                        PendingStateSync.Enqueue(true);

                        while (!cancellationToken.IsCancellationRequested)
                        {
                            WebSocketFrame frame = await ReadFrame(stream, cancellationToken);
                            if (frame == null)
                            {
                                return;
                            }

                            if (frame.OpCode == 0x8)
                            {
                                await connection.WriteFrame(0x8, frame.Payload, cancellationToken);
                                return;
                            }

                            if (frame.OpCode == 0x9)
                            {
                                await connection.WriteFrame(0xA, frame.Payload, cancellationToken);
                                continue;
                            }

                            if (frame.OpCode == 0x1)
                            {
                                PendingMessages.Enqueue(Encoding.UTF8.GetString(frame.Payload));
                            }
                        }
                    }
                    catch (Exception exception) when (
                        cancellationToken.IsCancellationRequested ||
                        exception is IOException ||
                        exception is SocketException ||
                        exception is ObjectDisposedException ||
                        exception is InvalidDataException)
                    {
                        // Normal disconnects and invalid clients do not stop the listener.
                    }
                }
            }
            finally
            {
                ActiveClients.TryRemove(client, out _);
            }
        }

        private static async Task<bool> CompleteHandshake(NetworkStream stream, CancellationToken cancellationToken)
        {
            string header = await ReadHttpHeader(stream, cancellationToken);
            string[] lines = header.Split(new[] { "\r\n" }, StringSplitOptions.None);

            if (lines.Length == 0 || !lines[0].StartsWith("GET " + WebSocketPath + " ", StringComparison.Ordinal))
            {
                return false;
            }

            string webSocketKey = null;
            foreach (string line in lines)
            {
                int separator = line.IndexOf(':');
                if (separator <= 0)
                {
                    continue;
                }

                if (line.Substring(0, separator).Trim().Equals(
                    "Sec-WebSocket-Key", StringComparison.OrdinalIgnoreCase))
                {
                    webSocketKey = line.Substring(separator + 1).Trim();
                    break;
                }
            }

            if (string.IsNullOrEmpty(webSocketKey))
            {
                return false;
            }

            byte[] acceptHash;
            using (SHA1 sha1 = SHA1.Create())
            {
                acceptHash = sha1.ComputeHash(Encoding.ASCII.GetBytes(webSocketKey + WebSocketMagic));
            }

            string response =
                "HTTP/1.1 101 Switching Protocols\r\n" +
                "Upgrade: websocket\r\n" +
                "Connection: Upgrade\r\n" +
                "Sec-WebSocket-Accept: " + Convert.ToBase64String(acceptHash) + "\r\n\r\n";

            byte[] responseBytes = Encoding.ASCII.GetBytes(response);
            await stream.WriteAsync(responseBytes, 0, responseBytes.Length, cancellationToken);
            return true;
        }

        private static async Task<string> ReadHttpHeader(NetworkStream stream, CancellationToken cancellationToken)
        {
            var header = new MemoryStream();
            byte[] oneByte = new byte[1];
            int matchedTerminators = 0;
            byte[] terminator = { 13, 10, 13, 10 };

            while (header.Length < MaxHeaderBytes)
            {
                await ReadExactly(stream, oneByte, 0, 1, cancellationToken);
                header.WriteByte(oneByte[0]);

                matchedTerminators = oneByte[0] == terminator[matchedTerminators]
                    ? matchedTerminators + 1
                    : oneByte[0] == terminator[0] ? 1 : 0;

                if (matchedTerminators == terminator.Length)
                {
                    return Encoding.ASCII.GetString(header.ToArray());
                }
            }

            throw new InvalidDataException("WebSocket handshake header is too large.");
        }

        private static async Task<WebSocketFrame> ReadFrame(NetworkStream stream, CancellationToken cancellationToken)
        {
            byte[] header = new byte[2];
            await ReadExactly(stream, header, 0, header.Length, cancellationToken);

            bool isFinal = (header[0] & 0x80) != 0;
            int opCode = header[0] & 0x0F;
            bool isMasked = (header[1] & 0x80) != 0;
            ulong payloadLength = (ulong)(header[1] & 0x7F);

            if (!isFinal || !isMasked)
            {
                throw new InvalidDataException("Only final, masked client frames are supported.");
            }

            if (payloadLength == 126)
            {
                byte[] lengthBytes = new byte[2];
                await ReadExactly(stream, lengthBytes, 0, lengthBytes.Length, cancellationToken);
                payloadLength = (ulong)((lengthBytes[0] << 8) | lengthBytes[1]);
            }
            else if (payloadLength == 127)
            {
                byte[] lengthBytes = new byte[8];
                await ReadExactly(stream, lengthBytes, 0, lengthBytes.Length, cancellationToken);
                payloadLength = 0;
                for (int i = 0; i < lengthBytes.Length; i++)
                {
                    payloadLength = (payloadLength << 8) | lengthBytes[i];
                }
            }

            if (payloadLength > MaxMessageBytes)
            {
                throw new InvalidDataException("WebSocket message is too large.");
            }

            byte[] mask = new byte[4];
            await ReadExactly(stream, mask, 0, mask.Length, cancellationToken);

            byte[] payload = new byte[(int)payloadLength];
            await ReadExactly(stream, payload, 0, payload.Length, cancellationToken);
            for (int i = 0; i < payload.Length; i++)
            {
                payload[i] ^= mask[i % mask.Length];
            }

            return new WebSocketFrame(opCode, payload);
        }

        private static async Task WriteFrame(
            NetworkStream stream,
            int opCode,
            byte[] payload,
            CancellationToken cancellationToken)
        {
            if (payload.Length <= 125)
            {
                await stream.WriteAsync(new[]
                {
                    (byte)(0x80 | opCode),
                    (byte)payload.Length
                }, 0, 2, cancellationToken);
            }
            else if (payload.Length <= ushort.MaxValue)
            {
                byte[] header =
                {
                    (byte)(0x80 | opCode),
                    126,
                    (byte)(payload.Length >> 8),
                    (byte)payload.Length
                };
                await stream.WriteAsync(header, 0, header.Length, cancellationToken);
            }
            else
            {
                byte[] header = new byte[10];
                header[0] = (byte)(0x80 | opCode);
                header[1] = 127;
                ulong length = (ulong)payload.Length;
                for (int index = 0; index < 8; index++)
                {
                    header[9 - index] = (byte)(length >> (index * 8));
                }

                await stream.WriteAsync(header, 0, header.Length, cancellationToken);
            }

            await stream.WriteAsync(payload, 0, payload.Length, cancellationToken);
        }

        private static async Task ReadExactly(
            Stream stream,
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            while (count > 0)
            {
                int bytesRead = await stream.ReadAsync(buffer, offset, count, cancellationToken);
                if (bytesRead == 0)
                {
                    throw new IOException("Remote endpoint closed the connection.");
                }

                offset += bytesRead;
                count -= bytesRead;
            }
        }

        private static void DispatchPendingMessages()
        {
            if (connectionRequested && listener == null && EditorApplication.timeSinceStartup >= nextStartAttempt)
                Start();

            bool shouldSyncState = false;
            while (PendingStateSync.TryDequeue(out _))
            {
                shouldSyncState = true;
            }
            if (shouldSyncState)
            {
                BroadcastPlayState();
            }

            while (PendingMessages.TryDequeue(out string json))
            {
                CommandEnvelope envelope;
                try
                {
                    envelope = JsonUtility.FromJson<CommandEnvelope>(json);
                }
                catch (ArgumentException)
                {
                    Debug.LogWarning($"{LogPrefix} Ignored invalid JSON command.");
                    continue;
                }

                if (envelope == null || envelope.version != 1 || envelope.type != "unity.command")
                {
                    Debug.LogWarning($"{LogPrefix} Ignored unsupported command envelope.");
                    continue;
                }

                try
                {
                    ExecuteCommand(envelope);
                }
                catch (TargetInvocationException exception)
                {
                    Debug.LogError($"{LogPrefix} Command Failed ({envelope.command}): " +
                                   (exception.InnerException?.Message ?? exception.Message));
                }
                catch (Exception exception)
                {
                    Debug.LogError($"{LogPrefix} Command Failed ({envelope.command}): {exception.Message}");
                }
            }

            BroadcastEditorStateIfChanged();
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            BroadcastPlayState();
        }

        private static void OnPauseStateChanged(PauseState state)
        {
            BroadcastPlayState();
        }

        private static void BroadcastPlayState()
        {
            BroadcastEditorStateIfChanged(true);
        }

        private static void BroadcastEditorStateIfChanged(bool force = false)
        {
            bool isPlaying = EditorApplication.isPlaying;
            bool isPaused = EditorApplication.isPaused;
            string mode = isPaused
                ? "paused"
                : isPlaying ? "playing" : "stopped";
            GetConsoleCounts(out int consoleErrors, out int consoleWarnings);
            string payloadText =
                $"{{\"version\":1,\"type\":\"unity.state\",\"playMode\":\"{mode}\"," +
                $"\"isPlaying\":{isPlaying.ToString().ToLowerInvariant()}," +
                $"\"isPaused\":{isPaused.ToString().ToLowerInvariant()}," +
                $"\"focusedWindow\":\"{EditorWindowFocusTracker.CurrentWindowName}\"," +
                $"\"sceneName\":\"{EscapeJson(SceneManager.GetActiveScene().name)}\"," +
                $"\"consoleErrors\":{consoleErrors}," +
                $"\"consoleWarnings\":{consoleWarnings}," +
                $"\"isCompiling\":{EditorApplication.isCompiling.ToString().ToLowerInvariant()}," +
                $"\"isUpdating\":{EditorApplication.isUpdating.ToString().ToLowerInvariant()}," +
                $"\"buildStatus\":\"{buildStatus}\"}}";
            if (!force && payloadText == lastEditorStatePayload)
                return;
            lastEditorStatePayload = payloadText;
            byte[] payload = Encoding.UTF8.GetBytes(payloadText);

            foreach (ClientConnection connection in ActiveClients.Values)
            {
                _ = connection.WriteFrame(0x1, payload, shutdown?.Token ?? CancellationToken.None);
            }
        }

        private static string EscapeJson(string value)
        {
            return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static void GetConsoleCounts(out int errors, out int warnings)
        {
            errors = 0;
            warnings = 0;
            try
            {
                Type logEntries = typeof(EditorApplication).Assembly.GetType("UnityEditor.LogEntries");
                MethodInfo method = logEntries?.GetMethod("GetCountsByType", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (method == null) return;
                object[] values = { 0, 0, 0 };
                method.Invoke(null, values);
                errors = Convert.ToInt32(values[0]);
                warnings = Convert.ToInt32(values[1]);
            }
            catch
            {
                // Console counters are optional status information.
            }
        }

        private static void ExecuteCommand(CommandEnvelope envelope)
        {
            UnityStreamDeckCommandDispatcher.Execute(
                envelope.command, envelope.argument, envelope.argument2, envelope.path);
        }

        // Command implementation entry points are kept here for now so the
        // dispatcher has no access to transport internals or protocol types.
        internal static void ClearConsoleCommand() => ClearConsole();
        internal static void BuildPlayerCommand() => BuildPlayer();
        internal static void ExecuteMenuCommand(string argument) => ExecuteMenuItem(argument);
        internal static void OpenEditorWindowCommand(string argument) => OpenEditorWindow(argument);
        internal static void OpenSceneCommand(string argument) => OpenSceneByName(argument);
        internal static void InvokeStaticMethodCommand(string argument, string argument2) => InvokeStaticMethod(argument, argument2);
        internal static void CaptureGameViewCommand(string argument, string path) => CaptureGameView(argument, path);
        internal static void CopyGameViewToClipboardCommand(string argument, string argument2) => CopyGameViewToClipboard(argument, argument2);
        internal static void ChooseScreenshotPathCommand(string currentPath) => ChooseScreenshotPath(currentPath);
        internal static void OpenScreenshotFolderCommand(string path) => OpenScreenshotFolder(path);

        private static void ClearConsole()
        {
            Type logEntries = typeof(EditorApplication).Assembly.GetType("UnityEditor.LogEntries");
            MethodInfo clear = logEntries?.GetMethod("Clear", BindingFlags.Static | BindingFlags.Public);
            if (clear == null)
            {
                throw new InvalidOperationException("Unity Console clear API was not found.");
            }

            clear.Invoke(null, null);
        }

        private static void BuildPlayer()
        {
            buildStatus = "building";
            BroadcastEditorStateIfChanged(true);
            try
            {
                BuildPlayerInternal();
                buildStatus = "succeeded";
            }
            catch
            {
                buildStatus = "failed";
                throw;
            }
            finally
            {
                ActivateUnityEditor();
                BroadcastEditorStateIfChanged(true);
            }
        }

        private static void ActivateUnityEditor()
        {
            if (Application.platform != RuntimePlatform.OSXEditor)
                return;

            try
            {
                System.Diagnostics.Process.Start(
                    "/usr/bin/osascript",
                    "-e 'tell application \"Unity\" to activate'");
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"{LogPrefix} Could not activate Unity Editor: {exception.Message}");
            }
        }

        private static void BuildPlayerInternal()
        {
            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            string location = EditorUserBuildSettings.GetBuildLocation(target);
            if (string.IsNullOrWhiteSpace(location))
            {
                throw new InvalidOperationException(
                    "Build location is empty. Configure it once in File > Build Profiles/Build Settings.");
            }

            string[] scenes = Array.ConvertAll(
                Array.FindAll(EditorBuildSettings.scenes, scene => scene.enabled),
                scene => scene.path);
            if (scenes.Length == 0)
            {
                throw new InvalidOperationException("No enabled scenes exist in Build Profiles/Build Settings.");
            }

            BuildReportLogger.Log(BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = location,
                target = target,
                options = BuildOptions.None
            }));
        }

        private static void ExecuteMenuItem(string menuPath)
        {
            RequireArgument(menuPath, "Menu path");
            if (!EditorApplication.ExecuteMenuItem(menuPath))
            {
                throw new InvalidOperationException($"Unity MenuItem was not found: {menuPath}");
            }
        }

        private static void OpenEditorWindow(string typeName)
        {
            RequireArgument(typeName, "EditorWindow type name");
            Type type = FindType(typeName);
            if (type == null || !typeof(EditorWindow).IsAssignableFrom(type))
            {
                throw new InvalidOperationException($"EditorWindow type was not found: {typeName}");
            }

            EditorWindow.GetWindow(type).Focus();
        }

        private static void OpenSceneByName(string sceneName)
        {
            RequireArgument(sceneName, "Scene name");
            string requestedName = sceneName.Trim();
            string[] sceneGuids = AssetDatabase.FindAssets("t:Scene");
            List<string> matches = new List<string>();

            foreach (string sceneGuid in sceneGuids)
            {
                string scenePath = AssetDatabase.GUIDToAssetPath(sceneGuid);
                string fileName = Path.GetFileNameWithoutExtension(scenePath);
                if (fileName.Equals(requestedName, StringComparison.OrdinalIgnoreCase) ||
                    scenePath.Equals(requestedName, StringComparison.OrdinalIgnoreCase) ||
                    scenePath.Equals(requestedName + ".unity", StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(scenePath);
                }
            }

            if (matches.Count == 0)
                throw new InvalidOperationException($"Scene was not found: {requestedName}");
            if (matches.Count > 1)
                throw new InvalidOperationException(
                    $"Multiple scenes have the same name: {string.Join(", ", matches.ToArray())}");

            EditorSceneManager.OpenScene(matches[0], OpenSceneMode.Single);
        }

        private static void InvokeStaticMethod(string typeName, string methodName)
        {
            RequireArgument(typeName, "Type name");
            RequireArgument(methodName, "Method name");
            Type type = FindType(typeName);
            MethodInfo method = type?.GetMethod(
                methodName,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null);
            if (method == null || method.ContainsGenericParameters)
            {
                throw new InvalidOperationException(
                    $"Static parameterless method was not found: {typeName}.{methodName}");
            }

            method.Invoke(null, null);
        }

        private static Type FindType(string typeName)
        {
            Type type = Type.GetType(typeName, false);
            if (type != null)
            {
                return type;
            }

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(typeName, false);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        private static Type FindEditorType(string typeName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(typeName, false);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        private static void RequireArgument(string value, string label)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(label + " is required.");
            }
        }

        private static void CaptureGameView(string requestedPath, string requestedFolder)
        {
            CaptureGameView(requestedPath, requestedFolder, false, 0, 0);
        }

        private static void CaptureGameView(string requestedPath, string requestedFolder, bool copyToClipboard, int requestedWidth, int requestedHeight)
        {
            string folder = copyToClipboard ? Path.GetTempPath() : requestedFolder;
            if (!copyToClipboard && string.IsNullOrWhiteSpace(folder))
                folder = Path.Combine(Application.dataPath, "..", "Library", "UnityStreamDeck");
            string prefix = copyToClipboard ? "GameViewClipboard" :
                (string.IsNullOrWhiteSpace(requestedPath) ? "GameView" : requestedPath.Trim());
            // Migrate old settings that contained an absolute file path.
            if (prefix.Contains("/") || prefix.Contains("\\"))
                prefix = Path.GetFileNameWithoutExtension(prefix);
            else
                prefix = Path.GetFileNameWithoutExtension(prefix);
            if (string.IsNullOrWhiteSpace(prefix)) prefix = "GameView";
            foreach (char invalid in Path.GetInvalidFileNameChars()) prefix = prefix.Replace(invalid.ToString(), "_");
            string path = Path.Combine(folder, prefix + ".png");
            if (string.IsNullOrWhiteSpace(path))
            {
                Debug.Log($"{LogPrefix} Game View screenshot skipped: no output path.");
                return;
            }
            path = Path.GetFullPath(path);
            path = GetUniqueScreenshotPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            Type snapshotUtils = FindEditorType("UnityEditor.SceneTemplate.SnapshotUtils");
            Type gameViewType = FindEditorType("UnityEditor.GameView");
            MethodInfo takeSnapshot = snapshotUtils?.GetMethod(
                "TakeGameViewSnapshot", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            EditorWindow gameView = FindGameView(gameViewType);
            if (gameView == null)
            {
                if (!EditorApplication.ExecuteMenuItem("Window/General/Game"))
                {
                    throw new InvalidOperationException("Game View is not open and could not be opened automatically.");
                }

                EditorApplication.delayCall += () => CaptureGameView(path, requestedFolder, copyToClipboard, requestedWidth, requestedHeight);
                return;
            }

            if (EditorWindow.focusedWindow != gameView)
            {
                gameView.Focus();
                EditorApplication.delayCall += () => CaptureGameView(path, requestedFolder, copyToClipboard, requestedWidth, requestedHeight);
                return;
            }

            if (takeSnapshot == null)
            {
                CaptureGameViewPixels(gameView, path, copyToClipboard, requestedWidth, requestedHeight);
                return;
            }

            pendingScreenshotPath = path;
            pendingScreenshotCopyToClipboard = copyToClipboard;
            Vector2 targetSize = GetTargetRenderSize(gameView);
            pendingScreenshotWidth = requestedWidth > 0 ? requestedWidth : Mathf.Max(1, Mathf.RoundToInt(targetSize.x));
            pendingScreenshotHeight = requestedHeight > 0 ? requestedHeight : Mathf.Max(1, Mathf.RoundToInt(targetSize.y));
            ParameterInfo callbackParameter = takeSnapshot.GetParameters()[1];
            MethodInfo callbackMethod = typeof(UnityStreamDeckCommandServer).GetMethod(
                nameof(OnGameViewSnapshot), BindingFlags.Static | BindingFlags.NonPublic);
            Delegate callback = Delegate.CreateDelegate(callbackParameter.ParameterType, callbackMethod);
            takeSnapshot.Invoke(null, new object[] { gameView, callback, false });
        }

        private static void CopyGameViewToClipboard(string widthArgument, string heightArgument)
        {
            int width = ParseOptionalDimension(widthArgument, "가로 해상도");
            int height = ParseOptionalDimension(heightArgument, "세로 해상도");
            if ((width == 0) != (height == 0))
                throw new ArgumentException("가로와 세로 해상도는 둘 다 입력하거나 둘 다 비워야 합니다.");

            CaptureGameView("GameViewClipboard", null, true, width, height);
        }

        private static int ParseOptionalDimension(string value, string label)
        {
            if (string.IsNullOrWhiteSpace(value)) return 0;
            if (!int.TryParse(value.Trim(), out int dimension) || dimension <= 0)
                throw new ArgumentException(label + "은 1 이상의 정수여야 합니다.");
            return dimension;
        }

        private static string ChooseScreenshotPath(string currentPath)
        {
            string path = EditorUtility.OpenFolderPanel(
                "게임 뷰 캡처 폴더 선택",
                string.IsNullOrWhiteSpace(currentPath)
                    ? Path.Combine(Application.dataPath, "..")
                    : currentPath,
                "");
            if (!string.IsNullOrWhiteSpace(path))
            {
                Debug.Log($"{LogPrefix} Screenshot path selected: {path}");
                BroadcastScreenshotPath(Path.GetFullPath(path));
            }

            return path;
        }

        private static void OpenScreenshotFolder(string requestedFolder)
        {
            string folder = string.IsNullOrWhiteSpace(requestedFolder)
                ? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library", "UnityStreamDeck"))
                : Path.GetFullPath(requestedFolder);
            Directory.CreateDirectory(folder);
            if (Application.platform == RuntimePlatform.OSXEditor)
                System.Diagnostics.Process.Start("open", "\"" + folder.Replace("\"", "\\\"") + "\"");
            else if (Application.platform == RuntimePlatform.WindowsEditor)
                System.Diagnostics.Process.Start("explorer.exe", "\"" + folder + "\"");
            else
                System.Diagnostics.Process.Start("xdg-open", "\"" + folder + "\"");
        }

        private static void BroadcastScreenshotPath(string path)
        {
            string escapedPath = path.Replace("\\", "\\\\").Replace("\"", "\\\"");
            byte[] payload = Encoding.UTF8.GetBytes(
                "{\"version\":1,\"type\":\"unity.screenshotPath\",\"path\":" +
                "\"" + escapedPath + "\"}");
            foreach (ClientConnection connection in ActiveClients.Values)
                _ = connection.WriteFrame(0x1, payload, shutdown?.Token ?? CancellationToken.None);
        }

        private static void CaptureGameViewPixels(EditorWindow gameView, string path, bool copyToClipboard, int requestedWidth, int requestedHeight)
        {
            Type gameViewType = gameView.GetType();
            Rect window = gameView.position;
            Rect captureRect = window;
            PropertyInfo contentProperty = gameViewType.GetProperty(
                "targetInContent", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo pixelRectMethod = gameViewType.GetMethod(
                "GetViewPixelRect", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (contentProperty != null && pixelRectMethod != null)
            {
                Rect contentRect = (Rect)contentProperty.GetValue(gameView);
                Rect pixelRect = (Rect)pixelRectMethod.Invoke(gameView, new object[] { contentRect });
                if (pixelRect.width > 1f && pixelRect.height > 1f)
                {
                    captureRect = new Rect(
                        window.x + pixelRect.x,
                        window.y + pixelRect.y,
                        pixelRect.width,
                        pixelRect.height);
                }
            }

            int width = Mathf.Max(1, Mathf.RoundToInt(captureRect.width));
            int height = Mathf.Max(1, Mathf.RoundToInt(captureRect.height));
            PropertyInfo targetSizeProperty = gameViewType.GetProperty(
                "targetRenderSize", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Vector2 targetSize = targetSizeProperty != null
                ? (Vector2)targetSizeProperty.GetValue(gameView)
                : new Vector2(width, height);
            int outputWidth = requestedWidth > 0 ? requestedWidth : Mathf.Max(1, Mathf.RoundToInt(targetSize.x));
            int outputHeight = requestedHeight > 0 ? requestedHeight : Mathf.Max(1, Mathf.RoundToInt(targetSize.y));

            Color[] pixels = UnityEditorInternal.InternalEditorUtility.ReadScreenPixel(
                new Vector2(captureRect.x, captureRect.y), width, height);
            Texture2D source = new Texture2D(width, height, TextureFormat.RGBA32, false);
            source.SetPixels(pixels);
            source.Apply(false, false);
            Texture2D output = source;
            if (width != outputWidth || height != outputHeight)
            {
                output = ResizeTexture(source, outputWidth, outputHeight);
                UnityEngine.Object.DestroyImmediate(source);
            }

            byte[] png = output.EncodeToPNG();
            File.WriteAllBytes(path, png);
            if (copyToClipboard)
                CopyPngToClipboard(path);
            Debug.Log(copyToClipboard
                ? $"{LogPrefix} Game View copied to clipboard ({output.width}x{output.height})"
                : $"{LogPrefix} Game View screenshot saved: {path} ({output.width}x{output.height})");
            UnityEngine.Object.DestroyImmediate(output);
            DeleteTemporaryClipboardFile(path, copyToClipboard);
        }

        private static Texture2D ResizeTexture(Texture2D source, int width, int height)
        {
            RenderTexture renderTexture = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(source, renderTexture);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = renderTexture;
            Texture2D result = new Texture2D(width, height, TextureFormat.RGBA32, false);
            result.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            result.Apply(false, false);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(renderTexture);
            return result;
        }

        private static EditorWindow FindGameView(Type gameViewType)
        {
            if (gameViewType == null)
            {
                return null;
            }

            EditorWindow focused = EditorWindow.focusedWindow;
            if (focused != null && gameViewType.IsInstanceOfType(focused))
            {
                return focused;
            }

            foreach (EditorWindow window in Resources.FindObjectsOfTypeAll(gameViewType))
            {
                return window;
            }

            return null;
        }

        private static void OnGameViewSnapshot(Texture2D texture)
        {
            string path = pendingScreenshotPath;
            bool copyToClipboard = pendingScreenshotCopyToClipboard;
            pendingScreenshotPath = null;
            pendingScreenshotCopyToClipboard = false;
            if (texture == null || string.IsNullOrEmpty(path))
            {
                Debug.LogError($"{LogPrefix} Game View screenshot returned no image.");
                return;
            }

            // SnapshotUtils may return a GPU/compressed texture. Always copy it into
            // an uncompressed RGBA32 texture before calling EncodeToPNG.
            Texture2D readable = ConvertToReadableTexture(texture);
            if (pendingScreenshotWidth > 0 && pendingScreenshotHeight > 0 &&
                (readable.width != pendingScreenshotWidth || readable.height != pendingScreenshotHeight))
            {
                Texture2D resized = ResizeTexture(readable, pendingScreenshotWidth, pendingScreenshotHeight);
                UnityEngine.Object.DestroyImmediate(readable);
                readable = resized;
            }
            pendingScreenshotWidth = 0;
            pendingScreenshotHeight = 0;
            int width = readable.width;
            int height = readable.height;
            File.WriteAllBytes(path, readable.EncodeToPNG());
            if (copyToClipboard)
                CopyPngToClipboard(path);
            UnityEngine.Object.DestroyImmediate(texture);
            UnityEngine.Object.DestroyImmediate(readable);
            Debug.Log(copyToClipboard
                ? $"{LogPrefix} Game View copied to clipboard ({width}x{height})"
                : $"{LogPrefix} Game View screenshot saved: {path} ({width}x{height})");
            DeleteTemporaryClipboardFile(path, copyToClipboard);
        }

        private static void CopyPngToClipboard(string path)
        {
            try
            {
                if (Application.platform == RuntimePlatform.OSXEditor)
                {
                    string escaped = path.Replace("\\", "\\\\").Replace("\"", "\\\"");
                    RunProcess("/usr/bin/osascript", "-e", "set the clipboard to (read POSIX file \"" + escaped + "\" as «class PNGf»)" );
                }
                else if (Application.platform == RuntimePlatform.WindowsEditor)
                {
                    string escaped = path.Replace("'", "''");
                    string script = "Add-Type -AssemblyName System.Windows.Forms; Add-Type -AssemblyName System.Drawing; $i=[System.Drawing.Image]::FromFile('" + escaped + "'); [System.Windows.Forms.Clipboard]::SetImage($i)";
                    RunProcess("powershell.exe", "-NoProfile", "-STA", "-Command", script);
                }
                else
                {
                    throw new InvalidOperationException("Image clipboard is currently supported on macOS and Windows only.");
                }
            }
            catch (Exception exception)
            {
                Debug.LogError($"{LogPrefix} Could not copy Game View screenshot to clipboard: {exception.Message}");
            }
        }

        private static void RunProcess(string fileName, params string[] arguments)
        {
            using (System.Diagnostics.Process process = new System.Diagnostics.Process())
            {
                process.StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = fileName,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                process.StartInfo.Arguments = string.Join(" ", Array.ConvertAll(arguments, QuoteProcessArgument));
                process.Start();
                process.WaitForExit();
                if (process.ExitCode != 0)
                    throw new InvalidOperationException($"Clipboard helper exited with code {process.ExitCode}.");
            }
        }

        private static string QuoteProcessArgument(string argument)
        {
            return "\"" + (argument ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static void DeleteTemporaryClipboardFile(string path, bool copyToClipboard)
        {
            if (!copyToClipboard) return;
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception exception) { Debug.LogWarning($"{LogPrefix} Temporary clipboard file could not be deleted: {exception.Message}"); }
        }

        private static Vector2 GetTargetRenderSize(EditorWindow gameView)
        {
            PropertyInfo property = gameView.GetType().GetProperty(
                "targetRenderSize", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null)
            {
                object value = property.GetValue(gameView);
                if (value is Vector2 vector) return vector;
                if (value is Vector2Int vectorInt) return vectorInt;
            }
            return new Vector2(Mathf.Max(1, gameView.position.width), Mathf.Max(1, gameView.position.height));
        }

        private static string GetUniqueScreenshotPath(string path)
        {
            if (!File.Exists(path)) return path;
            string directory = Path.GetDirectoryName(path);
            string name = Path.GetFileNameWithoutExtension(path);
            string extension = Path.GetExtension(path);
            for (int index = 1; index < 100000; index++)
            {
                string candidate = Path.Combine(directory, name + "_" + index.ToString("D3") + extension);
                if (!File.Exists(candidate)) return candidate;
            }
            return Path.Combine(directory, name + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmssfff") + extension);
        }

        private static Texture2D ConvertToReadableTexture(Texture2D source)
        {
            RenderTexture renderTexture = RenderTexture.GetTemporary(
                source.width, source.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(source, renderTexture);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = renderTexture;
            Texture2D readable = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            readable.Apply(false, false);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(renderTexture);
            return readable;
        }

        private static void Stop()
        {
            EditorApplication.update -= DispatchPendingMessages;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.pauseStateChanged -= OnPauseStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= Stop;
            EditorApplication.quitting -= Stop;

            StopServer();
        }

        private static void StopServer()
        {
            shutdown?.Cancel();
            listener?.Stop();
            listener = null;

            foreach (TcpClient client in ActiveClients.Keys)
            {
                client.Close();
            }
            ActiveClients.Clear();
            shutdown?.Dispose();
            shutdown = null;
        }

        [Serializable]
        private sealed class CommandEnvelope
        {
            public int version;
            public string type;
            public string command;
            public string argument;
            public string argument2;
            public string path;
        }

        private static class BuildReportLogger
        {
            public static void Log(UnityEditor.Build.Reporting.BuildReport report)
            {
                if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Build finished with {report.summary.result}: {report.summary.totalErrors} error(s).");
                }
            }
        }

        private sealed class WebSocketFrame
        {
            public WebSocketFrame(int opCode, byte[] payload)
            {
                OpCode = opCode;
                Payload = payload;
            }

            public int OpCode { get; }
            public byte[] Payload { get; }
        }

        private sealed class ClientConnection
        {
            private readonly SemaphoreSlim writeLock = new SemaphoreSlim(1, 1);

            public ClientConnection(TcpClient client, NetworkStream stream)
            {
                Client = client;
                Stream = stream;
            }

            public TcpClient Client { get; }
            public NetworkStream Stream { get; }

            public async Task WriteFrame(int opCode, byte[] payload, CancellationToken cancellationToken)
            {
                await writeLock.WaitAsync(cancellationToken);
                try
                {
                    await UnityStreamDeckCommandServer.WriteFrame(Stream, opCode, payload, cancellationToken);
                }
                catch (Exception exception) when (
                    cancellationToken.IsCancellationRequested ||
                    exception is IOException ||
                    exception is SocketException ||
                    exception is ObjectDisposedException)
                {
                    // The read loop removes disconnected clients.
                }
                finally
                {
                    writeLock.Release();
                }
            }
        }
    }
}
