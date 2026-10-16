# Schedule I Mod Manager

A Windows x64 desktop manager for local Schedule I mods, built with C# / .NET 10 / WPF and packaged as a per-user MSI and portable ZIP.

The current version is **0.1.1**. The manager is distributed independently of mods and does not bundle game files, MelonLoader, or Businesses.

- [Build and usage](manager/README.md)
- [GitHub Releases workflow](manager/RELEASING.md)
- [Validation and remaining testing](manager/TESTING.md)

```powershell
.\manager\build.ps1 -Installer
.\manager\run.ps1
```

Build requirements: Windows x64, PowerShell 7 and .NET 10 SDK. No game installation or mod package is required to build or run the automated checks. The script installs WiX 6.0.2 locally when creating the MSI.

Pushes to `main` build and test the application. A version that has no existing GitHub Release is published automatically as `v<version>`, with MSI, portable ZIP and checksums. Pull requests build artifacts only. Change `manager/Directory.Build.props` to publish the next version.

Businesses source has been preserved locally under ignored `local-mod-archive/Businesses` for extraction into a separate repository later. Its files are removed from the current source layout; earlier Git history still contains them. Existing installed mods and user libraries are preserved. Repository discovery and a community catalog will be a separate feature.
