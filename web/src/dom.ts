type Attribute = string | number | boolean | null | undefined;
type Child = Node | string | number | null | undefined | false | readonly Child[];

export function element<K extends keyof HTMLElementTagNameMap>(tag: K, attrs: Record<string, Attribute> = {}, ...children: Child[]): HTMLElementTagNameMap[K] {
  const node = document.createElement(tag);
  for (const [name, value] of Object.entries(attrs)) {
    if (value !== null && value !== undefined && value !== false) {
      node.setAttribute(name, value === true ? "" : String(value));
    }
  }
  const add = (child: Child) => {
    if (child === null || child === undefined || child === false) return;
    if (typeof child === "string" || typeof child === "number") node.append(String(child));
    else if (child instanceof Node) node.append(child);
    else child.forEach(add);
  };
  children.forEach(add);
  return node;
}

export function byId(id: string): HTMLElement;
export function byId<T extends HTMLElement>(id: string, kind: new () => T): T;
export function byId(id: string, kind: new () => HTMLElement = HTMLElement): HTMLElement {
  const node = document.getElementById(id);
  if (!(node instanceof kind)) throw new Error(`Missing element: ${id}`);
  return node;
}

const icons = {
  library: "M4 7h12v12H4zM7 4h13v12M7 15l3-3 3 3",
  stage: "M4 8V4h4m8 0h4v4m0 8v4h-4m-8 0H4v-4m5-4h6m-3-3v6",
  cloud: "M7 18h11a3.5 3.5 0 0 0 .4-7 6.5 6.5 0 0 0-12.3-1A4 4 0 0 0 7 18z",
  offline: "m4 4 16 16M7 18a4 4 0 0 1-1.7-7.6m3.3-3.2a6.4 6.4 0 0 1 9.8 3.8 3.5 3.5 0 0 1 1.8 6.3",
  link: "M10 8h-2a4 4 0 0 0 0 8h2m4-8h2a4 4 0 0 1 0 8h-2m-6-4h8",
  settings:
    "m10 3-.8 2.5-2 .9-2.4-.8-1.6 2.8L5 10.3v2.3l-1.8 1.8 1.6 2.8 2.4-.7 2 .9.8 2.6h3.2l.8-2.6 2-.9 2.4.7 1.6-2.8-1.8-1.8v-2.3L20 8.4l-1.6-2.8-2.4.8-2-.9L13.2 3zM14.5 11.5a3 3 0 1 1-6 0 3 3 0 0 1 6 0z",
  folder: "M3.5 7a2 2 0 0 1 2-2h4l2 2h7a2 2 0 0 1 2 2v8.5a2 2 0 0 1-2 2h-13a2 2 0 0 1-2-2z",
  open: "M4 8a2 2 0 0 1 2-2h3.5l2 2H18a2 2 0 0 1 2 2M4 8v10h13.5l3-7H7.5L4 18",
  refresh: "M19 10a7 7 0 0 0-12-3L4 10m0-5v5h5m-4 4a7 7 0 0 0 12 3l3-3m0 5v-5h-5",
  search: "M10.5 16.5a6 6 0 1 1 0-12 6 6 0 0 1 0 12zM15 15l5 5",
  close: "m7 7 10 10M17 7 7 17",
  chevron: "m9 5 7 7-7 7",
  down: "m5 9 7 7 7-7",
  grid: "M4 4h16v16H4zM4 12h16M12 4v16",
  dark: "M5 5h14v14H5zM9 15l6-6",
  light: "M5 5h14v14H5zM9 9h6v6H9z",
  film: "M4 5h16v14H4zM8 5v14m8-14v14M4 10h4m-4 4h4m8-4h4m-4 4h4",
  play: "m8 5 11 7-11 7z",
  pause: "M8 5v14m8-14v14",
  loop: "M5 9V6h12l3 3m-4 5 4-5-4-4m3 10v3H7l-3-3m4-5-4 5 4 4",
  ping: "M5 8h14m-4-4 4 4-4 4m4 4H5m4-4-4 4 4 4",
  upload: "M12 16V4m-5 5 5-5 5 5M5 15v5h14v-5",
  trash: "M5 7h14m-9 0V4h4v3M7 7l1 13h8l1-13m-7 4v5m4-5v5",
  image: "M4 5h16v14H4zm0 11 5-5 4 4 3-3 4 4m-4-7h1",
  external: "M12 5h7v7m0-7-9 9m-1-8H5v13h13v-4",
  check: "m5 12 5 5L19 7",
  warning: "M12 4 3 19h18zM12 9v4m0 3h.01",
  download: "M12 4v11m-5-5 5 5 5-5M5 16v4h14v-4",
  sticker: "M6 4h12a2 2 0 0 1 2 2v8l-6 6H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2zm8 16v-6h6",
  emoji: "M20 12a8 8 0 1 1-16 0 8 8 0 0 1 16 0zm-11 1.5q3 4 6 0M9 9.5h.01m6 0h.01",
  asset: "M6 4h8l4 4v12H6zM14 4v5h4M9 15l2-2 4 4",
  layers: "m3 9 9-5 9 5-9 5zM3 13l9 5 9-5M3 17l9 5 9-5",
} as const;

export type Icon = keyof typeof icons;
const svgNamespace = "http://www.w3.org/2000/svg";

function drawing(viewBox: string, paths: readonly [string, string][]): SVGSVGElement {
  const svg = document.createElementNS(svgNamespace, "svg");
  svg.setAttribute("viewBox", viewBox);
  svg.setAttribute("aria-hidden", "true");
  svg.setAttribute("focusable", "false");
  for (const [path, className] of paths) {
    const shape = document.createElementNS(svgNamespace, "path");
    shape.setAttribute("d", path);
    if (className) shape.setAttribute("class", className);
    svg.append(shape);
  }
  return svg;
}

export function icon(name: Icon): SVGSVGElement {
  const svg = drawing("0 0 24 24", [[icons[name], ""]]);
  svg.setAttribute("class", "icon");
  return svg;
}

export function mark(): SVGSVGElement {
  return drawing("0 0 32 32", [
    ["M16 2 29 9.5v13L16 30 3 22.5v-13z", "mark-shell"],
    ["m7 11 8.8 5.2V26M25 11l-9.2 5.2M11 9l5-3 5 3-5 3z", "mark-lines"],
    ["m19 20 6-3.5V21l-6 3.5z", "mark-fill"],
  ]);
}

export function illustration(kind: "folder" | "search" | "stage" | "remote" | "offline" | "error"): SVGSVGElement {
  const paths: [string, string][] = [
    ["M5 21v-9h9m96 0h9v9m0 62v9h-9m-96 0H5v-9", "illustration-guides"],
    ["M14 88h96M62 8v8m0 70v10M2 52h8m104 0h8", "illustration-guides"],
  ];
  if (kind === "stage") {
    paths.push(
      ["m20 61 43-24 43 24-43 24z", "illustration-shadow"],
      ["m20 53 43-24 43 24-43 24z", "illustration-base"],
      ["m20 45 43-24 43 24-43 24z", "illustration-top"],
      ["m31 45 32-18 32 18-32 18zM43 52l32-18M53 58l32-18M43 39l32 18M53 33l32 18", "illustration-guides"],
      ["m51 45 12-7 13 7-13 7zM20 45v8l43 24 43-24v-8", "illustration-key"]
    );
  } else if (kind === "folder" || kind === "search") {
    paths.push(
      ["m25 69 7-38h27l8 8h32v30z", "illustration-base"],
      ["M25 69 38 47h68L94 69z", "illustration-top"],
      kind === "search"
        ? ["M76 49a12 12 0 1 1-24 0 12 12 0 0 1 24 0zm-3 9 14 14", "illustration-key"]
        : ["M43 60h17m-17 4h9M73 27v9m-4-5h8", "illustration-key"]
    );
  } else {
    paths.push(
      ["M39 66h49a14 14 0 0 0 0-28 25 25 0 0 0-46-4 16 16 0 0 0-3 32z", "illustration-base"],
      ["m43 72 19 11 19-11-19-11z", "illustration-shadow"],
      kind === "offline"
        ? ["m53 42 18 18m0-18L53 60", "illustration-key"]
        : kind === "error"
          ? ["M62 40v13m0 6h.01", "illustration-key"]
          : ["m50 51 12-7 12 7-12 7z", "illustration-key"]
    );
  }
  const svg = drawing("0 0 124 104", paths);
  svg.setAttribute("class", "illustration");
  return svg;
}

export function button(
  label: string,
  glyph: Icon | null,
  action: (event: MouseEvent) => void,
  options: {
    class?: string;
    iconOnly?: boolean;
    pressed?: boolean;
    disabled?: boolean;
    blocked?: boolean;
    title?: string;
    key?: string;
  } = {}
): HTMLButtonElement {
  const node = element(
    "button",
    {
      type: "button",
      class: `button${options.class ? ` ${options.class}` : ""}${options.iconOnly ? " icon-button" : ""}`,
      title: options.title ?? label,
      "aria-label": label,
      "aria-pressed": options.pressed === undefined ? undefined : String(options.pressed),
      "aria-disabled": options.blocked ? "true" : undefined,
      "data-key": options.key,
      disabled: options.disabled,
    },
    glyph ? icon(glyph) : null,
    options.iconOnly ? null : element("span", { class: "button-label" }, label)
  );
  node.addEventListener("click", (event) => {
    if (!options.blocked) action(event);
  });
  return node;
}

export function setContents(node: HTMLElement, ...children: (Node | string)[]): void {
  const active = document.activeElement;
  const key = active && node.contains(active) ? active.getAttribute("data-key") : null;
  node.replaceChildren(...children);
  if (key) {
    for (const candidate of node.querySelectorAll<HTMLElement>("[data-key]")) {
      if (candidate.getAttribute("data-key") === key) {
        candidate.focus({ preventScroll: true });
        break;
      }
    }
  }
}

export function thumbnail(url: string, classes = ""): HTMLElement {
  const placeholder = icon("image");
  const box = element("span", { class: `thumbnail checker ${classes}` }, placeholder);
  if (url) {
    const image = element("img", {
      alt: "",
      draggable: "false",
      loading: "lazy",
    });
    image.addEventListener("error", () => image.remove(), { once: true });
    image.addEventListener("load", () => placeholder.remove(), { once: true });
    image.src = url;
    box.append(image);
  }
  return box;
}

export function emptyState(kind: Parameters<typeof illustration>[0], title: string, detail?: string, ...actions: HTMLElement[]): HTMLElement {
  return element(
    "div",
    { class: "empty-state" },
    illustration(kind),
    element("h3", {}, title),
    detail ? element("p", {}, detail) : null,
    actions.length ? element("div", { class: "control-row" }, actions) : null
  );
}
