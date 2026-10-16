# Schedule I Mod Manager — prototype 0.1.0

A Windows x64 desktop manager for local Schedule I mods. C# / .NET 10 / WPF; WiX creates the per-user MSI. The manager includes its own .NET runtime. No account, server, mod manager or storefront integration is needed. It launches the executable in the selected installation folder.

## Use

Install `ScheduleIModManager-0.1.0-x64.msi`, then open **Schedule I Mod Manager** from Start. Alternatively extract the entire portable ZIP and run `ScheduleIModManager.exe`.

1. Choose the folder containing `Schedule I.exe` and `Schedule I_Data`.
2. Choose a separate mod library folder. Save & detect reads the game version, backend, loader and runtime status.
3. Businesses 0.1.3 is imported into the library on first launch. Import additional compatible local package ZIPs with **Import mod ZIP**.
4. If needed, install MelonLoader 0.7.3 and the .NET 6 Desktop Runtime x64 using their buttons. Downloads come from the official release hosts and must match pinned checksums. Windows may request elevation for the runtime installer. Existing loaders are detected and never overwritten.
5. **Enable** copies the package DLL into the game's `Mods` folder. **Disable** removes only the recorded, unchanged copy. **Adopt** takes ownership of an identical DLL already installed manually. Close the game before changing mods.
6. **Play Schedule I** rechecks managed mod compatibility and starts the selected executable. **Open loader log** opens `MelonLoader/Latest.log` for troubleshooting.

This prototype supports single-DLL packages with `manifest.json`, `compatibility.json`, and `Mods/YourMod.dll`. It blocks unverified game/backend/loader combinations when enabling and launching managed mods. Other installed mods are not validated or modified. Publisher metadata records testing; it cannot guarantee a mod will never crash. Normal gameplay testing remains a release requirement.

## Files and ownership

- App/settings: `%LOCALAPPDATA%/ScheduleIModManager/settings.json`.
- Default library: `%LOCALAPPDATA%/ScheduleIModManager/Library`.
- Download cache: `%LOCALAPPDATA%/ScheduleIModManager/Downloads`.
- Managed DLLs: `<game>/Mods`; ownership receipts: `<game>/UserData/ScheduleIModManager/deployments.json`.
- Loader installation inventory: `<game>/UserData/ScheduleIModManager/loader-install.json` when this manager installs the loader.
- Game saves and original game assemblies are untouched. Uninstalling the desktop app preserves the library, settings and game mods. Disable managed mods before uninstalling if you want them removed.

The installer installs the manager only. Game folder selection and dependency provisioning happen in the app. The game directory must be writable by the current user. Symlink/junction libraries are rejected. Loader upgrades, loader removal/repair, automatic mod updates, multiple profiles, co-op compatibility, and a public catalog are outside this first prototype.

## Build

Install the .NET 10 SDK on Windows. Rebuilding the existing Businesses mod also requires its pinned .NET SDK 8.0.426; keeping its original compiler prevents a manager build from changing the already tested mod binary. Both SDKs are already installed locally for this workspace. From the repository root:

```powershell
.\manager\build.ps1 -GameDir 'Q:\Games\Schedule I' -Installer
.\manager\run.ps1
```

The script uses `tools/dotnet/dotnet.exe` if present, otherwise the SDK on PATH. It builds/packages Businesses using existing game interop references, imports the package into the application seed folder, runs filesystem and compatibility checks, publishes a self-contained x64 application, then creates a portable ZIP, MSI and SHA-256 files under `artifacts/manager/releases`. WiX 6.0.2 is installed locally under ignored `tools/wix`. `-SkipModBuild` reuses an existing Businesses release ZIP. The current loader archive at `tools/MelonLoader.x64.0.7.3.zip` is used only for the disposable dependency installation test; no loader archive or game code is bundled with the manager.

For clean builds without that archive, download the pinned official archive from `https://github.com/LavaGang/MelonLoader/releases/download/v0.7.3/MelonLoader.x64.zip` to that path. SHA-256: `5B2B2F3D1CD42B59EC886C5BDC2663EDAE87A0097A4F4A8F58C0965A99DDA416`.

## Compatibility and releases

The current test target is Schedule I **0.4.7f5**, **IL2CPP**, Windows x64, **MelonLoader 0.7.3**, Businesses **0.1.3**. The manager reads the game version from Unity's installed `globalgamemanagers`, not the Unity engine's executable version. Ambiguous/missing versions become Unknown and fail compatibility checks.

To release a mod for a new game build, test gameplay, app navigation, save/load, transitions and co-op as applicable; update the mod's version and `packaging/compatibility.json`; rebuild the package. Do not claim support based only on compilation. The manager reads the package's explicit tested versions; no source change is required for future compatible packages using the same schema. Loader provisioning remains pinned to the first tested game build until separately validated and updated in code.

Manager and mod versions are independent. Before distributing public binaries, sign the EXE/MSI with your own code-signing certificate; these development bundles are unsigned. Publish release ZIP/MSI/checksums later through GitHub Releases. No remote repository is configured by this setup.
