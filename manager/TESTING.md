# Validation

## Automated checks for 0.1.2

`manager/build.ps1 -Installer` runs 44 offline checks using a temporary PE fixture and fake HTTP responses. No game, Businesses source or real network access is required in CI. Checks cover local package import, ZIP traversal, compatibility, enable/disable/adoption, file ownership, changed DLL conflicts and launch validation, plus registry parsing, unsupported schema, duplicate identities, repository/tag URL binding, HTTP failures, API fallback, insecure redirects, index size limits, download SHA-256/size, package identity/compatibility mismatches, cancellation and temporary-file cleanup.

`manager/verify-release.ps1` verifies versioned release asset filenames and SHA-256 files, reads all ZIP entries, and enforces an exact manager-only file allowlist. No mod or game binaries are bundled.

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

A clean machine still needs testing for dependency download/elevation and first interop generation. Co-op, long gameplay sessions, other game builds, public code signing and automated registry PR review are unverified. Registry maintainers review submissions manually.
