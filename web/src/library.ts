import { nonEmpty, dateText, decimalText, numberText } from "./format";
import type { Folder, LocalAsset, RemoteItem, RootKind } from "./api";
import { button, element, icon, thumbnail } from "./dom";

export const sources: readonly [RootKind, string][] = [
  ["emoji", "Emoji"],
  ["stickers", "Stickers"],
  ["prints", "Prints"],
  ["photos", "Photos"],
  ["custom", "Custom"],
];

export const animationStyles = [
  "aura",
  "bats",
  "bees",
  "bounce",
  "cloud",
  "confetti",
  "crying",
  "dislike",
  "fire",
  "idea",
  "lasers",
  "like",
  "magnet",
  "mistletoe",
  "money",
  "noise",
  "orbit",
  "pizza",
  "rain",
  "rotate",
  "shake",
  "snow",
  "snowball",
  "spin",
  "splash",
  "stop",
  "zzz",
];

export type RemoteFilter = "all" | "stickers" | "emojis";

export const remoteFilters: readonly [RemoteFilter, string][] = [
  ["all", "All"],
  ["stickers", "Stickers"],
  ["emojis", "Emojis"],
];

export const itemCount = (count: number, singular: string, plural = `${singular}s`) => `${numberText(count)} ${count === 1 ? singular : plural}`;
export const formatExt = (ext: string) => ext.replace(/^\./, "").toUpperCase();
export const formatSize = (size: number, given?: string) => {
  if (given) return given.replace(/\s+(?=[a-z]+$)/i, "\u00a0");
  const unit = size >= 1024 ** 2 ? "MB" : size >= 1024 ? "KB" : "B";
  const amount = size / (unit === "MB" ? 1024 ** 2 : unit === "KB" ? 1024 : 1);
  return `${decimalText(amount)}\u00a0${unit}`;
};

export function remoteKind(item: RemoteItem): "sticker" | "emoji" | "animated" {
  if (item.tag.toLowerCase() === "sticker") return "sticker";
  return item.tag.toLowerCase() === "emojianimated" || (item.frames ?? 0) > 1 ? "animated" : "emoji";
}

export function remoteKindLabel(item: RemoteItem): string {
  const kind = remoteKind(item);
  return kind === "sticker" ? "Sticker" : kind === "animated" ? "Animated emoji" : "Emoji";
}

export function normalizedPath(path: string): string {
  return path
    .replace(/\\/g, "/")
    .replace(/^\/+|\/+$/g, "")
    .toLowerCase();
}

export function findFolder(folders: Folder[], relativePath: string): Folder | undefined {
  for (const folder of folders) {
    if (normalizedPath(folder.relativePath) === normalizedPath(relativePath)) return folder;
    const match = findFolder(folder.children, relativePath);
    if (match) return match;
  }
  return undefined;
}

export function pathText(path: string, limit = 56): string {
  if (path.length <= limit) return path;
  const tail = path.split(/[\\/]/).at(-1) ?? path;
  const head = path.slice(0, Math.min(16, Math.max(3, limit - tail.length - 4)));
  return `${head}…${tail.slice(-(limit - head.length - 1))}`;
}

export function localCard(asset: LocalAsset, isSelected: boolean, action: () => void): HTMLButtonElement {
  const tile = element(
    "button",
    {
      type: "button",
      class: "asset-card",
      title: asset.name,
      "aria-pressed": String(isSelected),
      "data-key": `local:${asset.id}`,
    },
    thumbnail(asset.thumbUrl, "asset-thumbnail"),
    asset.isAnimated || asset.isGif || asset.isSpriteSheet
      ? element("span", { class: "animation-badge numeric" }, icon("film"), asset.isGif ? "GIF" : asset.frames ? `${asset.frames}f` : "Motion")
      : null,
    element("span", { class: "asset-name truncate" }, asset.name),
    element(
      "span",
      { class: "asset-meta numeric" },
      element("span", {}, formatExt(asset.ext)),
      element("span", {}, formatSize(asset.sizeBytes, asset.sizeText))
    )
  );
  tile.addEventListener("click", action);
  return tile;
}

export function remoteCard(item: RemoteItem, isSelected: boolean, isBlocked: boolean, inspect: () => void, remove: () => void): HTMLElement {
  const date = item.createdAt ? new Date(item.createdAt) : null;
  const validDate = date && Number.isFinite(date.getTime()) ? date : null;
  const kind = remoteKind(item);
  const status = nonEmpty(item.status?.trim()) ?? "Available";
  const isAvailable = /^(available|complete|completed|ready|synced)$/i.test(status);
  const image = thumbnail(item.thumbUrl || item.previewUrl, "remote-thumbnail");
  const picker = element(
    "button",
    {
      type: "button",
      class: "remote-pick",
      title: item.name,
      "aria-pressed": String(isSelected),
      "data-key": `remote:${item.id}`,
    },
    image,
    element(
      "span",
      { class: "remote-text" },
      element("span", { class: "remote-name truncate" }, item.name),
      element(
        "span",
        { class: "remote-meta" },
        element("span", { class: `remote-tag tag-${kind}`, title: remoteKindLabel(item) }, kind === "animated" ? "Animated" : remoteKindLabel(item)),
        validDate
          ? element(
              "time",
              {
                class: "remote-date numeric",
                datetime: validDate.toISOString(),
                title: dateText(validDate, true),
              },
              dateText(validDate)
            )
          : null
      )
    )
  );
  picker.addEventListener("click", inspect);
  return element(
    "article",
    { class: `remote-card${isSelected ? " remote-selected" : ""}` },
    picker,
    element("span", {
      class: `status-dot ${isAvailable ? "status-available" : "status-pending"}`,
      title: status,
      role: "img",
      "aria-label": status,
    }),
    button(`Delete ${item.name}`, "trash", remove, {
      iconOnly: true,
      class: "remote-delete",
      title: "Delete remote file",
      disabled: isBlocked,
      key: `delete:${item.id}`,
    })
  );
}
