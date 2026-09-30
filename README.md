# VRCInventoryManager

A Windows desktop app for browsing your local VRChat screenshots and managing your uploaded stickers and emoji.

It opens the current VRChat picture folder, using the `picture_output_folder` setting from VRCX when there is one and your Pictures folder otherwise. Browsing is recursive and understands VRCX's month folders. Animated GIFs preview in place. Through the existing VRCX cookie store it can list, preview, upload and delete your remote stickers and emoji.

Each run writes `VRCInventoryManager.debug.log` next to `VRCInventoryManager.exe`.

## Build

```powershell
.\build.ps1
```

For a release build:

```powershell
.\build.ps1 -Release
```

That produces a compressed, self-contained, single-exe win-x64 zip under `release\`. If NSIS is installed it also builds a per-user installer.

## License

GNU General Public License v3.0; see [LICENSE](LICENSE).
