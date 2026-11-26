import type { Counts, Folder, LocalAsset, LocalLibrary, RemoteInventory, RemoteItem, RootKind, SpriteSheet, State, UpdateOffer } from "./api";

export function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function isString(value: unknown): value is string {
  return typeof value === "string";
}

function isNullableString(value: unknown): value is string | null {
  return value === null || isString(value);
}

function isNumber(value: unknown): value is number {
  return typeof value === "number" && Number.isFinite(value);
}

function isNullableNumber(value: unknown): value is number | null {
  return value === null || isNumber(value);
}

function isArray<T>(value: unknown, check: (item: unknown) => item is T): value is T[] {
  return Array.isArray(value) && value.every(check);
}

export function isRootKind(value: unknown): value is RootKind {
  return value === "emoji" || value === "stickers" || value === "prints" || value === "photos" || value === "custom";
}

function isCounts(value: unknown): value is Counts {
  return isRecord(value) && isNumber(value["stickers"]) && isNumber(value["stickersMax"]) && isNumber(value["emojis"]) && isNumber(value["emojisMax"]);
}

function isUpdateOffer(value: unknown): value is UpdateOffer {
  return isRecord(value) && isString(value["version"]) && isString(value["tag"]) && isString(value["htmlUrl"]) && typeof value["isBeta"] === "boolean";
}

export function isState(value: unknown): value is State {
  if (!isRecord(value)) return false;
  const folders = value["knownFolders"];
  const settings = value["settings"];
  const user = value["user"];
  const offer = value["updateOffer"];
  return (
    isString(value["version"]) &&
    typeof value["isInstalled"] === "boolean" &&
    typeof value["busy"] === "boolean" &&
    isNullableString(value["busyMessage"]) &&
    typeof value["connected"] === "boolean" &&
    isNullableString(value["authError"]) &&
    (user === null || (isRecord(user) && isString(user["id"]) && isString(user["displayName"]))) &&
    isNullableString(value["vrcxVersion"]) &&
    isRootKind(value["selectedRootKind"]) &&
    isNullableString(value["localRoot"]) &&
    typeof value["includeSubfolders"] === "boolean" &&
    isRecord(folders) &&
    ["emoji", "stickers", "prints", "photos"].every((key) => isNullableString(folders[key])) &&
    isRecord(settings) &&
    isNullableString(settings["localRoot"]) &&
    typeof settings["updateCheck"] === "boolean" &&
    isNullableString(settings["skippedUpdate"]) &&
    isCounts(value["counts"]) &&
    (offer === null || isUpdateOffer(offer))
  );
}

function isFolder(value: unknown): value is Folder {
  return (
    isRecord(value) &&
    isString(value["path"]) &&
    isString(value["name"]) &&
    isString(value["relativePath"]) &&
    isNumber(value["directCount"]) &&
    isNumber(value["totalCount"]) &&
    isArray(value["children"], isFolder)
  );
}

function isLocalAsset(value: unknown): value is LocalAsset {
  return (
    isRecord(value) &&
    ["id", "name", "path", "relativeDir", "bucket", "ext", "sizeText", "thumbUrl", "previewUrl"].every((key) => isString(value[key])) &&
    isNumber(value["sizeBytes"]) &&
    isNumber(value["modifiedMs"]) &&
    ["width", "height", "frames", "fps"].every((key) => isNullableNumber(value[key])) &&
    ["isAnimated", "isGif", "isSpriteSheet"].every((key) => typeof value[key] === "boolean") &&
    ["animationStyle", "loopStyle", "spriteSheetUrl"].every((key) => isNullableString(value[key]))
  );
}

export function isLocalLibrary(value: unknown): value is LocalLibrary {
  return (
    isRecord(value) &&
    isNullableString(value["root"]) &&
    isNumber(value["totalCount"]) &&
    isArray(value["folders"], isFolder) &&
    isArray(value["assets"], isLocalAsset)
  );
}

function isRemoteItem(value: unknown): value is RemoteItem {
  return (
    isRecord(value) &&
    ["id", "name", "tag", "thumbUrl", "previewUrl"].every((key) => isString(value[key])) &&
    ["createdAt", "status", "animationStyle", "loopStyle"].every((key) => isNullableString(value[key])) &&
    isNullableNumber(value["frames"]) &&
    isNullableNumber(value["fps"])
  );
}

export function isRemoteInventory(value: unknown): value is RemoteInventory {
  return (
    isRecord(value) &&
    typeof value["connected"] === "boolean" &&
    isNullableString(value["authError"]) &&
    ["stickersCount", "stickersMax", "emojisCount", "emojisMax"].every((key) => isNumber(value[key])) &&
    isArray(value["items"], isRemoteItem)
  );
}

export function isSpriteSheet(value: unknown): value is SpriteSheet {
  return isRecord(value) && ["frames", "fps", "cols", "rows", "width", "height"].every((key) => isNumber(value[key])) && isString(value["imageUrl"]);
}

export function isResult(value: unknown): value is { ok?: boolean; id?: string; message?: string } {
  return (
    isRecord(value) &&
    (value["ok"] === undefined || typeof value["ok"] === "boolean") &&
    (value["id"] === undefined || isString(value["id"])) &&
    (value["message"] === undefined || isString(value["message"]))
  );
}

export function isBrowseResult(value: unknown): value is {
  cancelled?: boolean;
  path?: string | null;
  selectedRootKind?: RootKind;
} {
  return (
    isRecord(value) &&
    (value["cancelled"] === undefined || typeof value["cancelled"] === "boolean") &&
    (value["path"] === undefined || isNullableString(value["path"])) &&
    (value["selectedRootKind"] === undefined || isRootKind(value["selectedRootKind"]))
  );
}
