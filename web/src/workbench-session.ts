import { nonEmpty, numberText } from "./format";
import { logFailure, api } from "./api";
import { isRecord } from "./api-schema";
import { button, byId, element, icon, setContents, type Icon } from "./dom";
import { findFolder, normalizedPath } from "./library";
import type { Workbench } from "./workbench";
import { type Operation, pageSize, storageKey, settingsDialog, deleteDialog, updateDialog, errorText } from "./workbench-shared";

export async function start(this: Workbench): Promise<void> {
  await this.loadState();
  await Promise.all([this.loadLocal(), this.loadRemote()]);
  this.listen();
}

export function persist(this: Workbench): void {
  try {
    localStorage.setItem(storageKey, JSON.stringify(this.prefs));
    const params = new URLSearchParams(location.hash.slice(1));
    if (this.prefs.remote === "all") params.delete("inventory");
    else params.set("inventory", this.prefs.remote);
    if (this.query) params.set("search", this.query);
    else params.delete("search");
    const hash = params.toString();
    if (hash !== location.hash.slice(1)) history.replaceState(null, "", location.pathname + location.search + (hash ? `#${hash}` : ""));
  } catch {
    return;
  }
}

export async function loadState(this: Workbench): Promise<void> {
  const request = ++this.stateRequest;
  try {
    const next = await api.state();
    if (request !== this.stateRequest) return;
    const old = this.state;
    this.state = next;
    this.rootKind = next.selectedRootKind;
    this.shouldIncludeSubfolders = next.includeSubfolders;
    if (this.rootKind === "custom" && next.localRoot && this.prefs.customRoot !== next.localRoot) {
      this.prefs.customRoot = next.localRoot;
      this.persist();
    }
    const hasRootChanged = old?.localRoot !== next.localRoot;
    const hasKindChanged = old?.selectedRootKind !== next.selectedRootKind;
    if (hasRootChanged || hasKindChanged) {
      this.folder = old?.selectedRootKind === next.selectedRootKind ? "" : (this.prefs.folders[this.rootKind] ?? "");
      this.visibleLimit = pageSize;
      this.openFolders.clear();
      if (this.selected?.source === "local") this.select(null);
    }
    this.renderChrome();
    this.renderLocalTools();
    this.renderRemoteTools();
    this.renderInspector();
    if (settingsDialog.open) this.renderSettings();
    this.offerUpdate();
  } catch (error) {
    if (request === this.stateRequest && !this.state) this.toast(errorText(error), "error");
  }
}

export async function loadLocal(this: Workbench, isResetScroll = false): Promise<void> {
  const request = ++this.localRequest;
  try {
    const library = await api.library(this.folder, this.query, this.shouldIncludeSubfolders);
    if (request !== this.localRequest) return;
    if (this.folder && !findFolder(library.folders, this.folder)) {
      this.folder = "";
      this.prefs.folders[this.rootKind] = "";
      this.openFolders.clear();
      this.persist();
      await this.loadLocal(isResetScroll);
      return;
    }
    this.library = library;
    this.localError = "";
    if (this.importedPaths.length) {
      const byPath = new Map(library.assets.map((asset) => [normalizedPath(asset.path), asset]));
      const item = this.importedPaths.map((path) => byPath.get(path)).find(Boolean) ?? library.assets[0];
      this.importedPaths = [];
      if (item) this.select({ source: "local", asset: item });
    }
    const first = library.assets[0];
    if (this.isFirstLibrary && first) {
      if (!this.selected) this.select({ source: "local", asset: first });
      this.isFirstLibrary = false;
    }
    if (this.selected?.source === "local") {
      const current = this.selected.asset;
      const latest = library.assets.find((asset) => asset.id === current.id);
      if (latest && latest.modifiedMs !== current.modifiedMs) this.select({ source: "local", asset: latest });
    }
  } catch (error) {
    if (request !== this.localRequest) return;
    this.localError = errorText(error);
  }
  this.renderLocal();
  if (isResetScroll) byId("local-scroll").scrollTop = 0;
}

export async function loadRemote(this: Workbench): Promise<void> {
  const request = ++this.remoteRequest;
  try {
    const inventory = await api.inventory();
    if (request !== this.remoteRequest) return;
    this.inventory = inventory;
    this.remoteError = "";
    if (this.selected?.source === "remote" && !inventory.items.some((item) => item.id === this.selectedId)) this.select(null);
  } catch (error) {
    if (request !== this.remoteRequest) return;
    this.remoteError = errorText(error);
  }
  this.renderChrome();
  this.renderRemote();
  this.renderInspector();
}

export function listen(this: Workbench): void {
  if (typeof EventSource === "function") {
    try {
      const stream = new EventSource("/api/events");
      stream.addEventListener("open", () => {
        this.isStreamOpen = true;
      });
      stream.addEventListener("error", () => {
        this.isStreamOpen = false;
      });
      stream.addEventListener("message", (event: MessageEvent<string>) => {
        try {
          const payload: unknown = JSON.parse(event.data);
          if (!isRecord(payload)) throw new Error("Invalid app event.");
          if (payload["type"] === "update-progress") {
            if (this.busy === "update" && typeof payload["progress"] === "number" && Number.isFinite(payload["progress"])) {
              const ratio = payload["progress"] ?? 0;
              this.updateProgress = Math.round(Math.min(1, Math.max(0, ratio)) * 100);
              this.renderProgress();
            }
          } else this.queueEvent(typeof payload["type"] === "string" && payload["type"] ? payload["type"] : "state");
        } catch {
          this.queueEvent("state");
        }
      });
      for (const kind of ["state", "local", "remote", "update", "settings"]) {
        stream.addEventListener(kind, () => this.queueEvent(kind));
      }
      window.addEventListener("pagehide", () => stream.close(), {
        once: true,
      });
    } catch {
      this.isStreamOpen = false;
    }
  }
  const polling = window.setInterval(() => {
    if (!this.isStreamOpen && !this.busy) this.queueEvent("ready");
  }, 30_000);
  window.addEventListener("pagehide", () => window.clearInterval(polling), {
    once: true,
  });
}

export function queueEvent(this: Workbench, kind: string): void {
  this.receivedEvents.add(kind);
  if (this.eventTimer !== null) return;
  this.eventTimer = window.setTimeout(() => {
    this.eventTimer = null;
    const kinds = new Set(this.receivedEvents);
    this.receivedEvents.clear();
    void this.syncEvent(kinds).catch(() => logFailure("Background workbench-session operation failed"));
  }, 100);
}

export async function syncEvent(this: Workbench, kinds: Set<string>): Promise<void> {
  const oldRoot = this.state?.localRoot;
  const wasConnected = this.isConnected;
  await this.loadState();
  const isBoth = kinds.has("ready");
  const requests: Promise<void>[] = [];
  if (isBoth || kinds.has("local") || oldRoot !== this.state?.localRoot) requests.push(this.loadLocal());
  if (isBoth || kinds.has("remote") || wasConnected !== this.isConnected) requests.push(this.loadRemote());
  await Promise.all(requests);
}

export async function operate(this: Workbench, kind: Operation, busyLabel: string, run: () => Promise<void>): Promise<void> {
  if (this.busy) return;
  this.busy = kind;
  this.busyLabel = busyLabel;
  this.operationError = "";
  this.refreshControls();
  this.busyTimer = window.setTimeout(() => {
    this.isBusyVisible = true;
    this.refreshControls();
  }, 250);
  try {
    await run();
  } catch (error) {
    const message = errorText(error);
    if (deleteDialog.open || settingsDialog.open || updateDialog.open) this.operationError = message;
    else this.toast(message, "error");
  } finally {
    window.clearTimeout(this.busyTimer);
    this.busyTimer = null;
    this.isBusyVisible = false;
    this.updateProgress = null;
    this.busy = null;
    this.refreshControls();
    this.offerUpdate();
  }
}

export function working(this: Workbench, kind: Operation, fallback: string): string {
  return this.busy === kind && this.isBusyVisible ? this.busyLabel : fallback;
}

export function refreshControls(this: Workbench): void {
  this.renderChrome();
  this.renderLocalTools();
  this.renderRemoteTools();
  this.renderInspector();
  if (settingsDialog.open) this.renderSettings();
  if (deleteDialog.open) this.renderDelete();
  if (updateDialog.open) this.renderUpdate();
}

export function quota(this: Workbench, label: string, glyph: Icon, value: number, max: number): HTMLElement {
  const capacity = max > 0 ? max : 18;
  const tone = value >= capacity ? "quota-full" : value >= capacity - 3 ? "quota-near" : "";
  const gauge = element("span", {
    class: "quota-gauge",
    "aria-hidden": "true",
  });
  gauge.style.setProperty("--quota-filled", `${Math.ceil((Math.min(value, capacity) * 18) / capacity) * 6}px`);
  return element(
    "div",
    {
      class: `quota ${tone}`,
      role: "meter",
      "aria-label": label,
      "aria-valuemin": 0,
      "aria-valuenow": Math.min(value, capacity),
      "aria-valuemax": capacity,
    },
    element(
      "span",
      { class: "quota-title" },
      icon(glyph),
      label,
      element("span", { class: "quota-value numeric" }, `${numberText(value)}/${numberText(capacity)}`)
    ),
    gauge
  );
}

export function renderChrome(this: Workbench): void {
  const isLoaded = this.state !== null || this.inventory !== null;
  const displayName = nonEmpty(this.state?.user?.displayName) ?? "Connected";
  const connection = this.isConnected
    ? element(
        "span",
        { class: "connection-label connected", title: displayName },
        element("span", { class: "connection-dot" }),
        element("span", { class: "truncate" }, displayName)
      )
    : isLoaded
      ? button("Reconnect VRCX", "offline", () => this.reconnect(), {
          class: "connection-action",
          disabled: this.isBlocked,
          key: "connection",
          title: this.state?.authError ?? this.inventory?.authError ?? "Sign in to VRCX and reconnect",
        })
      : element("span", { class: "connection-label" }, element("span", { class: "connection-dot" }), "Connecting…");
  setContents(byId("connection"), connection);
  const counts = this.counts;
  setContents(
    byId("quotas"),
    this.quota("Stickers", "sticker", counts.stickers, counts.stickersMax),
    this.quota("Emojis", "emoji", counts.emojis, counts.emojisMax)
  );
  const reconnectLabel = this.working("reconnect", "Reconnect");
  this.reconnectButton.disabled = this.isBlocked;
  this.reconnectButton.title = reconnectLabel;
  this.reconnectButton.setAttribute("aria-label", reconnectLabel);
  const reconnectText = this.reconnectButton.querySelector(".button-label");
  if (reconnectText && reconnectText.textContent !== reconnectLabel) reconnectText.textContent = reconnectLabel;
  this.renderProgress();
}

export function renderProgress(this: Workbench): void {
  const progress = byId("progress");
  const percent = this.updateProgress;
  const isActive = percent !== null || this.isBusyVisible || this.state?.busy === true;
  progress.hidden = !isActive;
  if (percent !== null) {
    progress.setAttribute("aria-valuenow", String(percent));
    progress.style.setProperty("--progress-fill", `${percent}%`);
  } else {
    progress.removeAttribute("aria-valuenow");
    progress.style.removeProperty("--progress-fill");
  }
  if (isActive) {
    progress.setAttribute("role", "progressbar");
    progress.setAttribute(
      "aria-label",
      percent !== null ? "Downloading update" : this.busyLabel && this.busy ? this.busyLabel : (nonEmpty(this.state?.busyMessage) ?? "Working")
    );
  }
}
