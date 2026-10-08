import streamDeck from "@elgato/streamdeck";
import WebSocket from "ws";

const UNITY_ENDPOINT = "ws://127.0.0.1:18765/unitystreamdeck/";
const RECONNECT_DELAY_MS = 2000;

export type UnityCommand =
  | "play"
  | "pause"
  | "stop"
  | "play.toggle"
  | "console.clear"
  | "build"
  | "menu.execute"
  | "window.open"
  | "scene.open"
  | "method.invoke"
  | "screenshot"
  | "screenshot.copy"
  | "screenshot.choose"
  | "screenshot.open-folder";

export interface UnityCommandRequest {
  command: UnityCommand;
  argument?: string;
  argument2?: string;
  path?: string;
}

export interface UnityMessage {
  version?: number;
  type?: string;
  path?: string;
}

export type UnityPlayMode = "stopped" | "playing" | "paused";
export interface UnityEditorState {
  playMode: UnityPlayMode;
  isPlaying: boolean;
  isPaused: boolean;
  focusedWindow: string;
  sceneName: string;
  consoleErrors: number;
  consoleWarnings: number;
  isCompiling: boolean;
  isUpdating: boolean;
  buildStatus: "idle" | "building" | "succeeded" | "failed";
}
type StateListener = (state: UnityEditorState) => void;

class UnityConnection {
  private socket?: WebSocket;
  private reconnectTimer?: ReturnType<typeof setTimeout>;
  private stopped = false;
  private state: UnityEditorState = {
    playMode: "stopped", isPlaying: false, isPaused: false,
    focusedWindow: "None", sceneName: "", consoleErrors: 0, consoleWarnings: 0,
    isCompiling: false, isUpdating: false, buildStatus: "idle"
  };
  private readonly connectionListeners = new Set<(connected: boolean) => void>();
  private readonly stateListeners = new Set<StateListener>();
  private readonly messageListeners = new Set<(message: UnityMessage) => void>();

  start(): void {
    this.stopped = false;
    this.connect();
  }

  send(request: UnityCommand | UnityCommandRequest): boolean {
    const message = typeof request === "string" ? { command: request } : request;
    if (this.socket?.readyState !== WebSocket.OPEN) {
      streamDeck.logger.warn(`Unity is not connected; command ignored: ${message.command}`);
      this.scheduleReconnect();
      return false;
    }

    this.socket.send(JSON.stringify({
      version: 1,
      type: "unity.command",
      ...message
    }));
    streamDeck.logger.info(`Sent Unity command: ${message.command}`);
    return true;
  }

  isConnected(): boolean {
    return this.socket?.readyState === WebSocket.OPEN;
  }

  getState(): UnityEditorState {
    return this.state;
  }

  onConnectionChanged(listener: (connected: boolean) => void): void {
    this.connectionListeners.add(listener);
  }

  onStateChanged(listener: StateListener): () => void {
    this.stateListeners.add(listener);
    listener(this.state);
    return () => this.stateListeners.delete(listener);
  }

  onMessage(listener: (message: UnityMessage) => void): () => void {
    this.messageListeners.add(listener);
    return () => this.messageListeners.delete(listener);
  }

  stop(): void {
    this.stopped = true;
    if (this.reconnectTimer) {
      clearTimeout(this.reconnectTimer);
      this.reconnectTimer = undefined;
    }
    this.socket?.close();
    this.socket = undefined;
  }

  private connect(): void {
    if (this.stopped || this.socket?.readyState === WebSocket.CONNECTING ||
        this.socket?.readyState === WebSocket.OPEN) {
      return;
    }

    streamDeck.logger.info(`Connecting to Unity at ${UNITY_ENDPOINT}`);
    const socket = new WebSocket(UNITY_ENDPOINT);
    this.socket = socket;

    socket.addEventListener("open", () => {
      streamDeck.logger.info("Connected to Unity Editor");
      for (const listener of this.connectionListeners) listener(true);
    });

    socket.addEventListener("close", () => {
      if (this.socket === socket) {
        this.socket = undefined;
        for (const listener of this.connectionListeners) listener(false);
      }
      this.scheduleReconnect();
    });

    socket.addEventListener("message", event => {
      try {
        const message = JSON.parse(event.data.toString()) as UnityMessage & {
          playMode?: UnityPlayMode;
          isPlaying?: boolean;
          isPaused?: boolean;
          focusedWindow?: string;
          sceneName?: string;
          consoleErrors?: number;
          consoleWarnings?: number;
          isCompiling?: boolean;
          isUpdating?: boolean;
          buildStatus?: UnityEditorState["buildStatus"];
        };
        for (const listener of this.messageListeners) {
          listener(message);
        }
        if (message.version === 1 && message.type === "unity.state" &&
            (message.playMode === "stopped" || message.playMode === "playing" || message.playMode === "paused")) {
          this.state = {
            playMode: message.playMode,
            isPlaying: message.isPlaying ?? (message.playMode === "playing" || message.playMode === "paused"),
            isPaused: message.isPaused ?? message.playMode === "paused",
            focusedWindow: message.focusedWindow ?? "None",
            sceneName: message.sceneName ?? "",
            consoleErrors: message.consoleErrors ?? 0,
            consoleWarnings: message.consoleWarnings ?? 0,
            isCompiling: message.isCompiling ?? false,
            isUpdating: message.isUpdating ?? false,
            buildStatus: message.buildStatus ?? "idle"
          };
          streamDeck.logger.info(
            `Unity state changed: playing=${this.state.isPlaying}, paused=${this.state.isPaused}`
          );
          for (const listener of this.stateListeners) {
            listener(this.state);
          }
        }
      } catch {
        streamDeck.logger.warn("Ignored invalid message from Unity Editor");
      }
    });

    socket.addEventListener("error", () => {
      streamDeck.logger.warn("Unable to connect to Unity Editor");
    });
  }

  private scheduleReconnect(): void {
    if (this.stopped || this.reconnectTimer) {
      return;
    }

    this.reconnectTimer = setTimeout(() => {
      this.reconnectTimer = undefined;
      this.connect();
    }, RECONNECT_DELAY_MS);
  }
}

export const unityConnection = new UnityConnection();
