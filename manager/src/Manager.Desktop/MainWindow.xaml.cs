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
    private bool? lastRunning;
    private readonly System.Windows.Threading.DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(3) };
    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await Run(async () =>
        {
            if (File.Exists(ManagerSettings.FilePath)) settings = JsonFiles.Read<ManagerSettings>(ManagerSettings.FilePath);
            if (string.IsNullOrWhiteSpace(settings.GameFolder)) settings.GameFolder = FindGame() ?? "";
            GamePath.Text = settings.GameFolder;
            LibraryPath.Text = settings.LibraryFolder;
            if (!string.IsNullOrWhiteSpace(settings.GameFolder)) ModLibrary.ValidateLibraryLocation(settings.LibraryFolder, settings.GameFolder);
            var seed = Path.Combine(AppContext.BaseDirectory, "SeedPackages");
            if (Directory.Exists(seed)) foreach (var zip in Directory.GetFiles(seed, "*.zip")) ModLibrary.Import(zip, settings.LibraryFolder);
            SavePaths();
            Reload();
            await Task.CompletedTask;
        });
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
        LibrarySummary.Text = packages.Count == 0 ? "No packages yet. Import a ZIP containing manifest.json, compatibility.json and one Mods DLL." : $"{packages.Count} package(s) in your library. Compatibility uses the publisher’s tested game and loader versions.";
    }
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
    private async void ImportMod(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Import a Schedule I mod package", Filter = "Mod package (*.zip)|*.zip" };
        if (dialog.ShowDialog() == true) await Run(() => { SavePaths(); ModLibrary.Import(dialog.FileName, settings.LibraryFolder); Reload(); }, "Package imported. Enable it when you’re ready.");
    }
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
