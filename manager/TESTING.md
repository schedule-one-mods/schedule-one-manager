# Prototype validation

Tested locally on Windows x64 against Schedule I **0.4.7f5 / IL2CPP**, MelonLoader **0.7.3**, Businesses **0.1.3**.

## Automated checks

`manager/build.ps1` runs `Manager.Checks` against disposable directories, the actual Businesses ZIP and the actual pinned loader ZIP. Checks cover import, repeat import, ZIP traversal rejection and staging cleanup, separate folder enforcement, version/backend/loader/runtime compatibility, enable/disable/adoption, ownership and checksum conflicts, launch validation, and loader extraction without overwriting existing files. Real game detection is read only. When the game is running, two additional checks verify that enable/disable calls refuse to modify it (30 total).

For an optional real installation round trip, first adopt Businesses in the app and close the game, then run:

```powershell
.\tools\dotnet\dotnet.exe run --project .\manager\tests\Manager.Checks -c Release -- .\artifacts\releases\Businesses-0.1.3-IL2CPP.zip 'Q:\Games\Schedule I' .\tools\MelonLoader.x64.0.7.3.zip --exercise-real
```

That explicitly disables and re-enables the already managed identical Businesses DLL. Its final checksum must match the library. The default build never runs this mutating test.

## Desktop validation performed

- Built the self-contained Windows x64 EXE, portable ZIP and MSI.
- Installed the per-user MSI with Windows Installer (exit 0), launched its installed EXE and checked that it matched the published binary.
- First-run import populated the library with Businesses 0.1.3.
- Auto-detection found another Steam installation, 0.4.7f12, and marked it unverified for this mod.
- Changed the game folder to Q:/Games/Schedule I, saved it, and detected 0.4.7f5, IL2CPP, MelonLoader 0.7.3 and the existing .NET 6 Desktop Runtime.
- Adopted the identical manually installed Businesses DLL using the desktop button.
- Clicked Play in the manager; the selected game executable started, and the new MelonLoader log reported Businesses v0.1.3 with SHA-256 `9A528E50CF942EFC7BE68178244C3713077113ACA5B47B403266DB5CB0CD0898`.
- Confirmed the game reached gameplay and manager launch/mutation guards detected its running process.

## Remaining release testing

The machine already had the game dependencies installed. Actual loader ZIP provisioning was tested in a disposable game folder; the runtime installer download/elevation workflow and a completely fresh game's first interop-generation launch still need a clean Windows test. Co-op, long gameplay sessions, public code signing, and other game builds are unverified. This prototype has no GitHub catalog or automatic update client yet.
