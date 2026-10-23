using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ScheduleI.Manager;

namespace ScheduleI.Manager.Desktop;

public partial class MainWindow : Window
{
    private ManagerSettings settings = new();
    private GameInstallation? game;
    private IReadOnlyList<LibraryPackage> packages = [];
    private RegistryClient registry = new();
    private IReadOnlyList<RegistryMod> availableMods = [];
    private bool? lastRunning;
    private readonly DependencySetupService setup = new();
    private RequirementStatus? requirements;
    private string? requirementFolder, setupLog;
    private bool setupBusy, restartRequired;
    private string? gameProblem, libraryProblem, requirementProblem;
    private bool operationBusy, checkingRequirements;
    private int checkGeneration;
    private CancellationTokenSource? setupCancellation;
    private readonly System.Windows.Threading.DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(3) };
    public MainWindow()
    {
        InitializeComponent();
#if MAINTAINER
        InitializeMaintainer();
#endif
        VersionLabel.Text = "Version " + (typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "Unknown");
        MyModsNav.IsChecked = true;
        Closed += (_, _) => { registry.Dispose(); setup.Dispose(); };
        Closing += (_, e) => { if (setupBusy) { e.Cancel = true; StatusText.Text = "Wait for setup to finish, or use Cancel setup while downloading."; } };
        Loaded += async (_, _) =>
        {
            await Run(() =>
            {
            if (File.Exists(ManagerSettings.FilePath)) settings = JsonFiles.Read<ManagerSettings>(ManagerSettings.FilePath);
#if MAINTAINER
            RestoreMaintainerReview();
            settings.LibraryFolder = review == null ? Path.Combine(ManagerPaths.Root, "Library") : Path.Combine(review.SessionDirectory, "Library");
#endif
            restartRequired = SystemPrerequisites.RestartStillRequired(settings.DependencyRestartRequestedAt);
            if (!restartRequired) settings.DependencyRestartRequestedAt = null;
#if !MAINTAINER
            if (string.IsNullOrWhiteSpace(settings.GameFolder)) settings.GameFolder = FindGame() ?? "";
#endif
            GamePath.Text = settings.GameFolder;
            LibraryPath.Text = settings.LibraryFolder;
            Reload();
            });
            await Task.WhenAll(CheckRequirements(), LoadRegistry());
        };
        timer.Tick += async (_, _) =>
        {
            if (operationBusy || setupBusy || checkingRequirements) return;
            try
            {
                GameInstallation? current = null;
                try { if (settings.GameFolder.Length > 0) current = GameDetector.Detect(settings.GameFolder); } catch { }
                if (current != game || (current != null && lastRunning != GameDetector.IsRunning(current)))
                {
                    Reload(); await CheckRequirements();
                }
                else UpdateRequirementControls();
            }
            catch (Exception exception) { PlayButton.IsEnabled = false; StatusText.Text = exception.Message; }
        };
        timer.Start();
    }
    private static string? FindGame()
    {
        var candidates = new List<string>();
        var steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
        if (steam != null)
        {
            candidates.Add(Path.Combine(steam, "steamapps", "common", "Schedule I"));
            var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf)) foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                candidates.Add(Path.Combine(match.Groups[1].Value.Replace(@"\\", @"\"), "steamapps", "common", "Schedule I"));
        }
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
        {
            candidates.Add(Path.Combine(drive.RootDirectory.FullName, "Games", "Schedule I"));
            candidates.Add(Path.Combine(drive.RootDirectory.FullName, "SteamLibrary", "steamapps", "common", "Schedule I"));
        }
        return candidates.FirstOrDefault(c => File.Exists(Path.Combine(c, "Schedule I.exe")));
    }
    private void SavePaths()
    {
        if (string.IsNullOrWhiteSpace(LibraryPath.Text)) throw new IOException("Choose a mod library folder.");
        var library = Path.GetFullPath(LibraryPath.Text.Trim());
#if MAINTAINER
        if (!library.StartsWith(ManagerPaths.Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Maintainer libraries must stay within the isolated maintainer data folder.");
        if (review != null && !string.Equals(library, Path.Combine(review.SessionDirectory, "Library"), StringComparison.OrdinalIgnoreCase))
            throw new IOException("PR reviews use their isolated library. Open a different PR commit to start another library.");
#endif
        var folder = GamePath.Text.Trim();
        if (folder.Length > 0)
        {
            folder = Path.GetFullPath(folder);
#if MAINTAINER
            if (game != null && !folder.Equals(game.Folder, StringComparison.OrdinalIgnoreCase) && packages.Any(p => ModDeployment.Status(game, p) == "Enabled"))
                throw new IOException("Disable this review's mods before selecting another game installation.");
#endif
            ModLibrary.ValidateLibraryLocation(library, folder);
        }
        settings.LibraryFolder = library; settings.GameFolder = folder;
        JsonFiles.Write(ManagerSettings.FilePath, settings);
    }
    private void Reload()
    {
        game = null;
        PlayButton.IsEnabled = SetupButton.IsEnabled = false;
        gameProblem = libraryProblem = null;
        packages = [];
        try
        {
            if (settings.GameFolder.Length > 0) ModLibrary.ValidateLibraryLocation(settings.LibraryFolder, settings.GameFolder);
            packages = ModLibrary.List(settings.LibraryFolder);
        }
        catch (Exception exception) { libraryProblem = "Mod library: " + exception.Message; }
        try { if (settings.GameFolder.Length > 0) game = GameDetector.Detect(settings.GameFolder); }
        catch (Exception exception) { gameProblem = "Game not found. " + exception.Message; }
        GameSummary.Text = game == null ? "Choose the folder containing Schedule I.exe." : $"Schedule I {game.Version}  ·  {game.Backend}  ·  Windows x64";
        if (!string.Equals(requirementFolder, game?.Folder, StringComparison.OrdinalIgnoreCase)) { requirements = null; requirementFolder = game?.Folder; checkGeneration++; }
        var stopped = game != null && !GameDetector.IsRunning(game);
        lastRunning = game != null && !stopped;
        if (requirements != null && game != null) requirements = requirements with { Game = game, Running = !stopped };
        RenderMods(stopped);
        RenderRegistry();
        UpdateRequirementControls();
    }
    private void RenderMods(bool stopped)
    {
        var enabled = 0;
        var warnings = new List<string>();
        var previous = ModGrid.ItemsSource?.Cast<LibraryModRow>().ToDictionary(r => r.Name, r => r.Version);
        LibraryVersionState Inspect(LibraryPackage p)
        {
            string state;
            try { state = game == null ? "Select a game folder" : ModDeployment.Status(game, p); }
            catch (Exception exception) { libraryProblem = "Cannot inspect installed mods: " + exception.Message; state = "Cannot inspect mod"; }

            var issue = game == null ? "" : ModDeployment.CompatibilityIssue(game, p);
            var canToggle = stopped && (state == "Enabled" || (requirements?.Ready == true && !restartRequired && issue.Length == 0 && (state == "Disabled" || state.StartsWith("Installed externally"))));

            var reason = canToggle ? "Applies on your next game launch." : game == null ? "Choose your game folder in Settings." : !stopped ? "Close the game before changing mods." : issue.Length > 0 ? issue : state.StartsWith("Another version") ? "Select the enabled version and disable it first." : restartRequired ? "Restart Windows to finish setup." : "Check requirements in Settings before enabling this mod.";
            return new(state + (issue.Length > 0 ? " · " + issue : ""), state == "Enabled" ? "Disable" : !canToggle && issue.Length > 0 ? "Blocked" : state.StartsWith("Installed externally") ? "Adopt" : "Enable", canToggle, reason);
        }
        foreach (var p in packages)
        {
            if (game != null && ModDeployment.Status(game, p) == "Enabled") enabled++;
            var issue = game == null ? "" : ModDeployment.CompatibilityIssue(game, p);
            if (issue.Length > 0 && (game == null || ModDeployment.Status(game, p) != "Enabled")) warnings.Add(p.Identity + ": " + issue);
        }
        var groups = packages.GroupBy(p => p.Manifest.Name).ToArray();
        ModGrid.ItemsSource = groups.Select(group =>
        {
            var choices = group.OrderByDescending(p => System.Version.Parse(p.Manifest.Version)).Select(p => new VersionChoice<LibraryPackage>(p,
                p.Manifest.Version + (game != null && ModDeployment.Status(game, p) == "Enabled" ? " · Enabled" : ""))).ToArray();
            var selected = choices.FirstOrDefault(c => previous?.GetValueOrDefault(group.Key) == c.Value.Manifest.Version) ??
                choices.FirstOrDefault(c => game != null && ModDeployment.Status(game, c.Value) == "Enabled") ?? choices[0];
            return new LibraryModRow { Name = group.Key, Versions = choices, Selected = selected, Inspect = Inspect };
        }).ToArray();
        LibrarySummary.Text = packages.Count == 0 ? "Your library is empty. Download a mod from Available mods." :
            $"{groups.Length} mod(s), {packages.Count} downloaded version(s), {enabled} enabled. " +
            (stopped ? "Changes apply when you start the game." : "Close the game to change enabled mods.");
        EmptyLibrary.Visibility = packages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ModWarningsBanner.Visibility = warnings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ModWarningsMessage.Text = string.Join("\n", warnings) + "\nThese mods will stay disabled. Use a tested game version, or wait for a compatible mod release.";
        PlayButton.Content = enabled == 0 && packages.Count > 0 ? "Play without mods" : "Play Schedule I";
    }
    private void RenderRegistry()
    {
        var search = ModSearch?.Text.Trim() ?? "";
        var visible = availableMods.Where(mod => mod.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || mod.Author.Contains(search, StringComparison.OrdinalIgnoreCase)).ToArray();
        var previous = RegistryGrid.ItemsSource?.Cast<RegistryModRow>().Where(r => r.Mod != null).ToDictionary(r => r.Entry.Id, r => r.Version);
        RegistryGrid.ItemsSource = visible.Select(mod =>
        {
            var choices = mod.Versions.Select(v => new VersionChoice<RegistryRelease>(v, v.Label)).ToArray();
            var selected = choices.FirstOrDefault(c => previous?.GetValueOrDefault(mod.Id) == c.Value.Version) ?? choices.FirstOrDefault(c => c.Value.IsLatest);
            return new RegistryModRow { Name = mod.Name, Entry = mod, Versions = choices, Selected = selected, Game = game,
                IsDownloaded = release => packages.Any(p => p.Manifest.Name == release.PackageName && p.Manifest.Version == release.Version) };
        }).ToArray();
        EmptyRegistry.Visibility = visible.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private async Task LoadRegistry()
    {
#if MAINTAINER
        if (review == null)
        {
            RegistrySummary.Text = "Open Settings and paste a registry PR URL to begin a review.";
            availableMods = [];
            RenderRegistry();
            return;
        }
#endif
        RegistryRefreshButton.IsEnabled = false;
        RegistrySummary.Text = "Fetching the community registry…";
        var fetchingRegistry = registry;
        try
        {
            var fetched = await fetchingRegistry.Fetch();
            if (!ReferenceEquals(fetchingRegistry, registry)) return;
#if MAINTAINER
            evidence?.SaveIndex(fetchingRegistry);
            ShowReviewDetails();
#endif
            availableMods = fetched;
            RegistryBanner.Visibility = fetched.Any(m => m.ReleaseError != null) ? Visibility.Visible : Visibility.Collapsed;
            RegistryNotice.Text = string.Join("\n", fetched.Where(m => m.ReleaseError != null).Select(m => m.Name + ": " + m.ReleaseError));
            RenderRegistry();
            RegistrySummary.Text = fetched.Count == 0 ? "No mods are listed in the community registry yet." : $"{fetched.Count} mod(s) available · schedule-one-mods/schedule-one-mod-registry";
#if MAINTAINER
            RegistrySummary.Text = $"{fetched.Count} mod(s) · PR #{review.Number} · {review.Commit} (pinned)";
#endif
        }
        catch (Exception exception)
        {
            if (!ReferenceEquals(fetchingRegistry, registry)) return;
#if MAINTAINER
            evidence?.Record("Registry fetch failed", new { Error = exception.Message });
#endif
            RegistryBanner.Visibility = Visibility.Visible;
            RegistryNotice.Text = "Community registry unavailable. " + exception.Message + " Your downloaded mods are still available in My mods. Use Refresh registry to retry.";
            RegistrySummary.Text = "Could not refresh the registry: " + exception.Message + (availableMods.Count > 0 ? " Showing the last list from this session." : " Your downloaded mods remain available in Installed mods.");
        }
        finally { if (ReferenceEquals(fetchingRegistry, registry)) RegistryRefreshButton.IsEnabled = true; }
    }
    private async void RefreshRegistry(object sender, RoutedEventArgs e) => await Run(LoadRegistry);
    private async void DownloadMod(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        var mod = (RegistryRelease)((Button)sender).Tag;
        if (libraryProblem != null) throw new IOException(libraryProblem);
        StatusText.Text = $"Downloading and verifying {mod.Name} {mod.Version}…";
#if MAINTAINER
        evidence?.Record("Download requested", mod);
#endif
        var downloadedPackage = await registry.Download(mod, settings.LibraryFolder);
#if MAINTAINER
        evidence?.Record("Download verified", ReviewEvidence.Package(downloadedPackage));
#endif
        Reload();
    }, "Mod downloaded and verified. Open My mods to enable it for your next launch.");
    private async void DeleteVersion(object sender, RoutedEventArgs e) => await DeleteDownloads([((LibraryModRow)((Button)sender).Tag).Package]);
    private async void DeleteMod(object sender, RoutedEventArgs e) => await DeleteDownloads(((LibraryModRow)((Button)sender).Tag).AllPackages);
    private async Task DeleteDownloads(LibraryPackage[] targets) => await Run(() =>
    {
        if (libraryProblem != null) throw new IOException(libraryProblem);
        if (settings.GameFolder.Length > 0 && game == null) throw new IOException("Restore access to the configured game before deleting managed downloads.");
        var current = game == null ? null : GameDetector.Detect(settings.GameFolder);
        if (current != null) ModLibrary.ValidateLibraryLocation(settings.LibraryFolder, current.Folder);
        ModLibrary.Delete(settings.LibraryFolder, targets, current);
#if MAINTAINER
        evidence?.Record("Downloads deleted", targets.Select(ReviewEvidence.Package));
#endif
        Reload();
    }, "Downloaded versions deleted from the library.");
    private async Task Run(Func<Task> action, string? success = null)
    {
        operationBusy = true;
        ControlsPanel.IsEnabled = PlayButton.IsEnabled = false;
        try { await action(); StatusText.Text = success ?? "Ready."; }
        catch (Exception exception)
        {
            StatusText.Text = exception.Message;
#if MAINTAINER
            evidence?.Record("Action failed", new { Error = exception.Message });
#endif
        }
        finally { operationBusy = false; ControlsPanel.IsEnabled = true; UpdateRequirementControls(); }
    }
    private Task Run(Action action, string? success = null) => Run(() => { action(); return Task.CompletedTask; }, success);
    private async void ChooseGame(object sender, RoutedEventArgs e) { var dialog = new OpenFolderDialog { Title = "Select the Schedule I game folder" }; if (dialog.ShowDialog() == true) { GamePath.Text = dialog.FolderName; await Run(() => { SavePaths(); Reload(); }); await CheckRequirements(); } }
    private async void ChooseLibrary(object sender, RoutedEventArgs e) { var dialog = new OpenFolderDialog { Title = "Select your mod library folder" }; if (dialog.ShowDialog() == true) { LibraryPath.Text = dialog.FolderName; await Run(() => { SavePaths(); Reload(); }); } }
    private async void SaveAndDetect(object sender, RoutedEventArgs e) { await Run(() => { SavePaths(); Reload(); }, "Folder settings saved."); await CheckRequirements(); }
    private async void Refresh(object sender, RoutedEventArgs e) { await Run(Reload); await CheckRequirements(); }
    private async void ToggleMod(object sender, RoutedEventArgs e)
    {
        string? result = null;
        await Run(() =>
        {
#if MAINTAINER
            if (review == null) throw new IOException("Open a registry PR before enabling test mods.");
#endif
            var current = GameDetector.Detect(settings.GameFolder);
            var package = ModLibrary.Read(((LibraryPackage)((Button)sender).Tag).Directory);
            var disable = ModDeployment.Status(current, package) == "Enabled";
            if (disable) ModDeployment.Disable(current, package); else ModDeployment.Enable(current, package);
#if MAINTAINER
            evidence?.Record(disable ? "Mod disabled" : "Mod enabled", new { Game = current, Package = ReviewEvidence.Package(package) });
#endif
            result = $"{package.Manifest.Name} {(disable ? "disabled" : "enabled")}. The change applies on the next game launch.";
            Reload();
        });
        if (result != null) StatusText.Text = result;
    }
    private async void Play(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        Reload();
        await CheckRequirements();
        if (requirements?.Ready != true || restartRequired) throw new IOException("Install or repair the game requirements before playing. If setup requested a restart, restart Windows first.");
        var current = game ?? throw new IOException("Choose a valid game folder.");
        ModDeployment.ValidateLaunch(current, packages);
        using var process = GameDetector.Launch(current);
#if MAINTAINER
        evidence?.Record("Manual launch requested", new { Game = current, Enabled = packages.Where(p => ModDeployment.Status(current, p) == "Enabled").Select(ReviewEvidence.Package), ProcessId = process.Id });
#endif
    }, "Schedule I launched. Mod changes are locked while it is running.");
    private async void OpenLibrary(object sender, RoutedEventArgs e) => await Run(() => { SavePaths(); Directory.CreateDirectory(settings.LibraryFolder); Open(settings.LibraryFolder); });
    private async void OpenGame(object sender, RoutedEventArgs e) => await Run(() => Open(GameDetector.Detect(GamePath.Text).Folder));
    private async void OpenLog(object sender, RoutedEventArgs e) => await Run(() => { var path = Path.Combine(GameDetector.Detect(GamePath.Text).Folder, "MelonLoader", "Latest.log"); if (!File.Exists(path)) throw new IOException("No loader log yet. Launch the game first."); new LogWindow(this, path, false).Show(); });
    private static void Open(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

    private void UpdateRequirementControls()
    {
        var stopped = game != null && !GameDetector.IsRunning(game);
        var ready = requirements?.Ready == true && game?.RuntimeInstalled == true && game.VisualCppInstalled && game.LoaderHealthy;
        var blockers = new List<string>();
        if (game == null) blockers.Add(gameProblem ?? "Game not located. Choose your game folder in Settings.");
        if (libraryProblem != null) blockers.Add(libraryProblem);
        if (restartRequired) blockers.Add("Windows restart required. Restart your computer to complete setup.");
        if (game != null)
        {
            if (!game.RuntimeInstalled) blockers.Add(".NET 6 Desktop Runtime x64 is missing. Install requirements in Settings.");
            if (!game.VisualCppInstalled) blockers.Add("Visual C++ x64 is missing. Install requirements in Settings.");
            if (!game.LoaderHealthy || requirements?.Loader.State is LoaderState.Missing or LoaderState.Repairable or LoaderState.Blocked)
                blockers.Add(requirements?.Loader.Message ?? "MelonLoader is missing or incomplete. Install requirements in Settings.");
            if (requirementProblem != null) blockers.Add(requirementProblem);
            if (ready && libraryProblem == null)
                try { ModDeployment.ValidateLaunch(game, packages); }
                catch (Exception exception) { blockers.Add(exception.Message); }
        }
        CriticalBanner.Visibility = blockers.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        CriticalMessage.Text = string.Join("\n", blockers.Distinct());
        LaunchState.Text = setupBusy ? "Setting up" : !stopped && game != null ? "Game running" : blockers.Count > 0 ? "Setup needs attention" : !ready || checkingRequirements ? "Checking setup" : "Ready to play";
        PlayButton.IsEnabled = !operationBusy && !checkingRequirements && !setupBusy && stopped && ready && !restartRequired && blockers.Count == 0;
        SetupButton.IsEnabled = !operationBusy && !checkingRequirements && !setupBusy && stopped && requirements?.CanSetup == true && !restartRequired;
        CheckRequirementsButton.IsEnabled = !setupBusy && !checkingRequirements && game != null;
        if (game == null) { DependencySummary.Text = "Choose a valid game folder to check its requirements."; RequirementDetail.Text = gameProblem ?? "No game selected."; return; }
        var loader = requirements?.Loader.State.ToString() ?? "Not checked";
        DependencySummary.Text = $"MelonLoader {DependencyInstaller.LoaderVersion}: {loader}  ·  .NET 6 Desktop x64: {(game.RuntimeInstalled ? "Installed" : "Missing")}  ·  Visual C++ x64: {(game.VisualCppInstalled ? "Installed" : "Missing")}";
        RequirementDetail.Text = restartRequired ? "Restart Windows, then reopen the manager and recheck requirements." : requirements?.Loader.Message ?? "Check requirements to verify the loader files.";
        RequirementDetail.ToolTip = RequirementDetail.Text;
    }
    private async Task CheckRequirements()
    {
        if (game == null || setupBusy) { UpdateRequirementControls(); return; }
        var generation = ++checkGeneration;
        var folder = game.Folder;
        checkingRequirements = true;
        UpdateRequirementControls();
        CheckRequirementsButton.IsEnabled = false;
        RequirementDetail.Text = "Verifying the installed loader and x64 runtimes…";
        try
        {
            var result = await setup.Inspect(folder);
            if (generation != checkGeneration) return;
            requirementProblem = null;
            requirements = result; game = result.Game; requirementFolder = result.Game.Folder;
            Reload();
        }
        catch (Exception exception) { if (generation == checkGeneration) { requirementProblem = "Could not verify requirements: " + exception.Message; StatusText.Text = requirementProblem; } }
        finally { checkingRequirements = false; if (generation == checkGeneration) UpdateRequirementControls(); }
    }
    private async void RecheckRequirements(object sender, RoutedEventArgs e)
    {
        await Run(() => { SavePaths(); Reload(); });
        await CheckRequirements();
    }
    private async void InstallRequirements(object sender, RoutedEventArgs e)
    {
        if (setupBusy || checkingRequirements) return;
        try { SavePaths(); Reload(); if (game == null) throw new IOException("Choose a valid game folder."); }
        catch (Exception exception) { StatusText.Text = exception.Message; return; }
        setupBusy = true;
        checkGeneration++;
        using var cancellation = new CancellationTokenSource();
        setupCancellation = cancellation;
        ControlsPanel.IsEnabled = PlayButton.IsEnabled = false;
        SetupProgressBar.Visibility = CancelSetupButton.Visibility = Visibility.Visible;
        CancelSetupButton.IsEnabled = true;
        var progress = new Progress<SetupProgress>(value =>
        {
            if (!setupBusy) return;
            StatusText.Text = value.Message;
            SetupProgressBar.IsIndeterminate = value.Percent == null;
            if (value.Percent is double percent) SetupProgressBar.Value = percent;
            CancelSetupButton.IsEnabled = value.CanCancel;
        });
        try
        {
            var result = await setup.Setup(settings.GameFolder, progress, cancellation.Token);
            requirements = result.Status;
            setupLog = result.LogPath;
            restartRequired = result.RestartRequired;
            if (restartRequired) { settings.DependencyRestartRequestedAt = DateTimeOffset.UtcNow; JsonFiles.Write(ManagerSettings.FilePath, settings); }
            StatusText.Text = restartRequired ? "Windows restart required. Restart, then run setup again to finish any remaining requirements." : "All requirements installed and verified. You can enable compatible mods and play.";
        }
        catch (OperationCanceledException exception)
        {
            setupLog = exception.Data["SetupLogPath"] as string;
            StatusText.Text = "Setup cancelled. Completed installations and verified downloads are kept for the next attempt.";
        }
        catch (Exception exception) { setupLog = exception.Data["SetupLogPath"] as string; StatusText.Text = "Setup failed: " + exception.Message; }
        finally
        {
            setupBusy = false; setupCancellation = null;
            ControlsPanel.IsEnabled = true;
            SetupProgressBar.Visibility = CancelSetupButton.Visibility = Visibility.Collapsed;
            await CheckRequirements();
        }
    }
    private void CancelSetup(object sender, RoutedEventArgs e) { setupCancellation?.Cancel(); CancelSetupButton.IsEnabled = false; StatusText.Text = "Cancelling the download…"; }
    private async void OpenSetupLog(object sender, RoutedEventArgs e) => await Run(() =>
    {
        var path = setupLog != null && File.Exists(setupLog) ? setupLog : SetupLogs.Latest(DependencySetupService.LogDirectory);
        if (path == null || !File.Exists(path)) throw new IOException("No requirements setup log yet.");
        new LogWindow(this, path, true).Show();
    });
    private void Navigate(object sender, RoutedEventArgs e)
    {
        if (MyModsPage == null) return;
        var page = (string)((RadioButton)sender).Tag;
        MyModsPage.Visibility = page == "MyMods" ? Visibility.Visible : Visibility.Collapsed;
        BrowsePage.Visibility = page == "Browse" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = page == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        PageTitle.Text = page == "Browse" ? "Browse mods" : page == "Settings" ? "Settings" : "My mods";
        PageDescription.Text = page == "Browse" ? "Discover community mods. Download here, enable in My mods." : page == "Settings" ? "Your game, library, and everything needed to play." : "Choose what comes with you on your next session.";
    }
    private void ShowSettings(object sender, RoutedEventArgs e) => SettingsNav.IsChecked = true;
    private void ShowBrowse(object sender, RoutedEventArgs e) => BrowseNav.IsChecked = true;
    private void SearchMods(object sender, TextChangedEventArgs e) { if (RegistryGrid != null) RenderRegistry(); }
}
