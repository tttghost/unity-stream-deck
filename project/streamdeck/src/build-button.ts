export type BuildReport = "idle" | "building" | "succeeded" | "failed";
export type BuildPhase = "ready" | "countdown" | "building" | "succeeded" | "failed";

/** One shared build lifecycle, even when the action is placed on multiple keys. */
export class BuildButton {
  phase: BuildPhase = "ready";
  compiling = false;
  secondsRemaining = 0;
  private lastReport?: BuildReport;
  private awaitingStart = false;
  private confirmationDeadline = 0;

  update(report: BuildReport, compiling: boolean): void {
    this.compiling = compiling;
    if (compiling && this.phase === "countdown") this.cancelCountdown();
    const changed = report !== this.lastReport;
    this.lastReport = report;
    if (report === "building") {
      this.secondsRemaining = 0;
      this.awaitingStart = false;
      this.phase = "building";
    } else if (!this.awaitingStart && changed && (report === "succeeded" || report === "failed")) {
      this.phase = report;
    } else if (!this.awaitingStart && report === "idle" && this.phase === "building") {
      // Unity restarted or reloaded without delivering a final build result.
      this.phase = "failed";
    }
  }

  press(connected = true, send: () => boolean = () => false, now = Date.now()):
    "countdown" | "started" | "expired" | "reset" | "blocked" | "disconnected" {
    if (this.phase === "countdown") {
      if (now >= this.confirmationDeadline) {
        this.cancelCountdown();
        return "expired";
      }
      if (!connected) {
        this.cancelCountdown();
        return "disconnected";
      }
      // Only a second click can send a build. Lock before sending.
      this.phase = "building";
      this.secondsRemaining = 0;
      this.awaitingStart = true;
      if (!send()) {
        this.phase = "ready";
        this.awaitingStart = false;
        return "disconnected";
      }
      return "started";
    }
    if (this.phase === "succeeded" || this.phase === "failed") {
      this.phase = "ready";
      return "reset";
    }
    if (this.phase === "building" || this.compiling) return "blocked";
    if (!connected) return "disconnected";
    this.phase = "countdown";
    this.secondsRemaining = 3;
    this.confirmationDeadline = now + 3000;
    return "countdown";
  }

  cancelCountdown(): void {
    if (this.phase !== "countdown") return;
    this.phase = "ready";
    this.secondsRemaining = 0;
    this.confirmationDeadline = 0;
  }

  tick(now = Date.now()): "waiting" | "expired" | "blocked" {
    if (this.phase !== "countdown") return "blocked";
    this.secondsRemaining = Math.max(0, Math.ceil((this.confirmationDeadline - now) / 1000));
    if (this.secondsRemaining > 0) return "waiting";
    this.cancelCountdown();
    return "expired";
  }

  connectionLost(): void {
    this.cancelCountdown();
    if (this.phase === "building") {
      this.awaitingStart = false;
      this.phase = "failed";
    }
  }

  startTimedOut(): void {
    if (this.awaitingStart) {
      this.awaitingStart = false;
      this.phase = "failed";
    }
  }

  get display(): { title: string; image: string } {
    if (this.phase === "countdown") return { title: `빌드 시작? ${this.secondsRemaining}`, image: "imgs/keys/build@2x.png" };
    if (this.phase === "building") return { title: "빌드 중", image: "imgs/keys/build-working.gif" };
    if (this.compiling) return { title: "컴파일 중", image: "imgs/keys/build-working.gif" };
    if (this.phase === "succeeded") return { title: "빌드 완료", image: "imgs/keys/build-success.gif" };
    if (this.phase === "failed") return { title: "빌드 실패", image: "imgs/keys/build-failed.gif" };
    return { title: "빌드", image: "imgs/keys/build@2x.png" };
  }
}
