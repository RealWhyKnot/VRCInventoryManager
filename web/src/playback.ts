import { decompressFrame, parseGIF } from "gifuct-js";
import { api, logFailure, type LocalAsset, type LoopStyle, type RemoteItem, type SpriteSheet } from "./api";
import { button, element, icon } from "./dom";
import { decimalText } from "./format";

export type PreviewItem =
  { source: "local"; asset: LocalAsset } | { source: "remote"; asset: RemoteItem } | { source: "drop"; name: string; size: number; url: string; file: File };

type Animation = {
  frames: number;
  fps: number;
  canvas: HTMLCanvasElement;
  draw: (index: number) => void;
  delay: (index: number) => number;
};

const maxGifBytes = 32 * 1024 * 1024;
const maxGifPixels = 4 * 1024 * 1024;
const maxGifWork = 96 * 1024 * 1024;

function canvas2d(width: number, height: number) {
  const canvas = element("canvas", { width, height, class: "preview-media" });
  const context = canvas.getContext("2d");
  if (!context) throw new Error("Canvas is unavailable.");
  return { canvas, context };
}

function loadImage(url: string): Promise<HTMLImageElement> {
  return new Promise((resolve, reject) => {
    const image = element("img");
    image.addEventListener("load", () => resolve(image), { once: true });
    image.addEventListener("error", () => reject(new Error("Couldn’t load the image.")), { once: true });
    image.src = url;
  });
}

function sheetAnimation(image: HTMLImageElement, sheet: SpriteSheet): Animation {
  const { cols, rows, fps, frames } = sheet;
  const cellWidth = Math.floor((image.naturalWidth || sheet.width) / cols);
  const cellHeight = Math.floor((image.naturalHeight || sheet.height) / rows);
  if (
    !Number.isInteger(cols) ||
    !Number.isInteger(rows) ||
    !Number.isInteger(frames) ||
    !(cellWidth > 0 && cellHeight > 0 && frames > 1 && fps > 0 && frames <= cols * rows)
  ) {
    throw new Error("This sprite sheet has an invalid frame layout.");
  }
  const scale = Math.min(1, 1536 / Math.max(cellWidth, cellHeight));
  const width = Math.max(1, Math.round(cellWidth * scale));
  const height = Math.max(1, Math.round(cellHeight * scale));
  const { canvas, context } = canvas2d(width, height);
  return {
    canvas,
    frames,
    fps,
    delay: () => 1000 / fps,
    draw: (index) => {
      context.clearRect(0, 0, width, height);
      context.drawImage(image, (index % cols) * cellWidth, Math.floor(index / cols) * cellHeight, cellWidth, cellHeight, 0, 0, width, height);
    },
  };
}

async function gifBytes(item: PreviewItem, url: string, signal: AbortSignal): Promise<ArrayBuffer> {
  if (item.source === "drop") return item.file.arrayBuffer();
  if (item.source === "local" && item.asset.sizeBytes > maxGifBytes) throw new Error("This GIF is too large to decode.");
  const response = await fetch(url, { signal });
  if (!response.ok || Number(response.headers.get("Content-Length")) > maxGifBytes) {
    throw new Error("Couldn’t load the GIF frames.");
  }
  return response.arrayBuffer();
}

async function gifAnimation(item: PreviewItem, url: string, signal: AbortSignal): Promise<Animation> {
  const bytes = await gifBytes(item, url, signal);
  if (bytes.byteLength > maxGifBytes) throw new Error("This GIF is too large to decode.");
  const gif = parseGIF(bytes);
  const sourceWidth = gif.lsd.width;
  const sourceHeight = gif.lsd.height;
  const frames = gif.frames.filter((frame) => "image" in frame);
  if (
    gif.header.signature !== "GIF" ||
    sourceWidth < 1 ||
    sourceHeight < 1 ||
    sourceWidth * sourceHeight > maxGifPixels ||
    frames.length < 2 ||
    frames.length > 500
  ) {
    throw new Error("This GIF can’t be decoded for frame preview.");
  }
  const scale = Math.min(1, 512 / Math.max(sourceWidth, sourceHeight), Math.sqrt(8_000_000 / (sourceWidth * sourceHeight * frames.length)));
  const width = Math.max(1, Math.round(sourceWidth * scale));
  const height = Math.max(1, Math.round(sourceHeight * scale));
  const { canvas, context } = canvas2d(width, height);
  const pixels = new Uint8ClampedArray(width * height * 4);
  const image = context.createImageData(width, height);
  const rendered: { pixels: Uint8ClampedArray; delay: number }[] = [];
  let work = 0;
  for (const frame of frames) {
    const descriptor = frame.image.descriptor;
    const area = descriptor.width * descriptor.height;
    work += area;
    if (area > maxGifPixels || work > maxGifWork || area === 0) throw new Error("This GIF is too large to decode.");
    const { dims, patch, disposalType, delay } = decompressFrame(frame, gif.gct, true);
    const previous = disposalType === 3 ? pixels.slice() : null;
    const left = Math.min(width, Math.ceil((dims.left * width) / sourceWidth));
    const top = Math.min(height, Math.ceil((dims.top * height) / sourceHeight));
    const right = Math.min(width, Math.ceil(((dims.left + dims.width) * width) / sourceWidth));
    const bottom = Math.min(height, Math.ceil(((dims.top + dims.height) * height) / sourceHeight));
    for (let y = top; y < bottom; y++) {
      const row = (Math.floor((y * sourceHeight) / height) - dims.top) * dims.width;
      for (let x = left; x < right; x++) {
        const input = (row + Math.floor((x * sourceWidth) / width) - dims.left) * 4;
        if (patch[input + 3]) pixels.set(patch.subarray(input, input + 4), (y * width + x) * 4);
      }
    }
    rendered.push({
      pixels: pixels.slice(),
      delay: delay < 20 ? 100 : delay || 100,
    });
    if (previous) pixels.set(previous);
    else if (disposalType === 2) {
      for (let y = top; y < bottom; y++) pixels.fill(0, (y * width + left) * 4, (y * width + right) * 4);
    }
  }
  const duration = rendered.reduce((sum, frame) => sum + frame.delay, 0);
  return {
    canvas,
    frames: rendered.length,
    fps: (1000 * rendered.length) / duration,
    delay: (index) => rendered[index]?.delay ?? 100,
    draw: (index) => {
      const frame = rendered[index];
      if (!frame) return;
      image.data.set(frame.pixels);
      context.putImageData(image, 0, 0);
    },
  };
}

function remoteSheet(item: RemoteItem, image: HTMLImageElement): SpriteSheet {
  const frames = item.frames ?? 0;
  const cols = Math.min(8, 2 ** Math.ceil(Math.log2(Math.ceil(Math.sqrt(frames)))));
  return {
    frames,
    fps: item.fps ?? 15,
    cols,
    rows: cols,
    width: image.naturalWidth,
    height: image.naturalHeight,
    imageUrl: item.previewUrl,
  };
}

export class Player {
  private token = 0;
  private animation: Animation | null = null;
  private index = 0;
  private direction = 1;
  private loop: LoopStyle = "linear";
  private timer: number | null = null;
  private isPlaying = false;
  private playButton: HTMLButtonElement | null = null;
  private loopButton: HTMLButtonElement | null = null;
  private readout: HTMLElement | null = null;
  private progress: HTMLElement | null = null;

  private readonly surface: HTMLElement;
  private readonly controls: HTMLElement;
  private request: AbortController | null = null;

  constructor(surface: HTMLElement, controls: HTMLElement) {
    this.surface = surface;
    this.controls = controls;
  }

  clear(): void {
    this.token++;
    this.request?.abort();
    this.request = null;
    this.stopTimer();
    this.animation = null;
    this.isPlaying = false;
    this.playButton = this.loopButton = null;
    this.progress = this.readout = null;
    this.surface.replaceChildren();
    this.controls.replaceChildren(element("span", { class: "transport-idle" }, icon("film"), "No preview selected"));
  }

  setLoop(loop: LoopStyle): void {
    this.loop = loop;
    this.direction = 1;
    if (this.loopButton) {
      this.loopButton.setAttribute("aria-pressed", String(loop === "pingpong"));
      this.loopButton.replaceChildren(
        icon(loop === "pingpong" ? "ping" : "loop"),
        element("span", { class: "button-label" }, loop === "pingpong" ? "Ping-pong" : "Linear")
      );
    }
  }

  async show(item: PreviewItem): Promise<void> {
    this.clear();
    const token = this.token;
    const controller = new AbortController();
    this.request = controller;
    const asset = item.source === "drop" ? null : item.asset;
    const url = item.source === "drop" ? item.url : item.asset.previewUrl || item.asset.thumbUrl;
    const name = item.source === "drop" ? item.name : item.asset.name;
    const isGif =
      item.source === "drop"
        ? /\.gif$/i.test(item.name) || item.file.type === "image/gif"
        : item.source === "local"
          ? item.asset.isGif
          : /\.gif(?:$|[?#])/i.test(url) || /\.gif$/i.test(item.asset.name);
    this.loop = asset?.loopStyle?.toLowerCase() === "pingpong" ? "pingpong" : "linear";
    const loading = element("span", { class: "preview-status", role: "status" }, "Loading preview…");
    this.surface.replaceChildren(loading);
    this.controls.replaceChildren();
    try {
      let animation: Animation | null = null;
      if (isGif) {
        animation = await gifAnimation(item, url, AbortSignal.any([controller.signal, AbortSignal.timeout(30_000)])).catch(() => {
          if (!controller.signal.aborted) logFailure("Using image fallback for GIF preview");
          return null;
        });
      } else if (item.source === "local" && item.asset.isSpriteSheet) {
        const metadata = await api.spriteSheet(item.asset.id, item.asset.spriteSheetUrl);
        if (token !== this.token) return;
        animation = sheetAnimation(await loadImage(metadata.imageUrl || url), metadata);
      } else if (item.source === "remote" && (item.asset.frames ?? 0) > 1) {
        const image = await loadImage(url);
        animation = sheetAnimation(image, remoteSheet(item.asset, image));
      }
      if (token !== this.token) return;
      if (animation) {
        this.animation = animation;
        this.index = 0;
        this.direction = 1;
        animation.canvas.setAttribute("role", "img");
        animation.canvas.setAttribute("aria-label", name);
        this.surface.replaceChildren(animation.canvas);
        this.mountTransport();
        this.renderFrame();
        if (!window.matchMedia("(prefers-reduced-motion: reduce)").matches) this.toggle();
        return;
      }
      const image = await loadImage(url);
      if (token !== this.token) return;
      image.className = "preview-media";
      image.alt = name;
      this.surface.replaceChildren(image);
      this.controls.replaceChildren(element("span", { class: "transport-idle" }, icon(isGif ? "film" : "image"), isGif ? "GIF preview" : "Still image"));
    } catch {
      if (token !== this.token) return;
      logFailure(`${item.source} preview failed`);
      this.surface.replaceChildren(element("span", { class: "preview-status", role: "status" }, "Preview unavailable"));
      this.controls.replaceChildren(element("span", { class: "transport-idle" }, icon("warning"), "Couldn’t open preview"));
    }
  }

  private mountTransport(): void {
    this.playButton = button("Play preview", "play", () => this.toggle(), {
      iconOnly: true,
    });
    this.readout = element("span", { class: "frame-count numeric" });
    this.progress = element("span", { class: "frame-progress-fill" });
    this.loopButton = button("Ping-pong preview", "loop", () => this.setLoop(this.loop === "linear" ? "pingpong" : "linear"), {
      pressed: false,
      class: "preview-loop",
    });
    this.setLoop(this.loop);
    this.controls.replaceChildren(
      this.playButton,
      this.readout,
      element("span", { class: "frame-progress", "aria-hidden": "true" }, this.progress),
      this.loopButton
    );
  }

  private renderFrame(): void {
    const animation = this.animation;
    if (!animation || !this.readout || !this.progress) return;
    animation.draw(this.index);
    const fps = decimalText(animation.fps);
    this.readout.textContent = `Frame ${this.index + 1} / ${animation.frames} · ${fps} fps`;
    this.progress.style.setProperty("--frame-progress", `${((this.index + 1) / animation.frames) * 100}%`);
  }

  private toggle(): void {
    if (!this.animation || !this.playButton) return;
    this.isPlaying = !this.isPlaying;
    const label = this.isPlaying ? "Pause preview" : "Play preview";
    this.playButton.title = label;
    this.playButton.setAttribute("aria-label", label);
    this.playButton.replaceChildren(icon(this.isPlaying ? "pause" : "play"));
    if (this.isPlaying) this.scheduleFrame();
    else this.stopTimer();
  }

  private scheduleFrame(): void {
    const animation = this.animation;
    if (!animation || !this.isPlaying) return;
    this.timer = window.setTimeout(
      () => {
        if (this.loop === "linear") this.index = (this.index + 1) % animation.frames;
        else {
          if (this.index + this.direction >= animation.frames) this.direction = -1;
          if (this.index + this.direction < 0) this.direction = 1;
          this.index += this.direction;
        }
        this.renderFrame();
        this.scheduleFrame();
      },
      Math.max(16, animation.delay(this.index))
    );
  }

  private stopTimer(): void {
    if (this.timer !== null) window.clearTimeout(this.timer);
    this.timer = null;
  }
}
