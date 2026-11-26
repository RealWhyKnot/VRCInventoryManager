import {
  logFailure,
  api,
  type Counts,
  type LocalLibrary,
  type LoopStyle,
  type RemoteInventory,
  type RemoteItem,
  type RootKind,
  type State,
  type UpdateOffer,
} from "./api";
import { button, byId, element, icon, mark } from "./dom";
import { remoteKind } from "./library";
import { type PreviewItem } from "./playback";
import * as session from "./workbench-session";
import * as library from "./workbench-library";
import * as preview from "./workbench-preview";
import * as dialogs from "./workbench-dialogs";
import * as input from "./workbench-input";
import {
  type Operation,
  pageSize,
  defaultCounts,
  rootSearch,
  subfolders,
  settingsDialog,
  deleteDialog,
  updateDialog,
  loadPreferences,
} from "./workbench-shared";

export class Workbench {
  readonly prefs = loadPreferences();
  readonly openFolders = new Set<string>();
  readonly reconnectButton = button("Reconnect", "link", () => this.reconnect(), {
    class: "compact-action",
    key: "reconnect",
  });
  state: State | null = null;
  library: LocalLibrary | null = null;
  inventory: RemoteInventory | null = null;
  selected: PreviewItem | null = null;
  rootKind: RootKind = "emoji";
  folder = "";
  query = new URLSearchParams(location.hash.slice(1)).get("search")?.slice(0, 160) ?? "";
  shouldIncludeSubfolders = true;
  isFolderExpanded = !window.matchMedia("(max-width: 1120px)").matches;
  visibleLimit = pageSize;
  style = "stop";
  loop: LoopStyle = "linear";
  localError = "";
  remoteError = "";
  searchTimer: number | null = null;
  localRequest = 0;
  isFirstLibrary = true;
  remoteRequest = 0;
  stateRequest = 0;
  busy: Operation | null = null;
  busyLabel = "";
  operationError = "";
  busyTimer: number | null = null;
  isBusyVisible = false;
  updateProgress: number | null = null;
  dropDepth = 0;
  isDropExpected = false;
  dropSequence = 0;
  importedPaths: string[] = [];
  pendingDelete: RemoteItem | null = null;
  currentOffer: UpdateOffer | null = null;
  seenOffer = "";
  isStreamOpen = false;
  eventTimer: number | null = null;
  readonly receivedEvents = new Set<string>();

  private start = session.start;
  persist = session.persist;
  loadState = session.loadState;
  loadLocal = session.loadLocal;
  loadRemote = session.loadRemote;
  listen = session.listen;
  queueEvent = session.queueEvent;
  syncEvent = session.syncEvent;
  operate = session.operate;
  working = session.working;
  refreshControls = session.refreshControls;
  quota = session.quota;
  renderChrome = session.renderChrome;
  renderProgress = session.renderProgress;

  renderLocalTools = library.renderLocalTools;
  renderRemoteTools = library.renderRemoteTools;
  renderLocal = library.renderLocal;
  renderFolders = library.renderFolders;
  folderNode = library.folderNode;
  folderRow = library.folderRow;
  renderLocalCollection = library.renderLocalCollection;
  renderRemote = library.renderRemote;
  loading = library.loading;
  changeRoot = library.changeRoot;
  browse = library.browse;
  refreshLocal = library.refreshLocal;
  refreshRemote = library.refreshRemote;
  reconnect = library.reconnect;
  chooseFolder = library.chooseFolder;
  private debounceSearch = library.debounceSearch;
  applySearch = library.applySearch;
  clearSearch = library.clearSearch;

  renderBackdrop = preview.renderBackdrop;
  select = preview.select;
  renderStage = preview.renderStage;
  renderInspector = preview.renderInspector;
  localInspector = preview.localInspector;
  upload = preview.upload;

  private openSettings = dialogs.openSettings;
  showDialog = dialogs.showDialog;
  dialogHeading = dialogs.dialogHeading;
  renderSettings = dialogs.renderSettings;
  checkForUpdate = dialogs.checkForUpdate;
  openDelete = dialogs.openDelete;
  renderDelete = dialogs.renderDelete;
  offerUpdate = dialogs.offerUpdate;
  renderUpdate = dialogs.renderUpdate;
  updateAction = dialogs.updateAction;
  dialogStatus = dialogs.dialogStatus;
  toast = dialogs.toast;

  private handleKeys = input.handleKeys;
  focusItem = input.focusItem;
  private mountDrop = input.mountDrop;
  importPaths = input.importPaths;
  inspectDrop = input.inspectDrop;

  constructor() {
    rootSearch.value = this.query;
    byId("studio-actions").append(
      this.reconnectButton,
      button("Settings", "settings", () => this.openSettings(), {
        iconOnly: true,
        key: "settings",
      })
    );
    byId("brand-mark").append(mark());
    byId("local-icon").append(icon("library"));
    byId("stage-icon").append(icon("stage"));
    byId("remote-icon").append(icon("cloud"));
    byId("search-icon").append(icon("search"));
    byId("drop-zone").append(element("div", { class: "drop-target" }, icon("upload"), element("h3", {}, "Drop images here")));
    byId("clear-search").append(
      button("Clear search", "close", () => this.clearSearch(), {
        iconOnly: true,
        class: "search-clear",
      })
    );
    rootSearch.addEventListener("input", () => this.debounceSearch());
    subfolders.addEventListener("click", (event) => {
      if (this.isBlocked) event.preventDefault();
    });
    subfolders.addEventListener("change", () => {
      const isNext = subfolders.checked;
      void this.operate("local", "Updating file list…", async () => {
        const state = await api.settings({ includeSubfolders: isNext });
        this.shouldIncludeSubfolders = state.includeSubfolders;
        this.visibleLimit = pageSize;
        await this.loadLocal();
      }).catch(() => logFailure("Background workbench operation failed"));
    });
    document.addEventListener("keydown", (event) => this.handleKeys(event));
    [settingsDialog, deleteDialog, updateDialog].forEach((dialog) => {
      dialog.addEventListener("close", () => {
        if (dialog === deleteDialog) this.pendingDelete = null;
        if (dialog === updateDialog) this.currentOffer = null;
        this.operationError = "";
        this.offerUpdate();
      });
      dialog.addEventListener("cancel", (event) => {
        if ((dialog === deleteDialog && this.busy === "delete") || (dialog === updateDialog && this.busy === "update")) event.preventDefault();
      });
    });
    this.mountDrop();
    this.renderBackdrop();
    this.renderStage();
    this.renderLocal();
    this.renderRemote();
    this.renderChrome();
    void this.start().catch(() => logFailure("Background workbench operation failed"));
  }

  get isConnected(): boolean {
    return this.state?.connected ?? this.inventory?.connected ?? false;
  }

  get isBlocked(): boolean {
    return this.busy !== null || this.state?.busy === true;
  }

  get counts(): Counts {
    if (this.inventory?.connected === this.isConnected) {
      return {
        stickers: this.inventory.stickersCount,
        stickersMax: this.inventory.stickersMax,
        emojis: this.inventory.emojisCount,
        emojisMax: this.inventory.emojisMax,
      };
    }
    return this.state?.counts ?? defaultCounts;
  }

  get filteredRemote(): RemoteItem[] {
    const items = this.inventory?.items ?? [];
    if (this.prefs.remote === "all") return items;
    const isStickers = this.prefs.remote === "stickers";
    return items.filter((item) => (remoteKind(item) === "sticker") === isStickers);
  }

  get selectedId(): string | null {
    return this.selected && this.selected.source !== "drop" ? this.selected.asset.id : null;
  }
}
