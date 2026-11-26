import { logFailure, api } from "./api";
import { byId } from "./dom";
import { normalizedPath } from "./library";
import { type PreviewItem } from "./playback";
import type { Workbench } from "./workbench";
import { type Scope, pageSize, dropLimit, rootSearch, settingsDialog, deleteDialog, updateDialog, errorText, readFile } from "./workbench-shared";

export function handleKeys(this: Workbench, event: KeyboardEvent): void {
  if (event.defaultPrevented || event.isComposing || settingsDialog.open || deleteDialog.open || updateDialog.open) return;
  const target = event.target;
  if (target instanceof HTMLElement && (target.closest("input, select, textarea, [contenteditable]") || target.isContentEditable)) return;
  if ((event.ctrlKey && event.key.toLowerCase() === "f") || (event.key === "/" && !event.ctrlKey && !event.altKey && !event.metaKey)) {
    event.preventDefault();
    rootSearch.focus();
    return;
  }
  if (event.ctrlKey || event.altKey || event.metaKey) return;
  const key = event.key;
  if (key === "Escape" && this.selected) {
    this.select(null);
    return;
  }
  if (!["ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown", "Home", "End"].includes(key)) return;
  const focusScope = target instanceof HTMLElement ? target.closest(".remote-scroll, .local-scroll") : null;
  const focusedKey = target instanceof HTMLElement ? target.getAttribute("data-key") : null;
  if (focusedKey && (!focusScope || !/^(local|remote):/.test(focusedKey))) return;
  const scope: Scope = focusScope?.classList.contains("remote-scroll") || (!focusScope && this.selected?.source === "remote") ? "remote" : "local";
  const items: PreviewItem[] =
    scope === "remote"
      ? this.filteredRemote.map((asset) => ({ source: "remote", asset }))
      : (this.library?.assets ?? []).slice(0, this.visibleLimit).map((asset) => ({ source: "local", asset }));
  if (!items.length) return;
  const index = items.findIndex((item) => item.source !== "drop" && item.asset.id === this.selectedId);
  const delta = key === "ArrowLeft" || key === "ArrowUp" ? -1 : 1;
  const next =
    key === "Home" ? 0 : key === "End" ? items.length - 1 : index < 0 ? (delta < 0 ? items.length - 1 : 0) : (index + delta + items.length) % items.length;
  const item = items[next];
  if (!item || item.source === "drop") return;
  event.preventDefault();
  this.select(item);
  this.focusItem(scope, item.asset.id);
}

export function focusItem(this: Workbench, scope: Scope, id: string): void {
  const container = byId(scope === "local" ? "local-collection" : "remote-collection");
  for (const node of container.querySelectorAll<HTMLElement>("[data-key]")) {
    if (node.getAttribute("data-key") === `${scope}:${id}`) {
      node.focus({ preventScroll: true });
      node.scrollIntoView({ block: "nearest", inline: "nearest" });
      return;
    }
  }
}

export function mountDrop(this: Workbench): void {
  const pane = byId("local-pane");
  const zone = byId("drop-zone");
  const hasFiles = (event: DragEvent) => event.dataTransfer !== null && Array.from(event.dataTransfer.types).includes("Files");
  pane.addEventListener("dragenter", (event) => {
    if (!hasFiles(event)) return;
    event.preventDefault();
    this.dropDepth++;
    zone.hidden = false;
  });
  pane.addEventListener("dragover", (event) => {
    if (!hasFiles(event)) return;
    event.preventDefault();
    if (event.dataTransfer) event.dataTransfer.dropEffect = "copy";
  });
  pane.addEventListener("dragleave", (event) => {
    if (!hasFiles(event)) return;
    this.dropDepth = Math.max(0, this.dropDepth - 1);
    if (!this.dropDepth) zone.hidden = true;
  });
  pane.addEventListener("drop", (event) => {
    if (!hasFiles(event)) return;
    event.preventDefault();
    this.dropDepth = 0;
    zone.hidden = true;
    this.isDropExpected = true;
    const files = Array.from(event.dataTransfer?.files ?? []);
    if (files.length) void this.inspectDrop(files[0]).catch(() => logFailure("Background workbench-input operation failed"));
  });
  window.addEventListener("dragover", (event) => {
    if (hasFiles(event)) event.preventDefault();
  });
  window.addEventListener("drop", (event) => {
    if (hasFiles(event)) event.preventDefault();
  });
  const dialogOpen = () => settingsDialog.open || deleteDialog.open || updateDialog.open;
  window.addEventListener("vrcim:file-drag", (event) => {
    zone.hidden = !(event instanceof CustomEvent) || event.detail !== true || dialogOpen();
  });
  window.addEventListener("vrcim:file-drop", (event) => {
    zone.hidden = true;
    if (dialogOpen()) return;
    if (!(event instanceof CustomEvent)) return;
    const paths: unknown = event.detail;
    if (Array.isArray(paths) && paths.every((path: unknown) => typeof path === "string")) this.importPaths(paths);
  });
}

export function importPaths(this: Workbench, paths: string[]): void {
  if (!paths.length) {
    this.toast("Couldn’t read the dropped files.", "error");
    return;
  }
  if (this.busy) {
    this.toast("Wait for the current task before dropping files.", "error");
    return;
  }
  this.isDropExpected = false;
  this.dropSequence++;
  void this.operate("local", "Importing files…", async () => {
    await api.importPaths(paths);
    await this.loadState();
    this.importedPaths = paths.map(normalizedPath);
    if (this.searchTimer !== null) window.clearTimeout(this.searchTimer);
    this.searchTimer = null;
    this.folder = "";
    this.prefs.folders[this.rootKind] = "";
    this.query = rootSearch.value = "";
    this.visibleLimit = pageSize;
    this.persist();
    await this.loadLocal(true);
    this.toast("Imported files");
  }).catch(() => logFailure("Background workbench-input operation failed"));
}

export async function inspectDrop(this: Workbench, file: File | undefined): Promise<void> {
  if (!file) return;
  const sequence = ++this.dropSequence;
  const path: unknown = Reflect.get(file, "path");
  if (typeof path === "string" && path) {
    this.importPaths([path]);
    return;
  }
  if (file.size > dropLimit || (!file.type.startsWith("image/") && !/\.(png|gif|jpe?g|webp|bmp|tiff?)$/i.test(file.name))) {
    this.toast("Drop an image smaller than 32 MB.", "error");
    return;
  }
  try {
    const url = await readFile(file);
    if (sequence !== this.dropSequence || !this.isDropExpected) return;
    this.select({
      source: "drop",
      file,
      name: file.name,
      size: file.size,
      url,
    });
  } catch (error) {
    if (sequence === this.dropSequence) this.toast(errorText(error), "error");
  }
}
