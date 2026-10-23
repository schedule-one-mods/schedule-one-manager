# Schedule I Mod Manager

A Windows x64 desktop manager for local Schedule I mods, built with C# / .NET 10 / WPF and packaged as a per-user MSI and portable ZIP.

The current version is [**0.1.5**](https://github.com/schedule-one-mods/schedule-one-manager/releases/tag/v0.1.5). The manager is distributed independently of mods and does not bundle game files, MelonLoader, or Businesses. It includes a verified requirements setup flow for MelonLoader, .NET 6 Desktop and Visual C++ x64. It reads the [community registry](https://github.com/schedule-one-mods/schedule-one-mod-registry), lists available mods, and downloads verified release packages on demand.

- [Build and usage](manager/README.md)
- [GitHub Releases workflow](manager/RELEASING.md)
- [Validation and remaining testing](manager/TESTING.md)

```powershell
.\manager\build.ps1 -Installer
.\manager\run.ps1
```

Build requirements: Windows x64, PowerShell 7 and .NET 10 SDK. No game installation or mod package is required to build or run the automated checks. The script installs WiX 6.0.2 locally when creating the MSI.

Pushes to `main` build and test the application. A version that has no existing GitHub Release is published automatically as `v<version>`, with MSI, portable ZIP and checksums. Pull requests build artifacts only. Change `manager/Directory.Build.props` to publish the next version.

Businesses is maintained in [its own repository](https://github.com/schedule-one-mods/schedule-one-business-app) and registered independently. Earlier Git history still contains its original source. Existing installed mods and user libraries are preserved. Register new mods through a metadata PR to the registry; the manager does not scan arbitrary repositories.

Registry schema v2 groups each mod into a selectable release history, with per-version tested game/backend/loader metadata and the publisher's GitHub Latest marker. Keep several downloaded versions, enable one at a time, or delete one version or the whole downloaded mod. Authors register once; subsequent tagged GitHub Releases need no registry PR or scanning job.
