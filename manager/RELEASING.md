# GitHub releases

## First push

Create a GitHub repository and push this repository's `main` branch to it. Commit the prepared workflow and version changes when ready, then push that commit. No repository name or account is hardcoded. Do not configure personal access tokens: the release job uses GitHub's automatically provided `GITHUB_TOKEN` with `contents: write`. Repository/organization policy must permit GitHub Actions and that permission.

The workflow is `.github/workflows/release-manager.yml`. On a push to `main` it builds/tests the manager on Windows, uploads the packages as Actions artifacts, then publishes a new GitHub Release named **Schedule I Mod Manager 0.1.1** with tag **v0.1.1** if that release does not already exist. The tag targets the exact commit that produced the build. The workflow does not commit version changes or source files.

Release assets:

- `ScheduleIModManager-0.1.1-x64.msi`
- `ScheduleIModManager-0.1.1-x64.msi.sha256`
- `ScheduleIModManager-0.1.1-win-x64.zip`
- `ScheduleIModManager-0.1.1-win-x64.zip.sha256`

## Later commits and versions

Every push to `main` builds installable artifacts, retained in the Actions run for 14 days. If a release for the current version already exists, the release job skips publishing and leaves that release unchanged. This preserves the relationship between a version, its tag and its original binary.

To publish a new release, change **only** the version value in `manager/Directory.Build.props`, for example from `0.1.1` to `0.1.2`, then commit and push to `main`. The EXE, UI label, MSI and asset filenames use the same value automatically. Use three numeric components within Windows Installer's limits; prerelease suffixes are not supported yet.

Pull requests targeting `main` build and upload artifacts but cannot publish releases. **Actions → Build and release manager → Run workflow** can retry the workflow; select `main` to allow release publication. Nothing is published to GitHub from local builds. An existing release is never replaced by rerunning a workflow. If a failed upload leaves an incomplete release, delete that incomplete release manually before retrying or publish a new version.

The build and release jobs have separate permissions; only the release job can write repository contents. Third-party action references are pinned to commit hashes. The release job uses GitHub CLI to create the release and upload the already validated assets. See the official [workflow trigger documentation](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/trigger-a-workflow) and [GitHub CLI release command](https://cli.github.com/manual/gh_release_create).

## Separate mod repositories

Manager builds and release packages contain no mods. Existing Businesses files were moved into ignored `local-mod-archive/Businesses` locally so they can be extracted into a separate repository later. The first historical commit still contains the original mod source; this change does not rewrite Git history. Old local build artifacts are also preserved under ignored `artifacts`.

A future discovery protocol could use a shared GitHub topic and a versioned metadata file in each mod repository. The identifier/schema and discovery client are intentionally not implemented in this release pipeline. For now users import independently published mod package ZIPs.
