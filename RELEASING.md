# Release maintenance

Run `./package.ps1` to build the mod and create a distribution ZIP under
`artifacts/releases`. It reads the package version directly from the compiled
DLL's MelonInfo attribute, keeping the marketplace version consistent with what
the game loads. The DLL's internal assembly version is not used as its release
identifier.

## Each release

1. Keep source in Git and tag each published version, for example `v0.1.3`.
   This workspace is not yet a Git repository and no remote has been created.
2. Bump the version in Mod.cs when the distributed code changes. Use patch
   increments for fixes, minor increments for features and a major increment
   for a deliberately incompatible release.
3. Update packaging/CHANGELOG.md and packaging/compatibility.json with actual
   test results. Mod version and compatible game versions are separate data.
4. Run the release tests below, then run package.ps1. The local package checks
   do not substitute for a clean mod-manager installation test.
5. On Thunderstore, select your publishing team, Schedule I, and IL2CPP as the
   backend category. Upload the ZIP. Update the same package name under the same
   team with a higher version for subsequent releases.

The package includes the mod DLL, documentation, original package icon,
compatibility notes and required third-party notices. It excludes game DLLs,
saves, development references, SDKs and MelonLoader itself. The loader is a
declared dependency.

## After a game update

- Preserve a separate test installation of the previously supported build where
  available. Keep known-good release ZIPs rather than replacing them in place.
- Generate fresh IL2CPP references from the new game build. Inspect changes to
  the phone, GameInput, business operations and UI hierarchy before rebuilding.
- Build and test in an isolated save copy. Verify loading, header button edges,
  physical Home, Back, Escape, phone orientation, background, multiple business
  switching, no owned businesses, no active deposits, operation completion,
  saving/reloading and returning to the menu/re-entering a save.
- Include longer play sessions and combinations with popular mods. If co-op
  compatibility is claimed, test host and client separately.
- Add a game version to the tested list only after those checks. One mod build
  may support several game versions; a game update does not automatically
  require a new binary if the existing release passes testing.
- If a compatibility patch is needed, publish a new mod version and explain
  the game builds that each release supports. Players on an older game build
  should retain the compatible mod release.

For future code hardening, add startup member/hierarchy checks and disable this
app cleanly when required interfaces are missing. Such checks reduce avoidable
errors but cannot guarantee against every mid-session/native crash. This
release has an IL2CPP domain declaration, but no exact game-version gate.

## Publishing routes checked on 2026-10-16

Schedule I's Steam Workshop URL redirects to the general Steam Workshop page;
its Steam store features do not advertise Workshop support. Workshop requires
integration by the game developer. Thunderstore is the current packaging route
for this mod. Nexus Mods can also host a manual installation ZIP.

References:
- https://wiki.thunderstore.io/mods/creating-a-package
- https://wiki.thunderstore.io/mods/packaging-your-mods
- https://wiki.thunderstore.io/mods/updating-a-package
- https://thunderstore.io/c/schedule-i/p/LavaGang/MelonLoader/versions/
- https://partner.steamgames.com/doc/features/workshop
