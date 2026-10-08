import { test } from "node:test";
import assert from "node:assert/strict";
import { BuildButton } from "../src/build-button.ts";

const neverSend = () => { throw new Error("Must not send a build request"); };

for (const confirmAt of [1, 1000, 2999]) {
  test(`second click at ${confirmAt}ms sends once; building ignores more clicks`, () => {
    const button = new BuildButton();
    let sent = 0;
    const send = () => { sent++; return true; };
    assert.equal(button.press(true, neverSend, 0), "countdown");
    assert.equal(button.press(true, send, confirmAt), "started");
    assert.equal(button.press(true, send, confirmAt + 1), "blocked");
    assert.equal(button.tick(3000), "blocked");
    assert.equal(sent, 1);
  });
}

test("no second click: 3, 2, 1 returns to ready without a build", () => {
  const button = new BuildButton();
  button.press(true, neverSend, 0);
  assert.equal(button.display.title, "빌드 시작? 3");
  button.tick(1000);
  assert.equal(button.display.title, "빌드 시작? 2");
  button.tick(2000);
  assert.equal(button.display.title, "빌드 시작? 1");
  assert.equal(button.tick(3000), "expired");
  assert.equal(button.display.title, "빌드");
  assert.equal(button.press(true, neverSend, 4000), "countdown");
});

for (const late of [3000, 5000]) {
  test(`late confirmation at ${late}ms cannot bypass deadline even if timer is delayed`, () => {
    const button = new BuildButton();
    button.press(true, neverSend, 0);
    assert.equal(button.press(true, neverSend, late), "expired");
    assert.equal(button.phase, "ready");
  });
}

for (const result of ["succeeded", "failed"] as const) {
  test(`${result}: reset requires a new two-click confirmation`, () => {
    const button = new BuildButton();
    button.update("idle", false);
    button.press(true, neverSend, 0);
    button.press(true, () => true, 1);
    button.update("building", false);
    button.update(result, false);
    assert.equal(button.phase, result);
    assert.equal(button.press(true, neverSend, 100), "reset");
    button.update(result, false);
    assert.equal(button.phase, "ready");
    assert.equal(button.press(true, neverSend, 200), "countdown");
    button.update(result, false);
    assert.equal(button.press(true, () => true, 300), "started");
    button.update(result, false);
    assert.equal(button.phase, "building");
    button.update("building", false);
    button.startTimedOut();
    assert.equal(button.phase, "building");
    button.update(result, false);
    assert.equal(button.phase, result);
  });
}

for (const cause of ["disconnect", "compile", "hidden", "external-build"]) {
  test(`${cause} cancels confirmation`, () => {
    const button = new BuildButton();
    button.press(true, neverSend, 0);
    if (cause === "disconnect") button.connectionLost();
    else if (cause === "hidden") button.cancelCountdown();
    else button.update(cause === "external-build" ? "building" : "idle", cause === "compile");
    assert.equal(button.tick(3000), "blocked");
    assert.notEqual(button.phase, "countdown");
  });
}

test("connection guards and send failure do not leave a running button", () => {
  const button = new BuildButton();
  assert.equal(button.press(false, neverSend, 0), "disconnected");
  button.press(true, neverSend, 0);
  assert.equal(button.press(false, neverSend, 1), "disconnected");
  button.press(true, neverSend, 2);
  assert.equal(button.press(true, () => false, 3), "disconnected");
  assert.equal(button.phase, "ready");
});

test("compiling blocks requests; missing acknowledgement and disconnect recover", () => {
  const button = new BuildButton();
  button.update("idle", true);
  assert.equal(button.press(true, neverSend, 0), "blocked");
  button.update("idle", false);
  button.press(true, neverSend, 0);
  button.press(true, () => true, 1);
  button.startTimedOut();
  assert.equal(button.phase, "failed");
  button.update("building", false);
  button.connectionLost();
  assert.equal(button.phase, "failed");
  button.update("building", false);
  button.update("idle", false);
  assert.equal(button.phase, "failed");
});
