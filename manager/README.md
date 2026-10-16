# Schedule I Mod Manager — prototype 0.1.1

A Windows x64 desktop manager for local Schedule I mods. C# / .NET 10 / WPF; WiX creates the per-user MSI. The manager includes its own .NET runtime and ships without any mods or game files.

## Use

Install `ScheduleIModManager-0.1.1-x64.msi`, then open **Schedule I Mod Manager** from Start. Alternatively extract the portable ZIP and run `ScheduleIModManager.exe`.

1. Choose the folder containing `Schedule I.exe` and `Schedule I_Data`.
2. Choose a separate mod library folder. **Save & detect** reads the game version, backend, loader and runtime status.
3. Import compatible local mod ZIPs using **Import mod ZIP**. A fresh library starts empty; existing libraries are preserved during upgrades.
4. If needed, install MelonLoader 0.7.3 and .NET 6 Desktop Runtime x64 using their buttons. Official downloads must match pinned checksums. Windows may request elevation for the runtime installer. Existing loaders are never overwritten.
5. **Enable** copies a package DLL into the game's `Mods` folder. **Disable** removes only the recorded, unchanged copy. **Adopt** manages an identical manually installed DLL. Close the game before changing mods.
6. **Play Schedule I** checks managed mod compatibility and starts the selected executable. **Open loader log** opens `MelonLoader/Latest.log`.

Supported packages contain `manifest.json`, `compatibility.json`, and one `Mods/YourMod.dll`. The manager blocks unverified game/backend/loader combinations for managed mods. Other installed mods are not validated or modified. Publisher compatibility metadata records testing; actual gameplay testing remains necessary.

## Files and ownership

- Settings: `%LOCALAPPDATA%/ScheduleIModManager/settings.json`.
- Default library: `%LOCALAPPDATA%/ScheduleIModManager/Library`.
- Download cache: `%LOCALAPPDATA%/ScheduleIModManager/Downloads`.
- Managed DLLs: `<game>/Mods`; receipts: `<game>/UserData/ScheduleIModManager/deployments.json`.
- Loader inventory: `<game>/UserData/ScheduleIModManager/loader-install.json` when installed through this manager.

The app installer preserves user settings, library and game mods during upgrades/uninstall. Disable mods before uninstalling if you want their managed copies removed. The game directory must be writable. Junction/symlink libraries, loader repair/removal, automatic mod updates, profiles and a public catalog are outside this prototype.

## Build

On Windows x64, install PowerShell 7 and .NET 10 SDK. From the repository root:

```powershell
.\manager\build.ps1 -Installer
.\manager\run.ps1
```

No game installation, .NET 8 SDK, mod DLL or loader archive is needed. The script runs self-contained fixture checks, publishes the application, verifies that packages contain only manager files, and writes the portable ZIP, MSI and SHA-256 files under `artifacts/manager/releases/0.1.1`. WiX 6.0.2 installs locally under ignored `tools/wix`. Without `-Installer`, only the portable ZIP is built. A local SDK under `tools/dotnet` is used when available; GitHub Actions uses its configured SDK on PATH.

`manager/Directory.Build.props` is the single version source for the application, title label, MSI, package filenames and GitHub Release tag. [Release instructions](RELEASING.md) explain automatic publication after pushing to `main`.

## Compatibility

The existing integration test target is Schedule I **0.4.7f5 / IL2CPP**, Windows x64, **MelonLoader 0.7.3**. Game versions are read from Unity's installed `globalgamemanagers`; ambiguous/missing versions are Unknown and fail compatibility checks. Mod package metadata provides its tested versions. Loader provisioning remains pinned to that game build until separately tested and updated.

Mod authors publish their own versioned packages and maintain compatibility metadata independently of manager versions. No repository scanning or community registry is implemented yet. Public development packages are unsigned; code signing can be added to the release pipeline later.
