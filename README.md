# VRCInventoryManager

VRCInventoryManager is a Windows desktop app for browsing a local VRChat image folder and managing uploaded sticker and emoji files.

The app opens `C:\Users\ADMIN\Pictures\VRChat\Emoji` by default, supports recursive browsing, previews animated GIFs, and can connect through the existing VRCX cookie store to list, upload, and delete remote sticker and emoji files.

## Local Build

```powershell
.\build.ps1
```

## Release Build

```powershell
.\build.ps1 -Release
```

The release build creates a self-contained win-x64 zip under `release\`. If NSIS is installed, it also builds a per-user installer.
