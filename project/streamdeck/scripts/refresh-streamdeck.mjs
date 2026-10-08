import { execFileSync } from "node:child_process";
import { setTimeout } from "node:timers/promises";

if (process.platform !== "darwin") {
  throw new Error("Stream Deck app refresh currently supports macOS only");
}

let pids = [];
try {
  pids = execFileSync("pgrep", ["-x", "Stream Deck"], { encoding: "utf8" })
    .trim().split(/\s+/).filter(Boolean).map(Number);
} catch (error) {
  if (error.status !== 1) throw error;
}

const isRunning = (pid) => {
  try {
    process.kill(pid, 0);
    return true;
  } catch (error) {
    if (error.code === "ESRCH") return false;
    throw error;
  }
};

for (const pid of pids) {
  try { process.kill(pid, "SIGTERM"); }
  catch (error) { if (error.code !== "ESRCH") throw error; }
}

const deadline = Date.now() + 10000;
while (pids.some(isRunning)) {
  if (Date.now() >= deadline) {
    throw new Error("Stream Deck did not exit within 10 seconds. Close it manually and run npm run refresh:app again.");
  }
  await setTimeout(200);
}

execFileSync("open", ["-a", "Elgato Stream Deck"]);
console.log("Stream Deck 앱을 다시 열었습니다. 액션 목록을 새로 불러옵니다.");
