# VRCInventoryManager

VRChat creators use the workbench to find local media, check transparency and motion, and manage the stickers and emojis on their account.

The workbench is dark, compact, and tactile. It borrows the browser, viewer, and inspector arrangement from DaVinci Resolve’s media workflow, with a transparency grid from image editors. The asset gets the largest surface and the strongest contrast. The library stays beside it, while uploads and remote inventory remain within reach.

## Tokens

| Role                                    | Value                                                                     |
| --------------------------------------- | ------------------------------------------------------------------------- |
| Canvas, panel, raised, active           | `#0b0e13`, `#11161f`, `#171d28`, `#1e2634`                                |
| Text, secondary text, hairline, top rim | `#edf3f7`, `#a5b2c2`, `#2b3545`, white at 6%                              |
| Selection and primary action            | Teal `#22d3be`                                                            |
| Connected, warning, destructive         | Emerald `#61d6a2`, amber `#f5a623`, coral `#f45b69`                       |
| Headings, body, numbers                 | Bahnschrift, Segoe UI Variable Text, Cascadia Mono, with system fallbacks |
| Type                                    | 12, 13, 14, 16, and 20 px, at weights 400 and 600                         |
| Spacing                                 | 4, 8, 12, 16, 24, 32, and 48 px                                           |
| Header and compact controls             | 52 px and 32 px                                                           |
| Corners                                 | 4 px for controls, 8 px for surfaces, 12 px for dialogs                   |

The palette, type scale, and control dimensions live in the stylesheet. Native fields, buttons, and dialogs use a custom 16 px stroke icon set. The app mark and empty-state drawings are SVG. The Windows font stacks require no network or font downloads.

## Workbench

- The studio header shows the current VRChat account, two quotas, reconnection, and settings.
- Local assets keeps the source, search, folder tree, and file grid in one pane. Files can be inspected by dropping them on this pane. Files found in the local library can also be uploaded.
- The light table shows the selected file with a choice of transparency backgrounds. The transport handles frame preview. The inspector shows file details and upload controls below it.
- VRChat inventory keeps filters, inspection, and deletion together. Delete opens a confirmation with a thumbnail and starts with focus on Cancel.
- Settings opens the current local root, connection, version, and update preference. The update dialog offers the release notes and a choice of install, skip, or later.

At 1024 × 640, the two side panes narrow and toolbar icons recover label space where needed. At 1360 × 800 the local and remote panes are about 380 and 340 px. At 1920 × 1080 they cap at 440 and 380 px. The viewer takes the extra width. Each pane has one scrolling region.

## Interaction details

- The backend remembers the library root, recursive scan setting, and update preference. The workbench remembers the backdrop, folder, and inventory filter when storage is available.
- The first file opens on startup when the library has one. The local grid loads 48 files at a time. The folder tree starts collapsed in the compact window.
- Arrow keys browse visible items. Home and End jump within the active list. Ctrl+F and / open local search. Fields and dialogs keep their own keyboard controls.
- Escape clears the preview when no dialog is open and focus is outside a field.
- Focus uses a 2 px teal outline. Hover lightens the surface, press darkens it, and disabled actions explain the reason beside the upload controls.
- The viewer keeps controls off the image. Switching backgrounds changes the surface behind the asset. Play and pause keep the current frame. Ping-pong visits each endpoint once per turn.
- Numeric readouts use tabular figures. Sizes, dimensions, and counts keep their units together. Dates use a short localized form and include the full date on hover.
- File names and paths stay on one line with the full text on hover. Folder paths keep their root and last segment visible where space allows.
- Settings apply as soon as they change. Escape closes dialogs and native focus handling returns focus to the opener.
- A hairline and an in-place working label show pending operations. Toasts report uploads and connection changes, and preserve errors until dismissed.
- Selection uses a teal edge. Amber marks animated media and near-capacity quotas. Coral marks destructive actions and full quotas. The transparency grid stays neutral on both stage and thumbnails.
- In previews, the compact, default, and wide windows are checked with long paths, full lists, empty lists, errors, animation, dialog focus, and quotas at capacity.
