# Businesses

Check your laundering businesses from your phone, using the game's own business
computer screen. View every active deposit, its amount and time remaining, and
cycle between business timelines with **Next**. **Back** and the phone's physical
Home button return to your apps.

This is a status viewer: deposits still start at the business computer. It does
not change laundering times, capacity, inventory or bank balances.

## Compatibility

| Component | Tested target |
| --- | --- |
| Schedule I | **0.4.7f5** |
| Game backend | **IL2CPP** |
| MelonLoader | **0.7.3** |
| Platform | Windows x64 |

Mono / alternate builds are not supported by this DLL. Other game versions,
co-op clients and combinations with other mods are unverified. This release
does not enforce the tested game version at runtime. A successful launch on a
different build does not establish full compatibility.

## Install with a mod manager

Select Schedule I in Thunderstore Mod Manager or r2modman and install this
package. MelonLoader is declared as a dependency. Launch through the manager's
modded launch option. Installation through a fresh manager profile still needs
verification before the public release is marked fully tested.

## Manual installation

1. Close the game and install MelonLoader 0.7.3 from its official release.
2. Copy `Mods/LaunderingStatus.dll` from this ZIP into the game's `Mods` folder.
3. Launch the game, load your save and open **Businesses** on the phone.

No S1API or Sideload installation is required. Keep only one installed copy of
this mod's DLL when switching between manual and mod-manager installation.

## Updates and troubleshooting

Close the game before replacing the DLL. To uninstall manually, remove
`Mods/LaunderingStatus.dll`; with a manager, uninstall the package from its
profile. Existing game save data does not depend on this mod.

When reporting an issue, include the mod version, exact game version, IL2CPP
backend, loader version, other installed mods, steps to reproduce and relevant
errors from `MelonLoader/Latest.log`. Include a screenshot for display issues.
Review logs before sharing them publicly.

The phone shell uses MIT-licensed integration patterns from
[ScheduleOne-Sideload](https://github.com/DooDesch-Mods/ScheduleOne-Sideload).
See the included THIRD-PARTY-NOTICES.md. Game UI is instantiated from the running
game; no game assemblies or UI assets are distributed in this package.
