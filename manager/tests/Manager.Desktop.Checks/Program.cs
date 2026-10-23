using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ScheduleI.Manager;
using ScheduleI.Manager.Desktop;

internal static class Program
{
    private sealed class DelayedRegistryHandler : HttpMessageHandler
    {
        public readonly TaskCompletionSource<HttpResponseMessage> Response = new();
        public HttpRequestMessage? Request;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) { Request = request; return Response.Task; }
    }
    [STAThread]
    private static void Main(string[] args)
    {
        var root = Path.Combine(Path.GetTempPath(), "ScheduleI-desktop-checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var window = new MainWindow(); // Never shown: startup does not load real settings or fetch the registry.
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(MainWindow);
        void Set(string name, object? value) => type.GetField(name, flags)!.SetValue(window, value);
        void Call(string name) => type.GetMethod(name, flags)!.Invoke(window, null);
        T Control<T>(string name) where T : FrameworkElement => (T)window.FindName(name);
        void Check(bool value, string description) { if (!value) throw new Exception(description); Console.WriteLine("PASS desktop: " + description); }
        ((DispatcherTimer)type.GetField("timer", flags)!.GetValue(window)!).Stop();
        try
        {
#if !MAINTAINER
            Check(type.GetMethod("OpenReview", flags) == null && type.GetField("suiteCancellation", flags) == null && window.Title == "Schedule I Mod Manager", "public desktop contains no maintainer controls or launch suite");
#endif
            var library = Path.Combine(root, "Library");
            Set("settings", new ManagerSettings { LibraryFolder = library, GameFolder = Path.Combine(root, "MissingGame") });
            Call("Reload");
            Check(Control<Border>("CriticalBanner").Visibility == Visibility.Visible && !Control<Button>("PlayButton").IsEnabled, "missing game blocks launch with persistent notice");
            Control<RadioButton>("BrowseNav").IsChecked = true;
            Check(Control<Grid>("BrowsePage").Visibility == Visibility.Visible && Control<Grid>("MyModsPage").Visibility == Visibility.Collapsed, "browse navigation opens a separate page");
            Control<TextBlock>("StatusText").Text = "Ready.";
            Call("UpdateRequirementControls");
            Check(Control<Border>("CriticalBanner").Visibility == Visibility.Visible, "activity messages and navigation do not dismiss missing-game banner");
            Control<RadioButton>("SettingsNav").IsChecked = true;
            Check(Control<ScrollViewer>("SettingsPage").Visibility == Visibility.Visible && Control<Grid>("BrowsePage").Visibility == Visibility.Collapsed, "settings navigation separates folder configuration");
            var gameFolder = Path.Combine(root, "Game");
            Directory.CreateDirectory(gameFolder);
            File.WriteAllText(Path.Combine(gameFolder, "version.dll"), "fixture");
            var game = new GameInstallation(gameFolder, Path.Combine(gameFolder, "Schedule I.exe"), "0.4.7f5", "IL2CPP", "0.7.3", true);
            var ready = new RequirementStatus(game, new LoaderStatus(LoaderState.Ready, "Verified"), false);
            Set("game", game); Set("gameProblem", null); Set("libraryProblem", null); Set("requirements", ready);
            Call("UpdateRequirementControls");
            Check(Control<Border>("CriticalBanner").Visibility == Visibility.Collapsed && Control<Button>("PlayButton").IsEnabled, "resolved setup clears notice and enables launch");
            Set("game", game with { RuntimeInstalled = false });
            Call("UpdateRequirementControls");
            Check(Control<TextBlock>("CriticalMessage").Text.Contains(".NET 6") && !Control<Button>("PlayButton").IsEnabled, "missing runtime is visible across pages and blocks play");
            Set("game", game); Set("restartRequired", true); Call("UpdateRequirementControls");
            Check(Control<TextBlock>("CriticalMessage").Text.Contains("restart") && !Control<Button>("PlayButton").IsEnabled, "restart requirement persists independently of activity");
            Set("restartRequired", false); Set("requirements", ready with { Loader = new(LoaderState.Blocked, "Changed loader files") }); Call("UpdateRequirementControls");
            Check(Control<TextBlock>("CriticalMessage").Text.Contains("Changed loader files") && !Control<Button>("PlayButton").IsEnabled, "changed loader remains blocked until verified");
            Set("requirements", ready); Set("libraryProblem", "Library unavailable"); Call("UpdateRequirementControls");
            Check(Control<TextBlock>("CriticalMessage").Text.Contains("Library unavailable") && !Control<Button>("PlayButton").IsEnabled, "library failure cannot silently allow launch");
            Set("libraryProblem", null); Set("checkingRequirements", true); Call("UpdateRequirementControls");
            Check(!Control<Button>("PlayButton").IsEnabled, "play stays disabled during verification");
            Set("checkingRequirements", false); Set("requirementProblem", "Verification failed"); Call("UpdateRequirementControls");
            Check(Control<TextBlock>("CriticalMessage").Text.Contains("Verification failed"), "failed verification retains a blocker");
            var packageFolder = Path.Combine(library, "FixtureMod-0.0.1");
            Directory.CreateDirectory(Path.Combine(packageFolder, "Mods"));
            var dll = Path.Combine(packageFolder, "Mods", "FixtureMod.dll");
            File.Copy(typeof(ModLibrary).Assembly.Location, dll);
            var package = new LibraryPackage(packageFolder, new PackageManifest { Name = "FixtureMod", Version = "0.0.1" },
                new Compatibility { Backend = "IL2CPP", GameVersions = ["0.4.7f5"], Loader = new() { Name = "MelonLoader", Versions = ["0.7.3"] } }, dll, ModLibrary.Hash(dll));
            Set("packages", new LibraryPackage[] { package }); Set("requirementProblem", null);
            void RenderMods() => type.GetMethod("RenderMods", flags)!.Invoke(window, [true]);
            object Row() => Control<DataGrid>("ModGrid").ItemsSource.Cast<object>().Single();
            T Value<T>(string name) => (T)Row().GetType().GetProperty(name)!.GetValue(Row())!;
            var newerGame = game with { Version = "0.4.7f12" };
            Set("game", newerGame); Set("requirements", ready with { Game = newerGame }); RenderMods();
            Check(!Value<bool>("CanToggle") && Value<string>("Action") == "Blocked" && Value<string>("ToggleReason").Contains("0.4.7f12"), "unsupported game explains why the mod action is blocked");
            Check(Control<Border>("ModWarningsBanner").Visibility == Visibility.Visible && Control<TextBlock>("ModWarningsMessage").Text.Contains("0.4.7f5"), "untested disabled mod gets a persistent compatibility notice");
            Check((string)Control<Button>("PlayButton").Content == "Play without mods", "launch action explicitly describes the disabled-mod session");
            Set("game", game); Set("requirements", ready); RenderMods();
            Check(Value<bool>("CanToggle") && Value<string>("Action") == "Enable" && Control<Border>("ModWarningsBanner").Visibility == Visibility.Collapsed, "supported game exposes Enable and clears the compatibility notice");
            ModDeployment.Enable(game, package); RenderMods();
            Check(Value<bool>("CanToggle") && Value<string>("Action") == "Disable", "enabling updates the UI to a working Disable action");
            ModDeployment.Disable(game, package); RenderMods();
            Check(Value<bool>("CanToggle") && Value<string>("Action") == "Enable" && File.Exists(dll), "disabling restores Enable and preserves the downloaded mod");
            var older = package with { Manifest = new PackageManifest { Name = "FixtureMod", Version = "0.0.0" } };
            Set("packages", new LibraryPackage[] { older, package }); RenderMods();
            Check(Control<DataGrid>("ModGrid").ItemsSource.Cast<object>().Count() == 1, "downloaded versions share one mod row");
            var libraryRow = (LibraryModRow)Row();
            libraryRow.Selected = libraryRow.Versions.Single(v => v.Value.Manifest.Version == "0.0.0");
            Check(libraryRow.Package == older && libraryRow.AllPackages.Length == 2, "library selector targets one version while whole-mod deletion sees all versions");
            var releaseA = new RegistryRelease { Version = "0.1.3", PackageName = "FixtureMod", Compatibility = package.Compatibility };
            var releaseB = new RegistryRelease { Version = "0.1.4", PackageName = "FixtureMod", Compatibility = package.Compatibility, IsLatest = true };
            Set("availableMods", new RegistryMod[] { new() { Id = "fixture", Name = "Fixture Mod", Versions = [releaseB, releaseA] } });
            Call("RenderRegistry");
            var browseRow = Control<DataGrid>("RegistryGrid").ItemsSource.Cast<RegistryModRow>().Single();
            Check(browseRow.Mod == releaseB && browseRow.Selected!.Label.Contains("Latest"), "Browse selects publisher Latest with a visible marker");
            browseRow.Selected = browseRow.Versions.Single(v => v.Value == releaseA);
            Check(browseRow.Mod == releaseA && browseRow.CanDownload && browseRow.Compatibility.Contains("0.4.7f5"), "Browse can target an older release with its own tested game metadata");
            Call("RenderRegistry");
            Check(Control<DataGrid>("RegistryGrid").ItemsSource.Cast<RegistryModRow>().Single().Mod == releaseA, "redrawing Browse preserves selected version");
            if (args.Length == 2 && args[0] == "--version-preview")
            {
                void Snapshot(string page, string filename)
                {
                    Control<RadioButton>(page).IsChecked = true;
                    var content = (FrameworkElement)window.Content;
                    ((Grid)content).Background = window.Background;
                    foreach (var name in new[] { "ModGrid", "RegistryGrid" }) { var grid = Control<DataGrid>(name); grid.EnableRowVirtualization = false; grid.EnableColumnVirtualization = false; }
                    window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    content.Measure(new Size(1160, 820)); content.Arrange(new Rect(0, 0, 1160, 820)); content.UpdateLayout();
                    window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    content.UpdateLayout();
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(1160, 820, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                    bitmap.Render(content);
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    Directory.CreateDirectory(args[1]);
                    using var output = File.Create(Path.Combine(args[1], filename)); encoder.Save(output);
                }
                Snapshot("BrowseNav", "browse-versions.png");
                Snapshot("MyModsNav", "library-versions.png");
            }
            Set("packages", new LibraryPackage[] { package });
            Set("availableMods", Array.Empty<RegistryMod>());
#if MAINTAINER
            Check(window.Title.Contains("Maintainer"), "maintainer edition is visibly identified");
            Set("review", new RegistryReview(23, "https://github.com/schedule-one-mods/schedule-one-mod-registry/pull/23", "contributor/registry", new string('a', 40)));
            var versionOverride = (CheckBox)type.GetField("unverifiedVersion", flags)!.GetValue(window)!;
            versionOverride.IsChecked = true;
            Check(MaintainerCompatibility.AllowUnverifiedGameVersion, "maintainer version override activates only inside a PR review");
            Set("game", newerGame); Set("requirements", ready with { Game = newerGame }); Set("packages", new LibraryPackage[] { package }); Set("libraryProblem", null); Set("gameProblem", null); RenderMods();
            Check(Value<bool>("CanToggle") && Value<string>("Action") == "Enable", "maintainer UI permits enabling an unverified game version when explicitly allowed");
            ModDeployment.Enable(newerGame, package); Call("UpdateRequirementControls");
            Check(Control<Button>("PlayButton").IsEnabled, "maintainer launch and enable share the same game-version override");
            ModDeployment.Disable(newerGame, package);
            versionOverride.IsChecked = false;
            Check(!MaintainerCompatibility.AllowUnverifiedGameVersion, "turning override off restores strict compatibility checks");
#endif
            var previousClient = (RegistryClient)type.GetField("registry", flags)!.GetValue(window)!;
            using (var handler = new DelayedRegistryHandler())
            using (var delayed = new RegistryClient(new HttpClient(handler)))
            {
                Set("registry", delayed);
                var pending = (Task)type.GetMethod("LoadRegistry", flags)!.Invoke(window, null)!;
                Set("registry", previousClient);
                var currentList = new RegistryMod[] { new() { Id = "current-review" } };
                Set("availableMods", currentList);
                handler.Response.SetResult(new(System.Net.HttpStatusCode.OK) { RequestMessage = handler.Request, Content = new StringContent("{\"schema_version\":2,\"mods\":[]}") });
                pending.GetAwaiter().GetResult();
                Check(ReferenceEquals(type.GetField("availableMods", flags)!.GetValue(window), currentList), "a stale registry response cannot replace another review's mod list");
                Set("availableMods", Array.Empty<RegistryMod>());
            }
            if (args.Length == 2 && args[0] == "--preview")
            {
                Control<RadioButton>("SettingsNav").IsChecked = true;
                var content = (FrameworkElement)window.Content;
                content.Measure(new Size(1160, 1000)); content.Arrange(new Rect(0, 0, 1160, 1000)); content.UpdateLayout();
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(1160, 1000, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(content);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
                using var output = File.Create(args[1]); encoder.Save(output);
            }
            Console.WriteLine("Desktop checks passed. No real game files or settings changed.");
        }
        finally { window.Close(); Directory.Delete(root, true); }
    }
}
