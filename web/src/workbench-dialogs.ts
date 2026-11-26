import { nonEmpty } from "./format";
import { logFailure, api, type RemoteItem, type UpdateAction } from "./api";
import { button, byId, element, icon, setContents, thumbnail, type Icon } from "./dom";
import { pathText, remoteKindLabel } from "./library";
import type { Workbench } from "./workbench";
import { type Operation, settingsDialog, deleteDialog, updateDialog, errorText, detail } from "./workbench-shared";

export function openSettings(this: Workbench): void {
  this.operationError = "";
  this.renderSettings();
  this.showDialog(settingsDialog);
}

export function showDialog(this: Workbench, dialog: HTMLDialogElement): void {
  byId("drop-zone").hidden = true;
  dialog.showModal();
}

export function dialogHeading(this: Workbench, id: string, title: string, glyph: Icon, dialog: HTMLDialogElement): HTMLElement {
  return element(
    "div",
    { class: "dialog-head" },
    element("span", { class: "dialog-emblem" }, icon(glyph)),
    element("h1", { id }, title),
    button("Close", "close", () => dialog.close(), {
      iconOnly: true,
      key: "close-dialog",
      blocked: (dialog === updateDialog && this.busy === "update") || (dialog === deleteDialog && this.busy === "delete"),
    })
  );
}

export function renderSettings(this: Workbench): void {
  const state = this.state;
  const isChecked = state?.settings.updateCheck ?? true;
  const update = element("input", {
    id: "update-check",
    type: "checkbox",
    role: "switch",
    checked: isChecked,
    "aria-disabled": this.isBlocked ? "true" : undefined,
    "data-key": "update-check",
    autofocus: true,
  });
  update.checked = isChecked;
  update.addEventListener("click", (event) => {
    if (this.isBlocked) event.preventDefault();
  });
  update.addEventListener("change", () => {
    const isNext = update.checked;
    void this.operate("settings", "Saving settings…", async () => {
      await api.settings({ updateCheck: isNext });
      await this.loadState();
    }).catch(() => logFailure("Background workbench-dialogs operation failed"));
  });
  const root = nonEmpty(state?.localRoot) ?? "No folder selected";
  setContents(
    settingsDialog,
    this.dialogHeading("settings-title", "Settings", "settings", settingsDialog),
    element(
      "div",
      { class: "dialog-body" },
      element(
        "dl",
        { class: "settings-details" },
        detail("Local folder", pathText(root), root),
        detail("VRCX", nonEmpty(state?.vrcxVersion) ?? "Version unavailable"),
        detail("VRChat", this.isConnected ? (nonEmpty(state?.user?.displayName) ?? "Connected") : "Disconnected"),
        detail("App version", nonEmpty(state?.version) ?? "Version unavailable")
      ),
      element(
        "label",
        { class: "settings-preference switch-label", for: "update-check" },
        update,
        element("span", { class: "switch-track", "aria-hidden": "true" }, element("span", { class: "switch-knob" })),
        element("span", {}, "Check for updates on startup")
      ),
      ...this.dialogStatus("settings", "update")
    ),
    element(
      "div",
      { class: "dialog-actions" },
      button(this.working("update", "Check now"), "refresh", () => this.checkForUpdate(), { blocked: this.isBlocked, key: "check-now" }),
      button("Done", null, () => settingsDialog.close(), {
        class: "primary-button",
        key: "done",
      })
    )
  );
}

export function checkForUpdate(this: Workbench): void {
  void this.operate("update", "Checking for updates…", async () => {
    await api.update("check");
    await this.loadState();
    if (this.state?.updateOffer) {
      this.seenOffer = "";
      settingsDialog.close();
    } else this.toast("You’re up to date");
  }).catch(() => logFailure("Background workbench-dialogs operation failed"));
}

export function openDelete(this: Workbench, item: RemoteItem): void {
  this.pendingDelete = item;
  this.operationError = "";
  this.renderDelete();
  this.showDialog(deleteDialog);
}

export function renderDelete(this: Workbench): void {
  const item = this.pendingDelete;
  if (!item) return;
  const cancel = button("Cancel", null, () => deleteDialog.close(), {
    key: "cancel-delete",
    blocked: this.busy === "delete",
  });
  cancel.setAttribute("autofocus", "");
  setContents(
    deleteDialog,
    this.dialogHeading("delete-title", "Delete remote file?", "trash", deleteDialog),
    element(
      "div",
      { class: "dialog-body" },
      element(
        "div",
        { class: "delete-item" },
        thumbnail(item.thumbUrl, "dialog-thumbnail"),
        element(
          "div",
          { class: "delete-name" },
          element("strong", { class: "truncate", title: item.name }, item.name),
          element("span", {}, remoteKindLabel(item))
        )
      ),
      element("p", {}, "Local files stay untouched."),
      ...this.dialogStatus("delete")
    ),
    element(
      "div",
      { class: "dialog-actions" },
      cancel,
      button(
        this.working("delete", "Delete remote file"),
        "trash",
        () => {
          void this.operate("delete", "Deleting…", async () => {
            await api.deleteRemote(item.id);
            deleteDialog.close();
            this.toast("Deleted remote file");
            await this.loadState();
            await this.loadRemote();
            if (!document.activeElement || document.activeElement === document.body) {
              byId("remote-tools").querySelector("button")?.focus();
            }
          }).catch(() => logFailure("Background workbench-dialogs operation failed"));
        },
        {
          class: "danger-primary",
          blocked: this.isBlocked,
          key: "confirm-delete",
        }
      )
    )
  );
}

export function offerUpdate(this: Workbench): void {
  const offer = this.state?.updateOffer;
  if (!offer || this.busy || settingsDialog.open || deleteDialog.open || updateDialog.open || this.seenOffer === offer.tag) return;
  this.seenOffer = offer.tag;
  this.currentOffer = offer;
  this.renderUpdate();
  this.showDialog(updateDialog);
}

export function renderUpdate(this: Workbench): void {
  const offer = this.currentOffer;
  if (!offer) return;
  const later = button("Later", null, () => this.updateAction("later"), {
    key: "update-later",
    blocked: this.isBlocked,
  });
  later.setAttribute("autofocus", "");
  setContents(
    updateDialog,
    this.dialogHeading("update-title", "Update available", "download", updateDialog),
    element(
      "div",
      { class: "dialog-body" },
      element(
        "div",
        { class: "update-release" },
        element("span", { class: "version numeric" }, offer.version),
        offer.isBeta ? element("span", { class: "remote-tag tag-animated" }, "Beta") : null,
        button(
          "Read release notes",
          "external",
          () => {
            void api.openExternal({ url: offer.htmlUrl }).catch((error: unknown) => {
              this.operationError = errorText(error);
              this.renderUpdate();
            });
          },
          { key: "release-notes" }
        )
      ),
      ...this.dialogStatus("update")
    ),
    element(
      "div",
      { class: "dialog-actions" },
      later,
      button("Skip version", null, () => this.updateAction("skip"), {
        key: "update-skip",
        blocked: this.isBlocked,
      }),
      button(this.working("update", "Update now"), "download", () => this.updateAction("install"), {
        class: "primary-button",
        key: "update-install",
        blocked: this.isBlocked,
      })
    )
  );
}

export function updateAction(this: Workbench, action: UpdateAction): void {
  void this.operate("update", action === "install" ? "Updating app…" : "Saving update choice…", async () => {
    await api.update(action);
    updateDialog.close();
    await this.loadState();
  }).catch(() => logFailure("Background workbench-dialogs operation failed"));
}

export function dialogStatus(this: Workbench, ...operations: Operation[]): HTMLElement[] {
  if (this.operationError) return [element("p", { class: "dialog-error", role: "alert" }, this.operationError)];
  if (this.busy && operations.includes(this.busy) && this.isBusyVisible) return [element("p", { class: "dock-note", role: "status" }, this.busyLabel)];
  return [];
}

export function toast(this: Workbench, message: string, type: "success" | "error" = "success"): void {
  const stack = byId("toasts");
  while (stack.children.length >= 3) stack.firstElementChild?.remove();
  const item = element(
    "div",
    {
      class: `toast toast-${type}`,
      role: type === "error" ? "alert" : "status",
    },
    icon(type === "error" ? "warning" : "check"),
    element("span", { title: message }, message),
    button("Dismiss notification", "close", () => item.remove(), {
      iconOnly: true,
    })
  );
  stack.append(item);
  if (type === "success") window.setTimeout(() => item.remove(), 5_000);
}
