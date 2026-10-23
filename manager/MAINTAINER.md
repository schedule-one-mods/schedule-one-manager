# Local maintainer tool

Build from the repository root:

```powershell
.\manager\build.ps1 -Maintainer
.\manager\run.ps1 -Maintainer
```

The executable is `artifacts/manager/maintainer/publish/ScheduleIModManager.Maintainer.exe`. It is self-contained and portable. No MSI is produced, no release assets or GitHub output variables are written, and the build refuses GitHub Actions. Close the normal manager first; the editions share a single-instance lock to avoid concurrent changes to the game.

Public `build.ps1` and `build.ps1 -Installer` explicitly compile with `MaintainerBuild=false`. PR resolution, testing controls, evidence collection and compatibility override source files are excluded from those assemblies. Public checks assert those types and desktop entry points are absent. The existing public release workflow stays on that path. This is distribution scoping, not authentication: anyone with this source can compile a maintainer edition. Keep the local maintainer executable out of public releases.

## Review a registry PR

1. Open **Settings → Registry PR review**, paste a URL such as `https://github.com/schedule-one-mods/schedule-one-mod-registry/pull/23`, and click **Open PR review**. PR Files/Commits/Checks URLs also work.
2. The manager resolves the PR's head repository and full commit SHA through GitHub's public API. Both index fetch routes use that same repository and commit. There is no fallback to `main`. The banner displays the pinned commit. Refresh keeps this pin; reopening the PR URL resolves its latest head again. Disable this review's enabled mods before switching commits.
3. **Browse mods** lists the entire index at that commit, including existing entries and proposed additions/updates. Download the mods you want to test. Wrong release repositories/tags, missing assets, checksum/size failures and mismatched package metadata use the normal manager validation and are recorded as failures.
4. Select a game installation in Settings and check its requirements. Use a dedicated test installation and disposable/backed-up saves for automatic launches. The manager does not install, download or switch game versions. Select each installed version separately; disable review mods before changing installations.
5. Use **My mods** to enable/disable combinations manually and **Play** to launch. **Allow testing an unverified game version** bypasses only the list of tested game versions, for this process/session. Backend, loader, runtime, checksum and ownership checks remain enforced. The package/index bytes stay unchanged. Opening a new PR review resets the override.
6. After gameplay, click **Capture loader log**, add a manual note if needed, and **Open evidence folder**. Attach the report and captured logs to the PR yourself. The tool never posts to GitHub.

GitHub rate limits and deleted/private source repositories can prevent loading a review; errors are shown. No token is required or stored. API reference: <https://docs.github.com/en/rest/pulls/pulls#get-a-pull-request>.

## Automated launch observations

Download 1–30 mods into the review library, then click **Run launch suite**. It uses all downloaded packages in their library order. For A, B and C the cases are: no mods, A alone, B alone, C alone, A+B, A+B+C. It does not enumerate every possible subset. Use the manual controls for other combinations. Conflicting DLL filenames, external DLLs/plugins, missing requirements, and managed mods absent from this review's library block the automated suite before mutation.

The observation window defaults to 90 seconds and accepts 15–600 seconds. First-time interop generation may need a longer window or a manual preparation launch. The game runs visibly. Each process is closed after observation; if it ignores a normal close for ten seconds, the exact process launched by the suite is terminated. Do not play or load a save during a suite. **Cancel launch suite** stops the owned process and restores the original enabled set. Closing the manager during a suite requests cancellation and waits for restoration.

Outcomes distinguish an early exit (with exit code), absent/unchanged loader logs, loader error markers, and a process surviving the window with a fresh log. `StartupObserved` means only the latter. It does not prove menu readiness, that every mod initialized, full playability, compatibility, or absence of errors the heuristic did not detect. Read the captured log. A failed/inconclusive baseline stops the suite. Other case outcomes are recorded and testing continues.

The original enabled set is restored after success, failure or cancellation. Restoration refuses changed files or a running game. If it cannot finish, the report records `RESTORATION REQUIRED` and the original set; close the game, resolve the conflict and restore that set manually. A machine crash or force-killing the manager can interrupt restoration; `Suite started` records the original state before any changes.

## Data and evidence

Maintainer settings, dependency downloads and setup logs live under `%LOCALAPPDATA%/ScheduleIModManager.Maintainer`, separate from the public edition. Each PR commit has its own `Reviews/pr-<number>-<sha>/Library`. The library is fixed during a review. Restarting resumes the last pinned commit and its library, even if the PR has since changed, so you can disable previously enabled mods. The version override starts off. Reopen the PR URL explicitly to resolve a newer commit.

Each review opening creates a new `Evidence/<timestamp>-<id>` folder containing the exact fetched index, its SHA-256, `report.json`, download requests/results, package DLL hashes, actual selected game/backend/loader details, override changes, enabled sets, launch outcomes and captured loader logs. The report is updated as actions happen. Invalid downloaded bytes are rejected and not installed. A failed index validation preserves the fetched index and error in the attempted review's evidence folder.

Game files, loader requirements, deployment receipts and saves belong to the selected game installation and are not isolated by the data profile. Separate profiles prevent sharing manager settings/library/cache; use separate game installations when you need independent game state.

## Verification

Both build paths run offline core and WPF checks. Maintainer checks use fake GitHub/HTTP responses and simulated suite outcomes to verify fork pinning, fallback isolation, version override boundaries, launch sets, cancellation/failure restoration and external-file protection. Disposable helper processes additionally exercise actual observation, exit codes, log freshness, error markers, cancellation and owned-process cleanup. They never launch the real game. An actual game launch suite remains an acceptance check on the chosen game installation.

## Schema v2 release discovery

A pinned PR commit pins directory identity/discovery policy, not future upstream release lists. GitHub Releases remain publisher-controlled. The downloaded version descriptor, digest, package metadata and index snapshot are recorded as evidence; select the exact release to test. Each release reads compatibility at its source tag. Schema v1 is intentionally unsupported. Registry reviewers approve registration and policy; they do not certify every future release. No registry Actions or scanner is introduced.
