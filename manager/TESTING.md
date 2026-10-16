# Validation

## Automated release checks

`manager/build.ps1 -Installer` runs 23 checks using a temporary package assembled from a harmless local test PE file and generated manifest/compatibility metadata. It never loads that file as a mod and needs no installed game, Businesses code, or MelonLoader archive.

Checks cover installed version/backend parsing, package import and duplicate import, ZIP traversal and cleanup, separate library location enforcement, compatibility rejection, enable/disable/adoption, ownership and checksum conflicts, and compatibility validation before launch. All fixture files are removed afterwards.

The release verifier checks the expected versioned asset filenames, SHA-256 checksums, readable portable ZIP contents and an exact manager-only allowlist. Seed mod ZIPs and game files cause verification to fail.

GitHub Actions runs these checks and builds the MSI/ZIP on a hosted Windows runner. The workflow itself cannot be exercised remotely until these uncommitted changes are pushed to a GitHub repository.

## Earlier local integration validation (0.1.0)

The original manager prototype was installed using Windows Installer (exit 0), detected Schedule I 0.4.7f5 / IL2CPP with MelonLoader 0.7.3, adopted the identical Businesses 0.1.3 DLL, and launched the game with that mod. The loader log confirmed the expected mod checksum. Running-game mutation guards passed. Actual loader extraction was checked in a disposable folder using the checksum-pinned official archive.

Version 0.1.1 removes automatic seed import and the dependency on that mod for builds/tests. Existing user libraries and installed game mods remain untouched. Businesses is being separated into its own repository.

## Remaining release testing

A clean machine still needs testing for the dependency download/elevation workflow and a fresh game's first interop-generation launch. Co-op, long gameplay sessions, other game builds and public code signing are unverified. Hosted release publication awaits the first remote workflow run.
