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
    private readonly System.Windows.Threading.DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(3) };
    public MainWindow()
    {
        InitializeComponent();
        VersionLabel.Text = "PROTOTYPE  " + (typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "Unknown");
        Closed += (_, _) => registry.Dispose();
        Loaded += async (_, _) =>
        {
            await Run(() =>
            {
            if (File.Exists(ManagerSettings.FilePath)) settings = JsonFiles.Read<ManagerSettings>(ManagerSettings.FilePath);
            if (string.IsNullOrWhiteSpace(settings.GameFolder)) settings.GameFolder = FindGame() ?? "";
            GamePath.Text = settings.GameFolder;
            LibraryPath.Text = settings.LibraryFolder;
            if (!string.IsNullOrWhiteSpace(settings.GameFolder)) ModLibrary.ValidateLibraryLocation(settings.LibraryFolder, settings.GameFolder);
            SavePaths();
            Reload();
            });
            await LoadRegistry();
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
        PlayButton.IsEnabled = LoaderButton.IsEnabled = RuntimeButton.IsEnabled = false;
        packages = ModLibrary.List(settings.LibraryFolder);
        if (settings.GameFolder.Length > 0) game = GameDetector.Detect(settings.GameFolder);
        GameSummary.Text = game == null ? "Choose the folder containing Schedule I.exe." : $"Schedule I {game.Version}  ·  {game.Backend}  ·  Windows x64";
        DependencySummary.Text = game == null ? "" : $"MelonLoader: {game.LoaderVersion}    |    .NET 6 Desktop: {(game.RuntimeInstalled ? "Installed" : "Missing")}";
        var stopped = game != null && !GameDetector.IsRunning(game);
        lastRunning = game != null && !stopped;
        PlayButton.IsEnabled = stopped;
        LoaderButton.IsEnabled = stopped && game!.LoaderVersion == "Missing";
        RuntimeButton.IsEnabled = stopped && !game!.RuntimeInstalled;
        ModGrid.ItemsSource = packages.Select(p =>
        {
            var state = game == null ? "Select a game folder" : ModDeployment.Status(game, p);
            var issue = game == null ? "" : ModDeployment.CompatibilityIssue(game, p);
            var canToggle = stopped && (state == "Enabled" || (issue.Length == 0 && (state == "Disabled" || state.StartsWith("Installed externally"))));
            return new { Name = p.Manifest.Name, Version = p.Manifest.Version, Status = state + (issue.Length > 0 ? " · " + issue : ""), Action = state == "Enabled" ? "Disable" : state.StartsWith("Installed externally") ? "Adopt" : "Enable", CanToggle = canToggle, Package = p };
        }).ToArray();
        LibrarySummary.Text = packages.Count == 0 ? "Your library is empty. Download a mod from Available mods." : $"{packages.Count} package(s) in your library. Close the game before enabling or disabling mods.";
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
        finally { ControlsPanel.IsEnabled = true; if (game != null) PlayButton.IsEnabled = !GameDetector.IsRunning(game); }
    }
    private Task Run(Action action, string? success = null) => Run(() => { action(); return Task.CompletedTask; }, success);
    private async void ChooseGame(object sender, RoutedEventArgs e) { var dialog = new OpenFolderDialog { Title = "Select the Schedule I game folder" }; if (dialog.ShowDialog() == true) { GamePath.Text = dialog.FolderName; await Run(() => { SavePaths(); Reload(); }); } }
    private async void ChooseLibrary(object sender, RoutedEventArgs e) { var dialog = new OpenFolderDialog { Title = "Select your mod library folder" }; if (dialog.ShowDialog() == true) { LibraryPath.Text = dialog.FolderName; await Run(() => { SavePaths(); Reload(); }); } }
    private async void SaveAndDetect(object sender, RoutedEventArgs e) => await Run(() => { SavePaths(); Reload(); }, "Folder settings saved.");
    private async void Refresh(object sender, RoutedEventArgs e) => await Run(() => { SavePaths(); Reload(); });
    private async void ToggleMod(object sender, RoutedEventArgs e) => await Run(() =>
    {
        SavePaths();
        var current = GameDetector.Detect(settings.GameFolder);
        var package = ModLibrary.Read(((LibraryPackage)((Button)sender).Tag).Directory);
        if (ModDeployment.Status(current, package) == "Enabled") ModDeployment.Disable(current, package); else ModDeployment.Enable(current, package);
        Reload();
    }, "Mod selection updated.");
    private async void InstallLoader(object sender, RoutedEventArgs e) => await Run(async () => { SavePaths(); StatusText.Text = "Downloading and verifying MelonLoader…"; await DependencyInstaller.InstallLoader(GameDetector.Detect(settings.GameFolder)); Reload(); }, "MelonLoader installed. Its first game launch may take longer while it generates game assemblies.");
    private async void InstallRuntime(object sender, RoutedEventArgs e) => await Run(async () => { SavePaths(); StatusText.Text = "Downloading and verifying the .NET runtime installer…"; await DependencyInstaller.InstallRuntime(GameDetector.Detect(settings.GameFolder)); Reload(); }, "Runtime installation finished.");
    private async void Play(object sender, RoutedEventArgs e) => await Run(() => { SavePaths(); Reload(); var current = game ?? throw new IOException("Choose a valid game folder."); ModDeployment.ValidateLaunch(current, packages); using var process = GameDetector.Launch(current); }, "Schedule I launched. Mod changes are locked while it is running.");
    private async void OpenLibrary(object sender, RoutedEventArgs e) => await Run(() => { SavePaths(); Directory.CreateDirectory(settings.LibraryFolder); Open(settings.LibraryFolder); });
    private async void OpenGame(object sender, RoutedEventArgs e) => await Run(() => Open(GameDetector.Detect(GamePath.Text).Folder));
    private async void OpenLog(object sender, RoutedEventArgs e) => await Run(() => { var path = Path.Combine(GameDetector.Detect(GamePath.Text).Folder, "MelonLoader", "Latest.log"); if (!File.Exists(path)) throw new IOException("No loader log yet. Launch the game first."); Open(path); });
    private static void Open(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
}
