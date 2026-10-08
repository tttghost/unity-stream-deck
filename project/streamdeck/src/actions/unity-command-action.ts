import streamDeck from "@elgato/streamdeck";
import {
  action,
  type KeyAction,
  type KeyDownEvent,
  SingletonAction,
  type WillAppearEvent,
  type WillDisappearEvent
} from "@elgato/streamdeck";
import { unityConnection, type UnityCommand } from "../unity-connection.js";
import { BuildButton } from "../build-button.js";
import { compactCount, compactKeyTitle } from "../key-title.js";

export abstract class SectionSeparatorAction extends SingletonAction {
  override async onKeyDown(): Promise<void> {
    // Visual section label only. It intentionally performs no command.
  }
}

@action({ UUID: "com.tttghost.stream-deck-unity.separator.play-mode" })
export class PlayModeSeparatorAction extends SectionSeparatorAction {}

@action({ UUID: "com.tttghost.stream-deck-unity.separator.capture" })
export class CaptureSeparatorAction extends SectionSeparatorAction {}

@action({ UUID: "com.tttghost.stream-deck-unity.separator.editor-tools" })
export class EditorToolsSeparatorAction extends SectionSeparatorAction {}

@action({ UUID: "com.tttghost.stream-deck-unity.separator.custom-commands" })
export class CustomCommandsSeparatorAction extends SectionSeparatorAction {}

@action({ UUID: "com.tttghost.stream-deck-unity.separator.status" })
export class StatusSeparatorAction extends SectionSeparatorAction {}

abstract class UnityCommandAction extends SingletonAction {
  protected abstract readonly command: UnityCommand;

  override async onKeyDown(ev: KeyDownEvent): Promise<void> {
    if (!unityConnection.send(this.command)) {
      await ev.action.showAlert();
    }
  }
}

@action({ UUID: "com.tttghost.stream-deck-unity.pause" })
export class PauseAction extends UnityCommandAction {
  protected readonly command = "pause";
  private readonly visibleActions = new Map<string, KeyAction>();

  constructor() {
    super();
    unityConnection.onStateChanged(state => {
      for (const visibleAction of this.visibleActions.values()) {
        void this.updateIcon(visibleAction, state.isPaused);
      }
    });
  }

  override async onWillAppear(ev: WillAppearEvent): Promise<void> {
    if (ev.action.isKey()) {
      this.visibleActions.set(ev.action.id, ev.action);
      await this.updateIcon(ev.action, unityConnection.getState().isPaused);
    }
  }

  override onWillDisappear(ev: WillDisappearEvent): void {
    this.visibleActions.delete(ev.action.id);
  }

  private async updateIcon(action: KeyAction, isPaused: boolean): Promise<void> {
    const state = isPaused ? 1 : 0;
    await action.setImage(isPaused ? "imgs/keys/play@2x.png" : "imgs/keys/pause@2x.png", { state });
    await action.setState(state);
  }
}

@action({ UUID: "com.tttghost.stream-deck-unity.play-stop-toggle" })
export class PlayStopToggleAction extends UnityCommandAction {
  protected readonly command = "play.toggle";
  private readonly visibleActions = new Map<string, KeyAction>();

  constructor() {
    super();
    unityConnection.onStateChanged(state => {
      for (const visibleAction of this.visibleActions.values()) {
        void this.updateIcon(visibleAction, !state.isPlaying);
      }
    });
  }

  override async onWillAppear(ev: WillAppearEvent): Promise<void> {
    if (ev.action.isKey()) {
      this.visibleActions.set(ev.action.id, ev.action);
      await this.updateIcon(ev.action, !unityConnection.getState().isPlaying);
    }
  }

  override onWillDisappear(ev: WillDisappearEvent): void {
    this.visibleActions.delete(ev.action.id);
  }

  private async updateIcon(action: KeyAction, isStopped: boolean): Promise<void> {
    const state = isStopped ? 0 : 1;
    await action.setImage(isStopped ? "imgs/keys/play@2x.png" : "imgs/keys/stop@2x.png", { state });
    await action.setState(state);
  }
}

@action({ UUID: "com.tttghost.stream-deck-unity.console-clear" })
export class ConsoleClearAction extends UnityCommandAction {
  protected readonly command = "console.clear";
}

@action({ UUID: "com.tttghost.stream-deck-unity.build" })
export class BuildAction extends SingletonAction {
  private readonly model = new BuildButton();
  private readonly rendered = new Map<string, string>();
  private readonly visibleActions = new Map<string, KeyAction>();
  private updates: Promise<void> = Promise.resolve();
  private startTimer?: ReturnType<typeof setTimeout>;
  private countdownTimer?: ReturnType<typeof setTimeout>;

  constructor() {
    super();
    unityConnection.onStateChanged(state => {
      this.model.update(state.buildStatus, state.isCompiling || state.isUpdating);
      if (state.buildStatus === "building") clearTimeout(this.startTimer);
      if (this.model.phase !== "countdown") clearTimeout(this.countdownTimer);
      void this.render();
    });
    unityConnection.onConnectionChanged(connected => {
      if (!connected) {
        clearTimeout(this.startTimer);
        clearTimeout(this.countdownTimer);
        this.model.connectionLost();
        void this.render();
      }
    });
  }

  override async onWillAppear(ev: WillAppearEvent): Promise<void> {
    if (ev.action.isKey()) {
      this.visibleActions.set(ev.action.id, ev.action);
      await this.render();
    }
  }

  override onWillDisappear(ev: WillDisappearEvent): void {
    this.visibleActions.delete(ev.action.id);
    this.rendered.delete(ev.action.id);
    if (this.visibleActions.size === 0) {
      this.model.cancelCountdown();
      clearTimeout(this.countdownTimer);
    }
  }

  override async onKeyDown(ev: KeyDownEvent): Promise<void> {
    const result = this.model.press(unityConnection.isConnected(), () => unityConnection.send("build"));
    clearTimeout(this.countdownTimer);
    if (result === "countdown") this.scheduleCountdown();
    if (result === "started") {
      clearTimeout(this.startTimer);
      // Only time out an unacknowledged request, never a running Unity build.
      this.startTimer = setTimeout(() => {
        this.model.startTimedOut();
        void this.render();
      }, 15000);
      this.startTimer.unref();
    }
    if (result === "disconnected") await ev.action.showAlert();
    await this.render();
  }

  private scheduleCountdown(): void {
    this.countdownTimer = setTimeout(() => {
      const result = this.model.tick();
      if (result === "waiting") this.scheduleCountdown();
      void this.render();
    }, 1000);
    this.countdownTimer.unref();
  }

  private render(): Promise<void> {
    this.updates = this.updates.then(async () => {
      const { title, image } = this.model.display;
      for (const key of this.visibleActions.values()) {
        const signature = `${title}|${image}`;
        if (this.rendered.get(key.id) === signature) continue;
        await key.setTitle(title);
        await key.setImage(image);
        this.rendered.set(key.id, signature);
      }
    }).catch(error => {
      streamDeck.logger.error("Unable to update build button", error);
    });
    return this.updates;
  }
}

abstract class StatusAction extends SingletonAction {
  protected readonly visibleActions = new Map<string, KeyAction>();

  constructor() {
    super();
    unityConnection.onStateChanged(state => {
      for (const visibleAction of this.visibleActions.values()) {
        void this.updateStatus(visibleAction, state);
      }
    });
  }

  override async onWillAppear(ev: WillAppearEvent): Promise<void> {
    if (ev.action.isKey()) {
      this.visibleActions.set(ev.action.id, ev.action);
      await this.updateStatus(ev.action, unityConnection.getState());
    }
  }

  override onWillDisappear(ev: WillDisappearEvent): void {
    this.visibleActions.delete(ev.action.id);
  }

  protected abstract updateStatus(action: KeyAction, state: ReturnType<typeof unityConnection.getState>): Promise<void>;
}

@action({ UUID: "com.tttghost.stream-deck-unity.console-status" })
export class ConsoleStatusAction extends StatusAction {
  protected async updateStatus(action: KeyAction, state: ReturnType<typeof unityConnection.getState>): Promise<void> {
    const title = state.consoleErrors > 0
      ? `오류 ${compactCount(state.consoleErrors)}`
      : state.consoleWarnings > 0 ? `경고 ${compactCount(state.consoleWarnings)}` : "정상";
    await action.setTitle(title);
    await action.setImage("imgs/keys/console@2x.png");
  }
}

@action({ UUID: "com.tttghost.stream-deck-unity.editor-status" })
export class EditorStatusAction extends StatusAction {
  protected async updateStatus(action: KeyAction, state: ReturnType<typeof unityConnection.getState>): Promise<void> {
    const windowNames: Record<string, string> = {
      SceneView: "씬", GameView: "게임", Inspector: "인스펙터", Console: "콘솔",
      Project: "프로젝트", Hierarchy: "계층"
    };
    const focused = windowNames[state.focusedWindow] || "에디터";
    const scene = compactKeyTitle(state.sceneName || "씬 없음");
    await action.setTitle(`${focused}\n${scene}`);
    await action.setImage("imgs/keys/editor-status@2x.png");
  }
}

type ConfigurableSettings = {
  argument?: string;
  argument2?: string;
  screenshotPath?: string;
};

abstract class ConfigurableUnityAction extends SingletonAction<ConfigurableSettings> {
  protected abstract readonly command: UnityCommand;

  override async onKeyDown(ev: KeyDownEvent<ConfigurableSettings>): Promise<void> {
    const argument = ev.payload.settings.argument?.trim();
    const argument2 = ev.payload.settings.argument2?.trim();
    const screenshotPath = this.command === "screenshot"
      ? ev.payload.settings.screenshotPath?.trim()
      : undefined;
    if ((!argument && !argument2 && this.command !== "screenshot" && this.command !== "screenshot.copy") ||
        (this.command === "screenshot.copy" && ((argument && !argument2) || (!argument && argument2))) ||
        (this.command === "method.invoke" && !argument2)) {
      await ev.action.showAlert();
      return;
    }

    if (!unityConnection.send({ command: this.command, argument, argument2, path: screenshotPath })) {
      await ev.action.showAlert();
    }
  }
}

@action({ UUID: "com.tttghost.stream-deck-unity.execute-menu" })
export class ExecuteMenuAction extends ConfigurableUnityAction {
  protected readonly command = "menu.execute";
}

@action({ UUID: "com.tttghost.stream-deck-unity.open-window" })
export class OpenWindowAction extends ConfigurableUnityAction {
  protected readonly command = "window.open";
}

@action({ UUID: "com.tttghost.stream-deck-unity.scene-open" })
export class OpenSceneAction extends ConfigurableUnityAction {
  protected readonly command = "scene.open";
}

@action({ UUID: "com.tttghost.stream-deck-unity.invoke-method" })
export class InvokeMethodAction extends ConfigurableUnityAction {
  protected readonly command = "method.invoke";
}

@action({ UUID: "com.tttghost.stream-deck-unity.screenshot" })
export class ScreenshotAction extends ConfigurableUnityAction {
  protected readonly command = "screenshot";
}

@action({ UUID: "com.tttghost.stream-deck-unity.screenshot-copy" })
export class ScreenshotCopyAction extends ConfigurableUnityAction {
  protected readonly command = "screenshot.copy";
}
