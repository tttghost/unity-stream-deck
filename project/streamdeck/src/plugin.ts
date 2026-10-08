import streamDeck from "@elgato/streamdeck";
import type { JsonObject } from "@elgato/utils";
import {
  BuildAction,
  ConsoleClearAction,
  ExecuteMenuAction,
  InvokeMethodAction,
  OpenWindowAction,
  OpenSceneAction,
  PauseAction,
  PlayStopToggleAction,
  ScreenshotAction,
  ScreenshotCopyAction,
  PlayModeSeparatorAction,
  CaptureSeparatorAction,
  EditorToolsSeparatorAction,
  CustomCommandsSeparatorAction,
  StatusSeparatorAction,
  ConsoleStatusAction,
  EditorStatusAction
} from "./actions/unity-command-action.js";
import { unityConnection } from "./unity-connection.js";

type ScreenshotInspectorMessage = {
  event?: string;
};

const SCREENSHOT_ACTION = "com.tttghost.stream-deck-unity.screenshot";
let waitingForScreenshotPath = false;

streamDeck.actions.registerAction(new PauseAction());
streamDeck.actions.registerAction(new PlayStopToggleAction());
streamDeck.actions.registerAction(new ConsoleClearAction());
streamDeck.actions.registerAction(new BuildAction());
streamDeck.actions.registerAction(new ExecuteMenuAction());
streamDeck.actions.registerAction(new OpenWindowAction());
streamDeck.actions.registerAction(new OpenSceneAction());
streamDeck.actions.registerAction(new InvokeMethodAction());
streamDeck.actions.registerAction(new ScreenshotAction());
streamDeck.actions.registerAction(new ScreenshotCopyAction());
streamDeck.actions.registerAction(new PlayModeSeparatorAction());
streamDeck.actions.registerAction(new CaptureSeparatorAction());
streamDeck.actions.registerAction(new EditorToolsSeparatorAction());
streamDeck.actions.registerAction(new CustomCommandsSeparatorAction());
streamDeck.actions.registerAction(new StatusSeparatorAction());
streamDeck.actions.registerAction(new ConsoleStatusAction());
streamDeck.actions.registerAction(new EditorStatusAction());

streamDeck.ui.onSendToPlugin<ScreenshotInspectorMessage, JsonObject>(async ev => {
  if (ev.action.manifestId !== SCREENSHOT_ACTION) return;

  const settings = await ev.action.getSettings<JsonObject & { screenshotPath?: string }>();
  const path = typeof settings.screenshotPath === "string" ? settings.screenshotPath : "";
  if (ev.payload.event === "chooseScreenshotPath") {
    waitingForScreenshotPath = true;
    if (!unityConnection.send({ command: "screenshot.choose", path })) {
      waitingForScreenshotPath = false;
      await ev.action.showAlert();
    }
    return;
  }

  if (ev.payload.event === "openScreenshotFolder" &&
      !unityConnection.send({ command: "screenshot.open-folder", path })) {
    await ev.action.showAlert();
  }
});

unityConnection.onMessage(async message => {
  if (!waitingForScreenshotPath || message.type !== "unity.screenshotPath" || !message.path) return;
  waitingForScreenshotPath = false;
  const action = streamDeck.ui.action;
  if (!action || action.manifestId !== SCREENSHOT_ACTION) return;
  const settings = await action.getSettings<JsonObject & { screenshotPath?: string }>();
  await action.setSettings({ ...settings, screenshotPath: message.path });
});

unityConnection.start();
process.once("SIGTERM", () => {
  unityConnection.stop();
  process.exit(0);
});
process.once("SIGINT", () => {
  unityConnection.stop();
  process.exit(0);
});

streamDeck.connect();
