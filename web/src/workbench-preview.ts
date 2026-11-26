import { nonEmpty, numberText } from "./format";
import { logFailure, api, type LocalAsset, type UploadKind } from "./api";
import { button, byId, element, emptyState, icon, setContents, type Icon } from "./dom";
import { animationStyles, formatExt, formatSize, pathText, remoteKindLabel } from "./library";
import { type PreviewItem } from "./playback";
import type { Workbench } from "./workbench";
import { backdrops, replaceMedia, errorText, labelStyle, detail } from "./workbench-shared";

export function renderBackdrop(this: Workbench): void {
  byId("platen").setAttribute("data-backdrop", this.prefs.backdrop);
  setContents(
    byId("backdrops"),
    ...backdrops.map(([value, label, glyph]) =>
      button(
        label,
        glyph,
        () => {
          this.prefs.backdrop = value;
          this.persist();
          this.renderBackdrop();
        },
        {
          class: "backdrop-choice",
          pressed: value === this.prefs.backdrop,
          key: value,
        }
      )
    )
  );
}

export function select(this: Workbench, item: PreviewItem | null): void {
  const previous = this.selected;
  if (item && item.source !== "drop" && previous && previous.source !== "drop" && previous.source === item.source && previous.asset === item.asset) return;
  this.selected = item;
  this.style =
    item?.source === "local" && item.asset.animationStyle && animationStyles.includes(item.asset.animationStyle.toLowerCase())
      ? item.asset.animationStyle.toLowerCase()
      : "stop";
  this.loop = item && item.source !== "drop" && item.asset.loopStyle?.toLowerCase() === "pingpong" ? "pingpong" : "linear";
  this.renderStage();
  for (const changed of [previous, item]) {
    if (!changed || changed.source === "drop") continue;
    const scope = changed.source;
    const container = byId(scope === "local" ? "local-collection" : "remote-collection");
    const key = `${scope}:${changed.asset.id}`;
    for (const card of container.querySelectorAll<HTMLButtonElement>("[aria-pressed][data-key]")) {
      if (card.getAttribute("data-key") !== key) continue;
      const isSelected = this.selected?.source === scope && this.selectedId === changed.asset.id;
      card.setAttribute("aria-pressed", String(isSelected));
      card.closest(".remote-card")?.classList.toggle("remote-selected", isSelected);
      break;
    }
  }
}

export function renderStage(this: Workbench): void {
  const caption = byId("viewer-caption");
  const item = this.selected;
  if (item) {
    const source = item.source === "local" ? "Local file" : item.source === "remote" ? "VRChat" : "Dropped file";
    const glyph = item.source === "remote" ? "cloud" : "asset";
    setContents(caption, element("span", { class: "stage-label" }, icon(glyph), source));
    void replaceMedia.show(item).catch(() => logFailure("Background workbench-preview operation failed"));
  } else {
    caption.replaceChildren();
    replaceMedia.clear();
    byId("preview").append(
      element(
        "div",
        { class: "stage-empty" },
        emptyState("stage", "Select an asset"),
        element("div", { class: "keyboard-hint" }, element("kbd", {}, "←"), element("kbd", {}, "→"), "Browse files")
      )
    );
  }
  this.renderInspector();
}

export function renderInspector(this: Workbench): void {
  const node = byId("inspector");
  const item = this.selected;
  if (!item) {
    setContents(
      node,
      element("div", { class: "inspector-idle" }, element("span", { class: "inspector-emblem" }, icon("asset")), element("h3", {}, "Asset details"))
    );
    return;
  }
  const name = item.source === "drop" ? item.name : item.asset.name;
  const label = element(
    "div",
    { class: "inspector-file" },
    element("span", { class: "inspector-emblem" }, icon(item.source === "remote" ? "cloud" : "asset")),
    element("h3", { class: "truncate", title: name }, name)
  );
  if (item.source === "local") {
    setContents(node, label, this.localInspector(item.asset));
  } else if (item.source === "remote") {
    const asset = item.asset;
    setContents(
      node,
      label,
      element(
        "dl",
        { class: "detail-list" },
        detail("Type", remoteKindLabel(asset)),
        detail("Status", nonEmpty(asset.status) ?? "Available"),
        (asset.frames ?? 0) > 1 ? detail("Frames", `${asset.frames} · ${asset.fps ?? 15} fps`) : null,
        asset.animationStyle ? detail("Animation", labelStyle(asset.animationStyle)) : null
      ),
      element(
        "div",
        { class: "action-dock" },
        button("Delete remote file", "trash", () => this.openDelete(asset), {
          class: "danger-button",
          disabled: this.isBlocked || !this.isConnected,
          key: "stage-delete",
        })
      )
    );
  } else {
    setContents(
      node,
      label,
      element("dl", { class: "detail-list" }, detail("Format", formatExt(nonEmpty(name.split(".").at(-1)) ?? "")), detail("Size", formatSize(item.size))),
      element("p", { class: "dock-note" }, "Choose its folder to upload this file."),
      element(
        "div",
        { class: "action-dock" },
        button("Choose folder", "open", () => this.browse(), {
          disabled: this.isBlocked,
          key: "drop-browse",
        })
      )
    );
  }
}

export function localInspector(this: Workbench, asset: LocalAsset): HTMLElement {
  const isAnimated = asset.isAnimated || asset.isGif || asset.isSpriteSheet;
  const counts = this.counts;
  const isStickerFull = counts.stickers >= counts.stickersMax;
  const isEmojiFull = counts.emojis >= counts.emojisMax;
  const details = element(
    "dl",
    { class: "detail-list" },
    detail("Dimensions", asset.width && asset.height ? `${numberText(asset.width)} × ${numberText(asset.height)}` : "Unknown"),
    detail("Format", formatExt(asset.ext)),
    detail("Size", formatSize(asset.sizeBytes, asset.sizeText)),
    detail("Folder", asset.relativeDir ? pathText(asset.relativeDir, 24) : "Root", asset.relativeDir || asset.path),
    asset.frames ? detail("Frames", numberText(asset.frames)) : null
  );
  const styles = element(
    "select",
    {
      id: "animation-style",
      "data-key": "animation-style",
      disabled: this.isBlocked,
    },
    animationStyles.map((style) => element("option", { value: style, selected: this.style === style }, labelStyle(style)))
  );
  styles.value = this.style;
  styles.addEventListener("change", () => {
    this.style = styles.value;
  });
  const loops = element(
    "select",
    {
      id: "loop-style",
      "data-key": "loop-style",
      disabled: !isAnimated || this.isBlocked,
    },
    element("option", { value: "linear", selected: this.loop === "linear" }, "Linear"),
    element("option", { value: "pingpong", selected: this.loop === "pingpong" }, "Ping-pong")
  );
  loops.value = this.loop;
  loops.addEventListener("change", () => {
    this.loop = loops.value === "pingpong" ? "pingpong" : "linear";
    replaceMedia.setLoop(this.loop);
  });
  const disabledReason = !this.isConnected
    ? "Reconnect to upload."
    : isStickerFull && isEmojiFull
      ? "Sticker and emoji slots are full."
      : isStickerFull
        ? "Sticker slots are full."
        : isEmojiFull
          ? "Emoji slots are full."
          : isAnimated
            ? ""
            : "Animated upload needs a GIF or sprite sheet.";
  const primary: UploadKind = this.rootKind === "stickers" || (isEmojiFull && !isStickerFull) ? "sticker" : isAnimated ? "emojianimated" : "emoji";
  const choices: readonly [UploadKind, string, Icon, boolean, string][] = [
    ["sticker", "Upload sticker", "sticker", isStickerFull, "Sticker slots are full."],
    ["emoji", "Upload emoji", "emoji", isEmojiFull, "Emoji slots are full."],
    [
      "emojianimated",
      "Upload animated",
      "film",
      isEmojiFull || !isAnimated,
      isEmojiFull ? "Emoji slots are full." : "Animated upload needs a GIF or sprite sheet.",
    ],
  ];
  const actions = element(
    "div",
    { class: "action-dock" },
    choices.map(([kind, label, glyph, isUnavailable, reason]) =>
      button(label, glyph, () => this.upload(asset, kind), {
        class: kind === primary ? "primary-button upload-action" : "upload-action",
        disabled: this.isBlocked || !this.isConnected || isUnavailable,
        title: !this.isConnected ? "Reconnect to upload" : isUnavailable ? reason : label,
        key: kind,
      })
    ),
    button(
      "Reveal in Explorer",
      "external",
      () => {
        void api.openExternal({ revealPath: asset.path }).catch((error: unknown) => this.toast(errorText(error), "error"));
      },
      { iconOnly: true, class: "reveal-button", key: "reveal" }
    )
  );
  return element(
    "div",
    { class: "inspector-body" },
    details,
    element(
      "div",
      { class: "composer" },
      element("label", { class: "field", for: "animation-style" }, "Animation style", styles),
      element("label", { class: "field", for: "loop-style" }, "Loop style", loops)
    ),
    actions,
    this.busy === "upload" && this.isBusyVisible
      ? element("p", { class: "dock-note", role: "status" }, this.busyLabel)
      : disabledReason
        ? element("p", { class: "dock-note" }, disabledReason)
        : null
  );
}

export function upload(this: Workbench, asset: LocalAsset, kind: UploadKind): void {
  const animationStyle = this.style;
  const loopStyle = this.loop;
  void this.operate("upload", "Uploading…", async () => {
    await api.upload({
      assetId: asset.id,
      kind,
      animationStyle,
      loopStyle,
    });
    const label = kind === "sticker" ? "sticker" : "emoji";
    this.toast(`Uploaded ${label} to VRChat`);
    await this.loadState();
    await this.loadRemote();
  }).catch(() => logFailure("Background workbench-preview operation failed"));
}
