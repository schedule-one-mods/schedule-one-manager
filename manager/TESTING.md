# Validation

## Maintainer PR review

`manager/build.ps1 -Maintainer` creates a separate local executable and runs offline checks for PR head/fork resolution, commit-pinned API/raw fallback, isolated profiles, version-override boundaries, exact launch combinations, cancellation/failure recovery and changed-file restoration failures. WPF checks cover the maintainer identity, version override, enable and launch behavior. Public builds explicitly disable the feature and assert that the maintainer core types and desktop entry points are absent. See [MAINTAINER.md](MAINTAINER.md) for usage and launch-observation limitations.

The live PR fetch/download check against registry PR #1 at `8300cdb330d33eb0c73d09d8d748ee9832f6aaf1` fetched that commit's index (SHA-256 `751bcab52744887e2fc15e948a6cf07e17414ecaaa9025913ef84f328252850f`) and downloaded/verified Businesses 0.1.3 and DealLocations 0.1.3 from their registered release URLs. No game was launched or modified. Actual process observation on a real game remains an acceptance check; disposable helper processes cover actual observation, early/late exits, log freshness, error markers, cancellation and owned-process cleanup.

## Automated checks for 0.1.3

`manager/build.ps1 -Installer` runs 96 offline checks using a temporary PE fixture and fake HTTP responses. No game, Businesses source or real network access is required in CI. Checks cover local package import, ZIP traversal, compatibility, enable/disable/adoption, file ownership, changed DLL conflicts and launch validation, plus registry parsing, unsupported schema, duplicate identities, repository/tag URL binding, HTTP failures, API fallback, insecure redirects, index size limits, download SHA-256/size, package identity/compatibility mismatches, cancellation and temporary-file cleanup.

`manager/verify-release.ps1` verifies versioned release asset filenames and SHA-256 files, reads all ZIP entries, and enforces an exact manager-only file allowlist. No mod or game binaries are bundled.

## Requirements setup tests

Offline fixtures cover fresh installation, missing-file repair, unchanged/foreign file protection, preserving newer Visual C++ versions through detection, x64 versus x86 runtime checks, missing Desktop/Core/host components, dotnet patch requirements, dependency cache reuse/corruption, download checksum and size errors, cancellation and cleanup, HTTPS downgrade rejection, installer exit failures, success without a detected installation, restart/cancel outcomes, native log paths, extraction rollback and retry. Microsoft installers are replaced with test callbacks in CI; no machine dependencies are installed by unit checks.

```powershell
dotnet run --project manager/tests/Manager.Checks -c Release -- --dependency-smoke
```

This optional test validates official payload checksums and exercises actual MelonLoader fresh installation, all 164 file hashes, and missing-file repair inside a disposable game directory. It uses verified cached payloads when present. The .NET and Visual C++ installers are not executed. The official payload checks and fresh/repair fixture tests passed locally. The .NET payload was downloaded through the new downloader; the slow official MelonLoader/Visual C++ network routes were supplemented with already downloaded, checksum-verified official payloads for completing the fixture test. Neither the actual game installation nor system runtimes were changed.

Pinned Microsoft downloads had valid Authenticode signatures when checked locally. Native Windows installer/elevation and a genuine 3010/reboot cycle still require clean Windows VM acceptance testing. The existing development machine already has both runtimes, so uninstalling shared runtimes here would not be an appropriate clean-machine test.

The 0.1.3 MSI installed locally (exit 0) and its installed EXE matched the published binary. Desktop inspection showed MelonLoader Ready, .NET 6 Desktop x64 Installed, Visual C++ x64 Installed, the setup action disabled for this already healthy installation, Check requirements available, and Play available after verification. This read-only detection did not reinstall system components or change game files.

## Public registry smoke test

```powershell
dotnet run --project manager/tests/Manager.Checks -c Release -- --registry-smoke
```

Fetches the real public index from schedule-one-mods/schedule-one-mod-registry, downloads Businesses 0.1.3 from schedule-one-mods/schedule-one-business-app, verifies its registered bytes and metadata, and exercises enable/disable and launch validation in a temporary installation fixture. It does not use a local Businesses ZIP and does not modify the real game. This passed locally. The original Businesses DLL SHA-256 is `9A528E50CF942EFC7BE68178244C3713077113ACA5B47B403266DB5CB0CD0898`.

The machine's raw.githubusercontent.com route timed out during desktop testing. The client now uses GitHub Contents API first with the raw route as a fallback. A live remote fetch using Contents API passed. Installed mods remain available if discovery fails.

## Optional real installation round trip

```powershell
dotnet run --project manager/tests/Manager.Checks -c Release -- --registry-real-smoke 'Q:\Games\Schedule I' '<separate library folder>'
```

This explicit test downloads Businesses from the real registry, requires the real game to be stopped and its identical Businesses DLL to be already managed, disables that copy, and restores it enabled with the same hash. It refuses conflicting or unmanaged installations and never launches the game itself. Normal builds never run this mutating test.

## Earlier game validation

The 0.1.2 desktop MSI installed successfully. The app fetched the published index, listed Businesses, downloaded it into a new empty library using its Download button, verified and imported it, and displayed it in Installed mods. The explicit real installation test disabled and restored that downloaded DLL with the original checksum. Play then launched the selected game through the desktop manager.

The original manager was installed with Windows Installer (exit 0), detected Schedule I 0.4.7f5 / IL2CPP with MelonLoader 0.7.3, adopted Businesses 0.1.3 and launched the game with it. Loader logs confirmed the expected checksum and running-game mutation guards passed. Loader extraction was tested in a disposable folder using the pinned official archive.

## Remaining testing

A clean Windows VM still needs acceptance testing for real Microsoft runtime installation/elevation, reboot handling and first interop generation. Co-op, long gameplay sessions, other game builds, public code signing and automated registry PR review are unverified. Registry maintainers review submissions manually.

## Local installer launch fix (unreleased)

The first real missing-runtime test found that ShellExecute with the runas verb failed with Windows error 3 before the verified .NET installer started. Microsoft installers now launch directly from their download folder and handle elevation internally. Two regression checks cover this launch configuration and argument boundaries. All 91 checks and MSI packaging passed; the local MSI installed with exit 0 and its executable matched the published binary. The fixed installation flow still needs the user's retry; dependencies remain missing for that test. No public release has been updated.

## Setup log viewer and native log failure (unreleased)

The real runtime installation initially returned 1622 after elevation. Its native MSI log confirms a log-opening failure, but a subsequent application restart and retry successfully installed all .NET 6 components with the previous log destination. The intermittent root cause is unresolved; this is not attributed to path length or permissions without evidence. Native log filenames have been shortened defensively and exit 1622 now has a specific message. Setup and loader logs open in a built-in read-only viewer instead of shell file associations. Setup log selection excludes native child logs, related installer logs are selectable, and Refresh reads active logs with shared access. All 96 offline checks passed. An isolated WPF smoke check instantiated the viewer and verified loading the real failed setup log and switching to a real native installer log. MSI packaging and checksum verification passed. The new package is not installed while the user's existing MelonLoader download is running.

## Mod controls acceptance preparation (unreleased)

The manager now opens Installed mods on startup when the library contains packages, shows enabled counts for the next launch, and identifies the mod and resulting state after a toggle. Existing Enable/Disable controls and running-game guards are retained. All 96 checks and release packaging passed. On the selected real game, MelonLoader files were initially absent despite the historical successful setup log; the existing setup service restored them from the checksum-verified cache. Businesses was disabled (deployed DLL absent, library preserved, launch validation passed), then enabled with its original checksum and launch validation passed. The MSI updated successfully (exit 0), its installed EXE matched the build, and the manager reopened. Businesses is left enabled for the user's enabled/disabled gameplay launch tests. No game was launched by this validation. Changes remain local and uncommitted.

## Three-page UI (0.1.5 development)

The working 0.1.4 checkpoint is commit 64e9a7a. The UI revision separates My mods, Browse mods with search/download, and Settings with folders, requirements and diagnostics. Global banners are independent of page navigation and activity messages. Eleven isolated WPF desktop checks cover page navigation, missing-game/runtime/library/loader notices, restart requirements, verification failures, launch blocking during checks, and clearing a resolved blocker. These checks do not show a window, load user settings, fetch the registry or modify real game files. They run in build.ps1 alongside the 96 core checks. Native desktop inspection confirmed the three pages, real Businesses enabled state, successful registry listing, and requirement status. The revised UI remains uncommitted.

The final 0.1.5 MSI installed locally with exit 0; the installed executable matches the packaged build and was reopened for review. All 107 checks (96 core plus 11 desktop) and package verification passed. Search and its empty state were also exercised through the native desktop UI. No remote push or UI commit was performed.

## Disabled mod compatibility explanation (0.1.5 development)

Investigation of the running manager found Steam game 0.4.7f12 and Businesses metadata tested only for 0.4.7f5. Enable was deliberately disabled by compatibility checks. Added a persistent notice for incompatible disabled mods, a Blocked action with a disabled-control tooltip, and explicit Play without mods wording when the downloaded library has no enabled mods. Six additional desktop checks cover blocked reasons, the notice, launch wording, clearing it for a supported game, and actual fixture enable/disable UI state changes. All 113 checks (96 core plus 17 desktop), MSI packaging and checksum validation passed. Installed the local MSI with exit 0 and verified its binary hash. Native inspection confirmed the compatibility notice for the selected Steam folder and library. No compatibility metadata was expanded and no game was launched. Changes remain uncommitted.

## Registry v2 and downloaded versions

Offline core checks cover grouped release histories, publisher-selected Latest independent of sort order, explicit prereleases, per-tag compatibility, pagination, invalid-release isolation, strict schema fields, asset binding/digest verification and failed-import cleanup. Deletion checks use isolated libraries and game fixtures: one version/all versions, outside-library rejection, changed managed deployment preservation, preflight before mutation, managed disable-before-delete and one-enabled-version enforcement even when DLL names differ.

WPF checks cover one row per mod, version-specific actions, a separate whole-mod target, Latest selection/label, older-version selection and selection preservation after redraw. Both public and maintainer editions are checked. Visual previews can be rendered without loading real settings:

```powershell
./tools/dotnet/dotnet.exe run --project manager/tests/Manager.Desktop.Checks -c Release -p:MaintainerBuild=false -- --version-preview "D:/Projects/Schedule 1 Game/artifacts/manager/versioning-preview"
```

The live `--registry-v2-smoke <local index path>` command resolves the candidate directory against GitHub and verifies every discovered package in a temporary library. It does not launch the game or change real settings/deployments. The six current releases (Businesses 0.1.3/0.1.4, Deal Locations 0.1.3/0.1.4, Special Mixing Terminal 0.5.2/0.5.3) passed this check during implementation. No game compatibility claims beyond their publisher metadata are inferred by this smoke test.
