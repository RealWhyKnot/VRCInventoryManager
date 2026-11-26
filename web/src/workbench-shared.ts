import { isRecord } from "./api-schema";
import { type RootKind } from "./api";
import { byId, element, type Icon } from "./dom";
import { remoteFilters, sources, type RemoteFilter } from "./library";
import { Player } from "./playback";

type Backdrop = "checker" | "dark" | "light";

export type Scope = "local" | "remote";

export type Operation = Scope | "upload" | "delete" | "reconnect" | "settings" | "update" | "browse";

type Preferences = {
  backdrop: Backdrop;
  remote: RemoteFilter;
  folders: Partial<Record<RootKind, string>>;
  customRoot?: string;
};

export const backdrops: readonly [Backdrop, string, Icon][] = [
  ["checker", "Checkerboard", "grid"],
  ["dark", "Dark", "dark"],
  ["light", "Light", "light"],
];

export const pageSize = 48;

export const defaultCounts = {
  stickers: 0,
  stickersMax: 18,
  emojis: 0,
  emojisMax: 18,
};

export const storageKey = "vrcim.workbench";

export const dropLimit = 32 * 1024 * 1024;

export const rootSearch = byId("asset-search", HTMLInputElement);

export const subfolders = byId("subfolders", HTMLInputElement);

export const settingsDialog = byId("settings-dialog", HTMLDialogElement);

export const deleteDialog = byId("delete-dialog", HTMLDialogElement);

export const updateDialog = byId("update-dialog", HTMLDialogElement);

export const replaceMedia = new Player(byId("preview"), byId("transport"));

export function loadPreferences(): Preferences {
  const defaults: Preferences = {
    backdrop: "checker",
    remote: "all",
    folders: {},
  };
  try {
    const value: unknown = JSON.parse(localStorage.getItem(storageKey) ?? "null");
    if (isRecord(value)) {
      const saved = value;
      const backdrop = backdrops.find(([key]) => key === saved["backdrop"]);
      if (backdrop) defaults.backdrop = backdrop[0];
      const remote = remoteFilters.find(([key]) => key === saved["remote"]);
      if (remote) defaults.remote = remote[0];
      if (typeof saved["customRoot"] === "string" && saved["customRoot"].length <= 4096) defaults.customRoot = saved["customRoot"];
      for (const [kind] of sources) {
        const folders = saved["folders"];
        const folder = isRecord(folders) ? folders[kind] : undefined;
        if (typeof folder === "string" && folder.length <= 4096) defaults.folders[kind] = folder;
      }
    }
    const params = new URLSearchParams(location.hash.slice(1));
    const remote = remoteFilters.find(([key]) => key === params.get("inventory"));
    if (remote) defaults.remote = remote[0];
  } catch {
    return defaults;
  }
  return defaults;
}

export function errorText(error: unknown): string {
  return error instanceof Error ? error.message : "Something went wrong. Try again.";
}

export function readFile(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.addEventListener("load", () => (typeof reader.result === "string" ? resolve(reader.result) : reject(new Error("Couldn’t read this image."))));
    reader.addEventListener("error", () => reject(new Error("Couldn’t read this image.")));
    reader.readAsDataURL(file);
  });
}

export function labelStyle(value: string): string {
  return value === "zzz" ? "Zzz" : value.charAt(0).toUpperCase() + value.slice(1);
}

export function detail(label: string, value: string, title = value): HTMLElement {
  return element("div", { class: "detail" }, element("dt", {}, label), element("dd", { class: "truncate", title }, value));
}
