import { nonEmpty } from "./format";
import { isBrowseResult, isLocalLibrary, isRecord, isRemoteInventory, isResult, isSpriteSheet, isState } from "./api-schema";
export type RootKind = "emoji" | "stickers" | "prints" | "photos" | "custom";
export type UploadKind = "sticker" | "emoji" | "emojianimated";
export type LoopStyle = "linear" | "pingpong";
export type UpdateAction = "install" | "skip" | "later" | "check";

export type Counts = {
  stickers: number;
  stickersMax: number;
  emojis: number;
  emojisMax: number;
};

export type UpdateOffer = {
  version: string;
  tag: string;
  htmlUrl: string;
  isBeta: boolean;
};

export type State = {
  version: string;
  isInstalled: boolean;
  busy: boolean;
  busyMessage: string | null;
  connected: boolean;
  authError: string | null;
  user: { id: string; displayName: string } | null;
  vrcxVersion: string | null;
  selectedRootKind: RootKind;
  localRoot: string | null;
  includeSubfolders: boolean;
  knownFolders: Record<Exclude<RootKind, "custom">, string | null>;
  settings: {
    localRoot: string | null;
    updateCheck: boolean;
    skippedUpdate: string | null;
  };
  counts: Counts;
  updateOffer: UpdateOffer | null;
};

export type Folder = {
  path: string;
  name: string;
  relativePath: string;
  directCount: number;
  totalCount: number;
  children: Folder[];
};

export type LocalAsset = {
  id: string;
  name: string;
  path: string;
  relativeDir: string;
  bucket: string;
  ext: string;
  sizeBytes: number;
  sizeText: string;
  modifiedMs: number;
  width: number | null;
  height: number | null;
  isAnimated: boolean;
  isGif: boolean;
  isSpriteSheet: boolean;
  frames: number | null;
  fps: number | null;
  animationStyle: string | null;
  loopStyle: string | null;
  thumbUrl: string;
  previewUrl: string;
  spriteSheetUrl: string | null;
};

export type LocalLibrary = {
  root: string | null;
  totalCount: number;
  folders: Folder[];
  assets: LocalAsset[];
};

export type SpriteSheet = {
  frames: number;
  fps: number;
  cols: number;
  rows: number;
  width: number;
  height: number;
  imageUrl: string;
};

export type RemoteItem = {
  id: string;
  name: string;
  tag: string;
  createdAt: string | null;
  status: string | null;
  frames: number | null;
  fps: number | null;
  animationStyle: string | null;
  loopStyle: string | null;
  thumbUrl: string;
  previewUrl: string;
};

export type RemoteInventory = {
  connected: boolean;
  authError: string | null;
  stickersCount: number;
  stickersMax: number;
  emojisCount: number;
  emojisMax: number;
  items: RemoteItem[];
};

export type UploadOptions = {
  assetId: string;
  kind: UploadKind;
  animationStyle?: string;
  loopStyle?: LoopStyle;
  fpsOverride?: number;
};

const loggedFailures = new Set<string>();

export function logFailure(operation: string, status?: number): void {
  const key = status === undefined ? operation : `${operation} (${status})`;
  if (loggedFailures.size >= 24 || loggedFailures.has(key)) return;
  loggedFailures.add(key);
  console.warn(`VRCInventoryManager: ${key}`);
}

async function request<T>(path: string, validate: (value: unknown) => value is T, method = "GET", body?: unknown): Promise<T> {
  const operation = `${method} ${path.split("?")[0]?.replace(/(\/spritesheet|\/remote)\/[^/]+$/, "$1/:id")}`;
  const options: RequestInit = {
    method,
    signal: AbortSignal.timeout(method === "GET" ? 30_000 : path === "/api/local/browse" ? 3_600_000 : 300_000),
    headers: { Accept: "application/json" },
  };
  if (body !== undefined) {
    options.headers = {
      Accept: "application/json",
      "Content-Type": "application/json",
    };
    options.body = JSON.stringify(body);
  }
  let response: Response;
  try {
    response = await fetch(path, options);
  } catch (cause) {
    logFailure(`${operation} did not connect`);
    throw new Error("Can’t reach the app. Try refreshing.", { cause });
  }
  const responseText = await response.text();
  let data: unknown;
  try {
    data = responseText ? JSON.parse(responseText) : {};
  } catch (cause) {
    logFailure(`${operation} returned invalid JSON`, response.status);
    throw new Error("The app sent an unreadable response. Try refreshing.", {
      cause,
    });
  }
  if (!response.ok || (isRecord(data) && data["ok"] === false)) {
    logFailure(`${operation} failed`, response.status);
    const message = isRecord(data) ? (data["message"] ?? data["error"]) : undefined;
    throw new Error(typeof message === "string" && message ? message : `Request failed (${response.status}).`);
  }
  if (!validate(data)) {
    logFailure(`${operation} returned invalid data`, response.status);
    throw new Error("The app sent an unreadable response. Try refreshing.");
  }
  return data;
}

export const api = {
  state: () => request("/api/state", isState),
  library: (folder: string, query: string, shouldIncludeSubfolders: boolean) => {
    const params = new URLSearchParams({
      folder,
      query,
      includeSubfolders: String(shouldIncludeSubfolders),
    });
    return request(`/api/local/assets?${params}`, isLocalLibrary);
  },
  spriteSheet: (id: string, url?: string | null) => request(nonEmpty(url) ?? `/api/local/spritesheet/${encodeURIComponent(id)}`, isSpriteSheet),
  selectRoot: (kind: RootKind, shouldIncludeSubfolders: boolean, path?: string) =>
    request("/api/local/select-root", isResult, "POST", {
      kind,
      path,
      includeSubfolders: shouldIncludeSubfolders,
    }),
  browse: () => request("/api/local/browse", isBrowseResult, "POST", {}),
  importPaths: (paths: string[]) => request("/api/local/import-paths", isResult, "POST", { paths }),
  refreshLocal: () => request("/api/local/refresh", isResult, "POST", {}),
  inventory: () => request("/api/remote/items", isRemoteInventory),
  reconnect: () => request("/api/remote/reconnect", isResult, "POST", {}),
  refreshRemote: () => request("/api/remote/refresh", isResult, "POST", {}),
  upload: (options: UploadOptions) => request("/api/upload", isResult, "POST", options),
  deleteRemote: (id: string) => request(`/api/remote/${encodeURIComponent(id)}`, isResult, "DELETE"),
  settings: (settings: { includeSubfolders?: boolean; updateCheck?: boolean }) => request("/api/settings", isState, "POST", settings),
  update: (action: UpdateAction) => request("/api/update/action", isResult, "POST", { action }),
  openExternal: (target: { url?: string; revealPath?: string }) => request("/api/system/open-external", isResult, "POST", target),
};
