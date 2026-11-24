# VRCInventoryManager

A Windows desktop app for browsing your local VRChat screenshots and managing the stickers and emoji you've uploaded.

[Download](https://github.com/RealWhyKnot/VRCInventoryManager/releases/latest) · [Changelog](CHANGELOG.md) · [Report a bug](https://github.com/RealWhyKnot/VRCInventoryManager/issues/new?template=bug_report.yml) · [Security](.github/SECURITY.md)

## What it does

- Screenshots: it opens your VRChat picture folder. That's VRCX's `picture_output_folder` setting if there is one, and your Pictures folder if not. Subfolders are included, VRCX's month folders too, and animated GIFs play in the preview.
- Stickers and emoji: with VRCX signed in, it can list, preview, upload and delete the ones on your VRChat account.

## Needs VRCX

The sticker and emoji features need [VRCX](https://github.com/vrcx-team/VRCX) installed and signed in to VRChat. The app reads the login cookie VRCX already saved on your PC. It never asks for your VRChat password and doesn't store one. Without VRCX you can still browse screenshots.

## Updates

When it starts, the app checks the [releases page](https://github.com/RealWhyKnot/VRCInventoryManager/releases) for a newer version. If there is one, it asks before updating and waits until nothing is uploading. If you installed it with the setup program, it runs the new setup. If you unzipped it, it replaces the exe with the new one. Either way the app reopens when it's done.

To turn the check off, set `"UpdateCheck": false` in `%AppData%\VRCInventoryManager\settings.json`.

## Logs

Each run writes `VRCInventoryManager.debug.log` next to `VRCInventoryManager.exe`.

## Build

You need the .NET SDK 10.0.404 (pinned in `global.json`, with roll-forward disabled) on 64-bit Windows. [NSIS](https://nsis.sourceforge.io) is optional and only needed for the installer.

```powershell
.\build.ps1
```

For a release build:

```powershell
.\build.ps1 -Release
```

That makes a compressed, self-contained win-x64 zip with a single exe under `release\`. If NSIS is installed you also get a per-user installer.

## Bug reports and changes

Open an [issue](https://github.com/RealWhyKnot/VRCInventoryManager/issues/new/choose) for bugs and ideas. Please don't send a pull request without talking to me first. [CONTRIBUTING.md](CONTRIBUTING.md) explains why.

## License

GNU General Public License v3.0. See [LICENSE](LICENSE).
