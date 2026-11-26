import { nonEmpty, numberText } from "./format";
import { logFailure, api, type Folder, type RootKind } from "./api";
import { button, byId, element, emptyState, icon, setContents } from "./dom";
import { itemCount, localCard, normalizedPath, pathText, remoteCard, remoteFilters, sources, type RemoteFilter } from "./library";
import type { Workbench } from "./workbench";
import { pageSize, rootSearch, subfolders } from "./workbench-shared";

export function renderLocalTools(this: Workbench): void {
  setContents(
    byId("local-tools"),
    button(this.working("browse", "Browse"), "open", () => this.browse(), {
      class: "compact-action",
      disabled: this.isBlocked,
      key: "browse",
    }),
    button("Refresh local files", "refresh", () => this.refreshLocal(), {
      iconOnly: true,
      disabled: this.isBlocked,
      key: "refresh-local",
    })
  );
  setContents(
    byId("sources"),
    ...sources.map(([kind, label]) =>
      button(label, null, () => this.changeRoot(kind), {
        pressed: this.rootKind === kind,
        disabled: this.isBlocked,
        key: kind,
      })
    )
  );
  subfolders.checked = this.shouldIncludeSubfolders;
  if (this.isBlocked) subfolders.setAttribute("aria-disabled", "true");
  else subfolders.removeAttribute("aria-disabled");
  byId("clear-search").hidden = !rootSearch.value;
}

export function renderRemoteTools(this: Workbench): void {
  setContents(
    byId("remote-tools"),
    button("Refresh inventory", "refresh", () => this.refreshRemote(), {
      iconOnly: true,
      disabled: this.isBlocked || !this.isConnected,
      key: "refresh-remote",
    })
  );
  const { stickers, emojis } = this.counts;
  const counts = {
    all: stickers + emojis,
    stickers,
    emojis,
  } as const satisfies Record<RemoteFilter, number>;
  setContents(
    byId("remote-filters"),
    ...remoteFilters.map(([filter, label]) => {
      const choice = button(
        label,
        null,
        () => {
          this.prefs.remote = filter;
          this.persist();
          this.renderRemote();
          byId("remote-scroll").scrollTop = 0;
        },
        { pressed: this.prefs.remote === filter, key: filter }
      );
      choice.setAttribute("aria-label", `${label}, ${itemCount(counts[filter], "item")}`);
      return choice;
    })
  );
  for (const action of byId("remote-collection").querySelectorAll<HTMLButtonElement>(".remote-delete")) action.disabled = this.isBlocked;
}

export function renderLocal(this: Workbench): void {
  this.renderLocalTools();
  this.renderFolders();
  this.renderLocalCollection();
  const root = nonEmpty(this.library?.root) ?? this.state?.localRoot;
  const count = this.library?.assets.length ?? 0;
  setContents(
    byId("local-footer"),
    element("span", { class: "footer-path truncate", title: root ?? "" }, icon("folder"), root ? pathText(root, 42) : "Choose a library folder"),
    element("span", { class: "numeric footer-count" }, itemCount(count, "file"))
  );
}

export function renderFolders(this: Workbench): void {
  const node = byId("folders");
  if (!this.library?.folders.length || !this.library.totalCount) {
    node.hidden = true;
    return;
  }
  node.hidden = false;
  const root = this.library.folders.find((folder) => !folder.relativePath);
  const children = root?.children ?? this.library.folders;
  const expand = button(
    "Folders",
    this.isFolderExpanded ? "down" : "chevron",
    () => {
      this.isFolderExpanded = !this.isFolderExpanded;
      this.renderFolders();
    },
    { class: "folders-toggle", key: "toggle-folders" }
  );
  expand.setAttribute("aria-expanded", String(this.isFolderExpanded));
  expand.setAttribute("aria-controls", "folder-list");
  const header = element(
    "div",
    { class: "folders-head" },
    expand,
    element(
      "span",
      {
        class: "folder-columns numeric",
        title: "Files here / files including subfolders",
      },
      "Here / total"
    )
  );
  const list = element("ul", {
    id: "folder-list",
    class: "folder-list",
    hidden: !this.isFolderExpanded,
  });
  if (this.isFolderExpanded) {
    list.append(this.folderRow("", "All", root?.directCount ?? this.library.assets.filter((asset) => !asset.relativeDir).length, this.library.totalCount, 0));
    for (const folder of children) list.append(this.folderNode(folder, 0));
  }
  setContents(node, header, list);
}

export function folderNode(this: Workbench, folder: Folder, depth: number): HTMLElement {
  const key = normalizedPath(folder.relativePath);
  const selectedPath = normalizedPath(this.folder);
  const isExpanded = this.openFolders.has(key) || selectedPath.startsWith(`${key}/`);
  const item = this.folderRow(folder.relativePath, folder.name, folder.directCount, folder.totalCount, depth, folder.path);
  if (folder.children.length && depth < 24) {
    const expand = button(
      `${isExpanded ? "Collapse" : "Expand"} ${folder.name}`,
      isExpanded ? "down" : "chevron",
      () => {
        if (isExpanded) {
          this.openFolders.delete(key);
          if (selectedPath.startsWith(`${key}/`)) this.chooseFolder(folder.relativePath);
        } else this.openFolders.add(key);
        this.renderFolders();
      },
      { iconOnly: true, class: "folder-expand", key: `expand:${key}` }
    );
    expand.setAttribute("aria-expanded", String(isExpanded));
    item.firstElementChild?.append(expand);
    if (isExpanded) {
      const nested = element("ul", { class: "folder-list" });
      for (const child of folder.children) nested.append(this.folderNode(child, depth + 1));
      item.append(nested);
    }
  }
  return item;
}

export function folderRow(this: Workbench, path: string, name: string, direct: number, total: number, depth: number, title = name): HTMLElement {
  const isSelected = normalizedPath(path) === normalizedPath(this.folder);
  const row = button(name, path ? "folder" : "layers", () => this.chooseFolder(path), {
    class: "folder-row",
    pressed: isSelected,
    title,
    key: `folder:${path}`,
  });
  row.append(element("span", { class: "folder-count numeric", "aria-hidden": "true" }, `${numberText(direct)} / ${numberText(total)}`));
  row.setAttribute("aria-label", `${name}, ${itemCount(direct, "file")} here, ${numberText(total)} total`);
  const line = element("div", { class: "folder-line" }, row);
  line.style.setProperty("--folder-indent", `${Math.min(depth, 5) * 12}px`);
  return element("li", {}, line);
}

export function renderLocalCollection(this: Workbench): void {
  const node = byId("local-collection");
  const assets = this.library?.assets ?? [];
  if (this.localError) {
    setContents(
      node,
      emptyState(
        "error",
        "Couldn’t load files",
        this.localError,
        button("Refresh", "refresh", () => this.refreshLocal(), {
          blocked: this.isBlocked,
          key: "retry-local",
        })
      )
    );
  } else if (!this.library || (!assets.length && this.state?.busy && !this.query)) {
    setContents(node, this.loading(nonEmpty(this.state?.busyMessage) ?? "Loading files…", true));
  } else if (!assets.length) {
    setContents(
      node,
      this.query
        ? emptyState(
            "search",
            "No matching files",
            undefined,
            button("Clear search", "close", () => this.clearSearch())
          )
        : emptyState(
            "folder",
            "Folder is empty",
            undefined,
            button("Choose folder", "open", () => this.browse(), {
              class: "primary-button",
              disabled: this.isBlocked,
            }),
            button("Refresh", "refresh", () => this.refreshLocal(), {
              disabled: this.isBlocked,
            })
          )
    );
  } else {
    const list = element(
      "div",
      { class: "asset-grid" },
      assets
        .slice(0, this.visibleLimit)
        .map((asset) => localCard(asset, this.selected?.source === "local" && this.selectedId === asset.id, () => this.select({ source: "local", asset })))
    );
    const heading = element("div", { class: "collection-head" }, element("h3", {}, "Files"), element("span", { class: "numeric" }, numberText(assets.length)));
    setContents(node, heading, list);
    if (assets.length > this.visibleLimit) {
      node.append(
        element(
          "div",
          { class: "load-more" },
          button("Show more files", "down", () => {
            const oldLimit = this.visibleLimit;
            this.visibleLimit += pageSize;
            this.renderLocalCollection();
            const next = assets[oldLimit];
            if (next) this.focusItem("local", next.id);
          })
        )
      );
    }
  }
}

export function renderRemote(this: Workbench): void {
  this.renderRemoteTools();
  const node = byId("remote-collection");
  if (this.remoteError) {
    setContents(
      node,
      emptyState(
        "error",
        "Couldn’t load inventory",
        this.remoteError,
        button("Refresh", "refresh", () => this.refreshRemote(), {
          blocked: this.isBlocked,
          key: "retry-remote",
        })
      )
    );
  } else if ((!this.state && !this.inventory) || (!this.isConnected && this.state?.busy && !this.state.authError)) {
    setContents(node, this.loading(nonEmpty(this.state?.busyMessage) ?? "Loading inventory…", false));
  } else if (!this.isConnected) {
    setContents(
      node,
      emptyState(
        "offline",
        "VRChat disconnected",
        "Sign in to VRCX, then reconnect.",
        button("Reconnect", "link", () => this.reconnect(), {
          class: "primary-button",
          blocked: this.isBlocked,
          key: "reconnect-empty",
        })
      )
    );
  } else if (!this.filteredRemote.length) {
    setContents(node, emptyState("remote", this.prefs.remote === "all" ? "Inventory is empty" : `No ${this.prefs.remote}`));
  } else {
    const list = element(
      "div",
      { class: "remote-list" },
      this.filteredRemote.map((item) =>
        remoteCard(
          item,
          this.selected?.source === "remote" && item.id === this.selectedId,
          this.isBlocked,
          () => this.select({ source: "remote", asset: item }),
          () => this.openDelete(item)
        )
      )
    );
    setContents(node, list);
  }
  setContents(
    byId("remote-footer"),
    element("span", { class: "footer-path" }, icon("cloud"), "VRChat"),
    element("span", { class: "numeric footer-count" }, itemCount(this.filteredRemote.length, "item"))
  );
}

export function loading(this: Workbench, label: string, isLocal: boolean): HTMLElement {
  return element(
    "div",
    { class: "loading-state", role: "status" },
    element("span", { class: "loading-label" }, icon("refresh"), label),
    element(
      "div",
      {
        class: isLocal ? "asset-grid loading-grid" : "remote-list loading-list",
        "aria-hidden": "true",
      },
      Array.from({ length: isLocal ? 6 : 5 }, () => element("span", { class: "loading-block" }))
    )
  );
}

export function changeRoot(this: Workbench, kind: RootKind): void {
  if (kind === this.rootKind) return;
  if (kind === "custom" && !this.prefs.customRoot) {
    this.browse();
    return;
  }
  const path = kind === "custom" ? this.prefs.customRoot : undefined;
  void this.operate("local", "Opening folder…", async () => {
    await api.selectRoot(kind, this.shouldIncludeSubfolders, nonEmpty(path) ?? undefined);
    await this.loadState();
    await this.loadLocal(true);
  }).catch(() => logFailure("Background workbench-library operation failed"));
}

export function browse(this: Workbench): void {
  void this.operate("browse", "Choosing folder…", async () => {
    const result = await api.browse();
    if (result.cancelled || !result.path) return;
    if (!result.selectedRootKind) await api.selectRoot("custom", this.shouldIncludeSubfolders, result.path);
    await this.loadState();
    await this.loadLocal(true);
  }).catch(() => logFailure("Background workbench-library operation failed"));
}

export function refreshLocal(this: Workbench): void {
  void this.operate("local", "Refreshing files…", async () => {
    await api.refreshLocal();
    await this.loadState();
    await this.loadLocal();
  }).catch(() => logFailure("Background workbench-library operation failed"));
}

export function refreshRemote(this: Workbench): void {
  void this.operate("remote", "Refreshing inventory…", async () => {
    if (this.isConnected) await api.refreshRemote();
    await this.loadState();
    await this.loadRemote();
  }).catch(() => logFailure("Background workbench-library operation failed"));
}

export function reconnect(this: Workbench): void {
  void this.operate("reconnect", "Reconnecting…", async () => {
    await api.reconnect();
    await this.loadState();
    await this.loadRemote();
    if (this.isConnected) this.toast("Connected to VRChat");
    else throw new Error(this.state?.authError ?? this.inventory?.authError ?? "Sign in to VRCX, then reconnect.");
  }).catch(() => logFailure("Background workbench-library operation failed"));
}

export function chooseFolder(this: Workbench, path: string): void {
  if (this.folder === path) return;
  this.folder = path;
  this.prefs.folders[this.rootKind] = path;
  this.persist();
  this.visibleLimit = pageSize;
  this.renderFolders();
  void this.loadLocal().catch(() => logFailure("Background workbench-library operation failed"));
}

export function debounceSearch(this: Workbench): void {
  byId("clear-search").hidden = !rootSearch.value;
  if (this.searchTimer !== null) window.clearTimeout(this.searchTimer);
  this.searchTimer = window.setTimeout(() => this.applySearch(), 250);
}

export function applySearch(this: Workbench): void {
  if (this.searchTimer !== null) window.clearTimeout(this.searchTimer);
  this.searchTimer = null;
  const query = rootSearch.value.trim();
  if (query === this.query) return;
  this.query = query;
  this.persist();
  this.visibleLimit = pageSize;
  void this.loadLocal(true).catch(() => logFailure("Background workbench-library operation failed"));
}

export function clearSearch(this: Workbench): void {
  rootSearch.value = "";
  byId("clear-search").hidden = true;
  this.applySearch();
  rootSearch.focus();
}
