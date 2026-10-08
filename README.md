# VRCInventoryManager

Windows desktop app for browsing your local VRChat screenshots and managing the stickers and emoji you've uploaded.

It opens your VRChat picture folder. That's VRCX's `picture_output_folder` setting if there is one, and your Pictures folder if not. Subfolders are included, VRCX's month folders too, and animated GIFs play in the preview.

With VRCX signed in, it can also list, preview, upload and delete your stickers and emoji on VRChat. It uses the cookie store VRCX already has and never asks for your VRChat password.

Each run writes `VRCInventoryManager.debug.log` next to `VRCInventoryManager.exe`.

At startup it checks the releases page for a newer version and asks before updating, once nothing is uploading. An installed copy updates by running the new setup, and a zip copy swaps in the new exe. Both reopen afterwards. To stop the check, set `"UpdateCheck": false` in `%AppData%\VRCInventoryManager\settings.json`.

## Build

```powershell
.\build.ps1
```

For a release build:

```powershell
.\build.ps1 -Release
```

That makes a compressed, self-contained win-x64 zip with a single exe under `release\`. If NSIS is installed you also get a per-user installer.

## License

GNU General Public License v3.0. See [LICENSE](LICENSE).
