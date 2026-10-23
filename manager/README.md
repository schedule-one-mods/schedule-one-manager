# Schedule I Mod Manager — prototype 0.1.5

A Windows x64 desktop manager for local Schedule I mods. C# / .NET 10 / WPF; WiX creates the per-user MSI. The manager includes its own .NET runtime and ships without any mods or game files.

## Use

Install `ScheduleIModManager-0.1.5-x64.msi`, then open **Schedule I Mod Manager** from Start. Alternatively extract the portable ZIP and run `ScheduleIModManager.exe`.

1. In **Settings**, choose the folder containing `Schedule I.exe` and `Schedule I_Data`.

2. Choose a separate mod library folder. **Save and check** reads the game version, backend, loader and runtime status.

3. Open **Browse mods** to read the [community registry](https://github.com/schedule-one-mods/schedule-one-mod-registry). Select a version (the publisher's **Latest** is selected initially), then click **Download**. Each version shows its tested game and loader versions. The manager verifies its size, SHA-256, package identity and compatibility metadata before importing it into the local library. A fresh library starts empty; existing libraries are preserved during upgrades.

4. In **Settings**, click **Check again**. **Install missing requirements** downloads and installs Visual C++ x64, .NET 6 Desktop x64, then MelonLoader 0.7.3 as needed. Official downloads must match pinned sizes/checksums. Windows asks for administrator approval for Microsoft runtimes. Setup verifies each installation, shows progress, supports cancelling downloads, and keeps verified downloads for retries. If Windows requires a restart, restart and run setup again to finish remaining steps. There is no automatic reboot.

5. In **My mods**, **Enable** copies a downloaded package DLL into the game's `Mods` folder. **Disable** removes only the recorded, unchanged copy. **Adopt** manages an identical manually installed DLL. Close the game before changing mods. Downloads never enable a mod automatically.

6. **Play Schedule I** checks managed mod compatibility and starts the selected executable. **Open loader log** opens `MelonLoader/Latest.log`.

Blocking problems appear in a persistent banner across all three pages. Missing game folders, missing or damaged dependencies, restart requirements, library failures, and incompatible enabled mods keep Play disabled until resolved. Registry failures appear separately and leave downloaded mods usable. Settings contains the built-in setup and loader log viewers. Browse supports searching by mod name or author.

Supported packages contain `manifest.json`, `compatibility.json`, and one `Mods/YourMod.dll`. The manager blocks unverified game/backend/loader combinations for managed mods. Other installed mods are not validated or modified. Publisher compatibility metadata records testing; actual gameplay testing remains necessary.

## Files and ownership

- Settings: `%LOCALAPPDATA%/ScheduleIModManager/settings.json`.

- Default library: `%LOCALAPPDATA%/ScheduleIModManager/Library`.

- Download cache: `%LOCALAPPDATA%/ScheduleIModManager/Downloads`.

- Managed DLLs: `<game>/Mods`; receipts: `<game>/UserData/ScheduleIModManager/deployments.json`.

- Loader inventory: `<game>/UserData/ScheduleIModManager/loader-install.json` when installed through this manager.

The app installer preserves user settings, library and game mods during upgrades/uninstall. Disable mods before uninstalling if you want their managed copies removed. The game directory must be writable. Setup can restore missing files in a verified MelonLoader 0.7.3 installation. Changed/foreign loader files and other loader versions are preserved and block automatic setup; use the original loader installer to handle those conflicts. Junction/symlink libraries, loader replacement/removal, automatic updates and profiles are outside this prototype. Registry errors do not prevent using downloaded mods offline; refresh explicitly to retry discovery.

## Build

On Windows x64, install PowerShell 7 and .NET 10 SDK. From the repository root:

```powershell

.\manager\build.ps1 -Installer

.\manager\run.ps1

```

No game installation, .NET 8 SDK, mod DLL or loader archive is needed. The script runs self-contained fixture checks, publishes the application, verifies that packages contain only manager files, and writes the portable ZIP, MSI and SHA-256 files under `artifacts/manager/releases/0.1.5`. WiX 6.0.2 installs locally under ignored `tools/wix`. Without `-Installer`, only the portable ZIP is built. A local SDK under `tools/dotnet` is used when available; GitHub Actions uses its configured SDK on PATH.

`manager/Directory.Build.props` is the single version source for the application, title label, MSI, package filenames and GitHub Release tag. [Release instructions](RELEASING.md) explain automatic publication after pushing to `main`.

## Compatibility

The existing integration test target is Schedule I **0.4.7f5 / IL2CPP**, Windows x64, **MelonLoader 0.7.3**. Game versions are read from Unity's installed `globalgamemanagers`; ambiguous/missing versions are Unknown and fail compatibility checks. Mod package metadata provides its tested versions. Loader provisioning remains pinned to that game build until separately tested and updated.

Mod authors register once in the schema-v2 directory and publish tagged GitHub Releases independently. The registry stores stable identities and a `release_source` policy, including `latest: github_latest`. Browse reads published releases directly and their compatibility JSON at each version tag; GitHub supplies package size and digest. Refresh discovers new versions without a registry PR or GitHub Actions job. Publisher compatibility is checked against package contents on download. Latest does not mean compatible. Prereleases remain explicit choices; malformed releases show an error. Downloaded mods remain available offline.

**My mods** shows one row per mod with a downloaded-version selector. Keep several versions locally, select the enabled one to disable it, then enable another. Only one version per package identity may be enabled, including versions with different DLL filenames. **Delete version** removes the selected local version; **Delete whole mod** removes every downloaded version. Managed enabled copies are disabled first. Changed managed files block deletion until resolved. Browse continues listing registered mods after deletion. Future packs can pin stable identities, versions and digests; packs are not implemented by this change.

The v2 index and manager must be released together. Schema v1 is rejected; there is no legacy schema reader. Registration delegates future release publication to the registered repository owner; future versions are not separately reviewed by the registry.

## Live registry check

Local maintainers can build separate PR review tooling with `manager/build.ps1 -Maintainer`; the normal public build excludes its code and controls. Full instructions are in `manager/MAINTAINER.md` in the source repository.

```powershell
dotnet run --project manager/tests/Manager.Checks -c Release -- --registry-smoke
```

This fetches the real public index, downloads Businesses from its public release, verifies it, and exercises enable/disable/launch validation in an isolated installation fixture. It never uses a local Businesses ZIP or modifies your real game. Normal automated checks use fake HTTP responses so builds do not depend on live registry availability.

## Requirements setup

Automatic setup supports Windows x64 IL2CPP installations. Dependency setup is separate from a mod's tested game-version compatibility; installing MelonLoader does not make an unverified mod compatible. A writable game folder is required. Play remains unavailable until the requirements pass verification. Compatible mods can then be enabled.

- MelonLoader is pinned to the official 0.7.3 x64 archive. All 164 shipped files are checked against the embedded `LoaderFiles.json` hash catalog. Missing files can be restored; changed files are not overwritten. Extraction failures roll back new files and their empty directories. Mods, saves, game assemblies, generated interop files and unrelated loader files are preserved.
- .NET detection requires a global x64 host, .NET 6 Desktop/Core and hostfxr components, at least 6.0.36. A .NET 8/10 installation or a private manager runtime does not satisfy MelonLoader's .NET 6 requirement.
- Visual C++ detection checks the installed x64 v14 runtime in both Windows registry views. Existing 14.29.30139 or later versions are accepted; missing systems receive the pinned Microsoft 14.44.35211 installer. Newer installed versions are not downgraded.
- Downloads have exact size and SHA-256/SHA-512 checks, HTTPS enforcement, connection/read/overall timeouts, and a verified cache under `%LOCALAPPDATA%/ScheduleIModManager/Downloads`. No installers are bundled or requested by arbitrary mod metadata.
- Microsoft installers run with standard Windows elevation and `/install /passive /norestart`. Once an installer starts, complete or cancel it in Windows; the app does not kill a system installation. Codes 0, 3010/1641, 1223/1602 and other failures are handled separately. Restart notices survive closing the manager until Windows restarts.
- **Open setup log** opens the step log. Setup and native Microsoft installer logs are saved under `%LOCALAPPDATA%/ScheduleIModManager/Logs`.

The manager includes its own .NET 10 runtime. The prerequisites above belong to the game loader, not to the manager. The pinned files can be updated independently after compatibility testing.

```powershell
dotnet run --project manager/tests/Manager.Checks -c Release -- --dependency-smoke
```

This optional check verifies official payloads and exercises fresh MelonLoader installation/missing-file repair in a disposable game directory. It uses verified cached downloads when available and never runs Microsoft installers or modifies your real game/system runtimes. Native installation/elevation/reboot behavior still needs a clean Windows VM acceptance test.

Sources: [MelonLoader requirements](https://github.com/LavaGang/MelonWiki/blob/master/docs/gettingstarted.md), [official 0.7.3 release](https://github.com/LavaGang/MelonLoader/releases/tag/v0.7.3), [Microsoft installer options and exit codes](https://learn.microsoft.com/en-us/dotnet/core/install/windows#command-line-options).

Before the v2 registry is published, test its local candidate against real public releases without changing any game files:

```powershell
./tools/dotnet/dotnet.exe run --project manager/tests/Manager.Checks -c Release -- --registry-v2-smoke D:/Projects/schedule-one-mod-registry/index.json
```

This discovers all registered release histories and verifies every discovered package in a temporary library, including Businesses 0.1.3 and 0.1.4.
