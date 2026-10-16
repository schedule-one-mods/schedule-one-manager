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
    private readonly RegistryClient registry = new();
    private IReadOnlyList<RegistryMod> availableMods = [];
    private bool? lastRunning;
    private readonly DependencySetupService setup = new();
    private RequirementStatus? requirements;
    private string? requirementFolder, setupLog;
    private bool setupBusy, restartRequired;
    private int checkGeneration;
    private CancellationTokenSource? setupCancellation;
    private readonly System.Windows.Threading.DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(3) };
    public MainWindow()
    {
        InitializeComponent();
        VersionLabel.Text = "PROTOTYPE  " + (typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "Unknown");
        Closed += (_, _) => { registry.Dispose(); setup.Dispose(); };
        Closing += (_, e) => { if (setupBusy) { e.Cancel = true; StatusText.Text = "Wait for setup to finish, or use Cancel setup while downloading."; } };
        Loaded += async (_, _) =>
        {
            await Run(() =>
            {
            if (File.Exists(ManagerSettings.FilePath)) settings = JsonFiles.Read<ManagerSettings>(ManagerSettings.FilePath);
            restartRequired = SystemPrerequisites.RestartStillRequired(settings.DependencyRestartRequestedAt);
            if (!restartRequired) settings.DependencyRestartRequestedAt = null;
            if (string.IsNullOrWhiteSpace(settings.GameFolder)) settings.GameFolder = FindGame() ?? "";
            GamePath.Text = settings.GameFolder;
            LibraryPath.Text = settings.LibraryFolder;
            if (!string.IsNullOrWhiteSpace(settings.GameFolder)) ModLibrary.ValidateLibraryLocation(settings.LibraryFolder, settings.GameFolder);
            SavePaths();
            Reload();
            if (packages.Count > 0) ModTabs.SelectedIndex = 1;
            });
            await Task.WhenAll(CheckRequirements(), LoadRegistry());
        };
        timer.Tick += (_, _) =>
        {
            if (!ControlsPanel.IsEnabled || game == null) return;
            try { if (lastRunning != GameDetector.IsRunning(game)) Reload(); }
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
        settings.LibraryFolder = Path.GetFullPath(LibraryPath.Text.Trim());
        settings.GameFolder = GamePath.Text.Trim();
        if (settings.GameFolder.Length > 0)
        {
            settings.GameFolder = Path.GetFullPath(settings.GameFolder);
            ModLibrary.ValidateLibraryLocation(settings.LibraryFolder, settings.GameFolder);
        }
        JsonFiles.Write(ManagerSettings.FilePath, settings);
    }
    private void Reload()
    {
        game = null;
        PlayButton.IsEnabled = SetupButton.IsEnabled = false;
        packages = ModLibrary.List(settings.LibraryFolder);
        if (settings.GameFolder.Length > 0) game = GameDetector.Detect(settings.GameFolder);
        GameSummary.Text = game == null ? "Choose the folder containing Schedule I.exe." : $"Schedule I {game.Version}  ·  {game.Backend}  ·  Windows x64";
        if (!string.Equals(requirementFolder, game?.Folder, StringComparison.OrdinalIgnoreCase)) { requirements = null; requirementFolder = game?.Folder; checkGeneration++; }
        var stopped = game != null && !GameDetector.IsRunning(game);
        lastRunning = game != null && !stopped;
        if (requirements != null && game != null) requirements = requirements with { Game = game, Running = !stopped };
        UpdateRequirementControls();
        ModGrid.ItemsSource = packages.Select(p =>
        {
            var state = game == null ? "Select a game folder" : ModDeployment.Status(game, p);
            var issue = game == null ? "" : ModDeployment.CompatibilityIssue(game, p);
            var canToggle = stopped && (state == "Enabled" || (requirements?.Ready == true && !restartRequired && issue.Length == 0 && (state == "Disabled" || state.StartsWith("Installed externally"))));
            return new { Name = p.Manifest.Name, Version = p.Manifest.Version, Status = state + (issue.Length > 0 ? " · " + issue : ""), Action = state == "Enabled" ? "Disable" : state.StartsWith("Installed externally") ? "Adopt" : "Enable", CanToggle = canToggle, Package = p };
        }).ToArray();
        var enabled = game == null ? 0 : packages.Count(p => ModDeployment.Status(game, p) == "Enabled");
        LibrarySummary.Text = packages.Count == 0 ? "Your library is empty. Download a mod from Available mods." :
            $"{enabled} of {packages.Count} mod(s) enabled for the next launch. " +
            (stopped ? "Use Enable or Disable below, then Play Schedule I." : "Close the game to change enabled mods.");
        RenderRegistry();
    }
    private void RenderRegistry()
    {
        RegistryGrid.ItemsSource = availableMods.Select(mod =>
        {
            var downloaded = packages.Any(p => p.Manifest.Name == mod.PackageName && p.Manifest.Version == mod.Version);
            var compatibility = mod.Compatibility.Backend + " · " + string.Join(", ", mod.Compatibility.GameVersions);
            if (game != null && (game.Backend != mod.Compatibility.Backend || !mod.Compatibility.GameVersions.Contains(game.Version))) compatibility += " · Unverified for this game";
            return new { mod.Name, mod.Author, mod.Version, Compatibility = compatibility, Action = downloaded ? "Downloaded" : "Download", CanDownload = !downloaded, Mod = mod };
        }).ToArray();
    }
    private async Task LoadRegistry()
    {
        RegistryRefreshButton.IsEnabled = false;
        RegistrySummary.Text = "Fetching the community registry…";
        try
        {
            var fetched = await registry.Fetch();
            availableMods = fetched;
            RenderRegistry();
            RegistrySummary.Text = fetched.Count == 0 ? "No mods are listed in the community registry yet." : $"{fetched.Count} mod(s) available · schedule-one-mods/schedule-one-mod-registry";
        }
        catch (Exception exception)
        {
            RegistrySummary.Text = "Could not refresh the registry: " + exception.Message + (availableMods.Count > 0 ? " Showing the last list from this session." : " Your downloaded mods remain available in Installed mods.");
        }
        finally { RegistryRefreshButton.IsEnabled = true; }
    }
    private async void RefreshRegistry(object sender, RoutedEventArgs e) => await Run(LoadRegistry);
    private async void DownloadMod(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        SavePaths();
        var mod = (RegistryMod)((Button)sender).Tag;
        StatusText.Text = $"Downloading and verifying {mod.Name} {mod.Version}…";
        await registry.Download(mod, settings.LibraryFolder);
        Reload();
        ModTabs.SelectedIndex = 1;
    }, "Mod downloaded and verified. Use Enable in Installed mods when you’re ready.");
    private async Task Run(Func<Task> action, string? success = null)
    {
        ControlsPanel.IsEnabled = PlayButton.IsEnabled = false;
        try { await action(); StatusText.Text = success ?? "Ready."; }
        catch (Exception exception) { StatusText.Text = exception.Message; }
        finally { ControlsPanel.IsEnabled = true; UpdateRequirementControls(); }
    }
    private Task Run(Action action, string? success = null) => Run(() => { action(); return Task.CompletedTask; }, success);
    private async void ChooseGame(object sender, RoutedEventArgs e) { var dialog = new OpenFolderDialog { Title = "Select the Schedule I game folder" }; if (dialog.ShowDialog() == true) { GamePath.Text = dialog.FolderName; await Run(() => { SavePaths(); Reload(); }); await CheckRequirements(); } }
    private async void ChooseLibrary(object sender, RoutedEventArgs e) { var dialog = new OpenFolderDialog { Title = "Select your mod library folder" }; if (dialog.ShowDialog() == true) { LibraryPath.Text = dialog.FolderName; await Run(() => { SavePaths(); Reload(); }); } }
    private async void SaveAndDetect(object sender, RoutedEventArgs e) { await Run(() => { SavePaths(); Reload(); }, "Folder settings saved."); await CheckRequirements(); }
    private async void Refresh(object sender, RoutedEventArgs e) => await Run(() => { SavePaths(); Reload(); });
    private async void ToggleMod(object sender, RoutedEventArgs e)
    {
        string? result = null;
        await Run(() =>
        {
            SavePaths();
            var current = GameDetector.Detect(settings.GameFolder);
            var package = ModLibrary.Read(((LibraryPackage)((Button)sender).Tag).Directory);
            var disable = ModDeployment.Status(current, package) == "Enabled";
            if (disable) ModDeployment.Disable(current, package); else ModDeployment.Enable(current, package);
            result = $"{package.Manifest.Name} {(disable ? "disabled" : "enabled")}. The change applies on the next game launch.";
            Reload();
        });
        if (result != null) StatusText.Text = result;
    }
    private async void Play(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        SavePaths(); Reload();
        await CheckRequirements();
        if (requirements?.Ready != true || restartRequired) throw new IOException("Install or repair the game requirements before playing. If setup requested a restart, restart Windows first.");
        var current = game ?? throw new IOException("Choose a valid game folder.");
        ModDeployment.ValidateLaunch(current, packages);
        using var process = GameDetector.Launch(current);
    }, "Schedule I launched. Mod changes are locked while it is running.");
    private async void OpenLibrary(object sender, RoutedEventArgs e) => await Run(() => { SavePaths(); Directory.CreateDirectory(settings.LibraryFolder); Open(settings.LibraryFolder); });
    private async void OpenGame(object sender, RoutedEventArgs e) => await Run(() => Open(GameDetector.Detect(GamePath.Text).Folder));
    private async void OpenLog(object sender, RoutedEventArgs e) => await Run(() => { var path = Path.Combine(GameDetector.Detect(GamePath.Text).Folder, "MelonLoader", "Latest.log"); if (!File.Exists(path)) throw new IOException("No loader log yet. Launch the game first."); new LogWindow(this, path, false).Show(); });
    private static void Open(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

    private void UpdateRequirementControls()
    {
        var stopped = game != null && !GameDetector.IsRunning(game);
        var ready = requirements?.Ready == true && game?.RuntimeInstalled == true && game.VisualCppInstalled && game.LoaderHealthy;
        PlayButton.IsEnabled = !setupBusy && stopped && ready && !restartRequired;
        SetupButton.IsEnabled = !setupBusy && stopped && requirements?.CanSetup == true && !restartRequired;
        CheckRequirementsButton.IsEnabled = !setupBusy && game != null;
        if (game == null) { DependencySummary.Text = "Choose a valid game folder to check its requirements."; return; }
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
        requirements = null;
        UpdateRequirementControls();
        CheckRequirementsButton.IsEnabled = false;
        RequirementDetail.Text = "Verifying the installed loader and x64 runtimes…";
        try
        {
            var result = await setup.Inspect(folder);
            if (generation != checkGeneration) return;
            requirements = result; game = result.Game; requirementFolder = result.Game.Folder;
            Reload();
        }
        catch (Exception exception) { if (generation == checkGeneration) StatusText.Text = "Could not check requirements: " + exception.Message; }
        finally { if (generation == checkGeneration) UpdateRequirementControls(); }
    }
    private async void RecheckRequirements(object sender, RoutedEventArgs e)
    {
        await Run(() => { SavePaths(); Reload(); });
        await CheckRequirements();
    }
    private async void InstallRequirements(object sender, RoutedEventArgs e)
    {
        if (setupBusy) return;
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
}
