using System.IO.Compression;
using ScheduleI.Manager;

var root = Path.Combine(Path.GetTempPath(), "ScheduleIModManager-checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var count = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); count++; }
void Reject(Action action, string name) { try { action(); } catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException or System.Text.Json.JsonException) { Check(true, name); return; } throw new Exception("Expected rejection: " + name); }
try
{
    var library = Path.Combine(root, "Library");
    var folder = Path.Combine(root, "Game");
    Directory.CreateDirectory(Path.Combine(folder, "Schedule I_Data", "il2cpp_data", "Metadata"));
    File.WriteAllText(Path.Combine(folder, "Schedule I.exe"), "fixture");
    File.WriteAllText(Path.Combine(folder, "GameAssembly.dll"), "fixture");
    File.WriteAllText(Path.Combine(folder, "Schedule I_Data", "il2cpp_data", "Metadata", "global-metadata.dat"), "fixture");
    File.WriteAllText(Path.Combine(folder, "Schedule I_Data", "globalgamemanagers"), "bundle version 0.4.7f5");
    File.WriteAllText(Path.Combine(folder, "version.dll"), "fixture");
    var detected = GameDetector.Detect(folder);
    Check(detected.Version == "0.4.7f5" && detected.Backend == "IL2CPP", "detect installed version and backend from game files");
    var game = detected with { LoaderVersion = "0.7.3", RuntimeInstalled = true };
    // A harmless PE fixture exercises copying and hashing without loading mod or game code.
    var fixture = Path.Combine(root, "FixturePackage");
    Directory.CreateDirectory(Path.Combine(fixture, "Mods"));
    File.Copy(typeof(ModLibrary).Assembly.Location, Path.Combine(fixture, "Mods", "FixtureMod.dll"));
    JsonFiles.Write(Path.Combine(fixture, "manifest.json"), new PackageManifest { Name = "FixtureMod", Version = "0.0.1", Dependencies = ["LavaGang-MelonLoader-0.7.3"] });
    JsonFiles.Write(Path.Combine(fixture, "compatibility.json"), new Compatibility { Game = "Schedule I", GameVersions = ["0.4.7f5"], Backend = "IL2CPP", Platforms = ["Windows x64"], Loader = new() { Name = "MelonLoader", Versions = ["0.7.3"] } });
    var fixtureZip = Path.Combine(root, "fixture.zip");
    ZipFile.CreateFromDirectory(fixture, fixtureZip);
    var packageZip = args.Length > 0 ? args[0] : fixtureZip;
    var package = ModLibrary.Import(packageZip, library);
    Check(args.Length > 0 || (package.Manifest.Name == "FixtureMod" && package.Manifest.Version == "0.0.1"), "import local package without game or mod dependencies");
    Check(ModLibrary.Import(packageZip, library).Sha256 == package.Sha256 && ModLibrary.List(library).Count == 1, "repeat import is idempotent");
    Reject(() => ModLibrary.ValidateLibraryLocation(Path.Combine(folder, "Mods"), folder), "reject library nested in game");
    Reject(() => ModLibrary.ValidateLibraryLocation(root, folder), "reject library containing game");
    var unsafeZip = Path.Combine(root, "unsafe.zip");
    using (var zip = ZipFile.Open(unsafeZip, ZipArchiveMode.Create)) { using var writer = new StreamWriter(zip.CreateEntry("../outside.dll").Open()); writer.Write("bad"); }
    Reject(() => ModLibrary.Import(unsafeZip, library), "reject ZIP traversal");
    Check(!File.Exists(Path.Combine(root, "outside.dll")) && !Directory.EnumerateDirectories(library, ".import-*").Any(), "failed import leaves no payload or staging folder");
    Check(ModDeployment.CompatibilityIssue(game, package) == "", "known game and loader pass compatibility");
    Reject(() => ModDeployment.Enable(game with { Version = "0.4.8f1" }, package), "block unverified future game version");
    Reject(() => ModDeployment.Enable(game with { Backend = "Mono" }, package), "block wrong scripting backend");
    Reject(() => ModDeployment.Enable(game with { LoaderVersion = "0.7.2" }, package), "block untested loader version");
    Reject(() => ModDeployment.Enable(game with { RuntimeInstalled = false }, package), "block missing game runtime");
    ModDeployment.Enable(game, package);
    Check(ModDeployment.Status(game, package) == "Enabled", "enable and record ownership");
    ModDeployment.Enable(game, package);
    Check(ModDeployment.Status(game, package) == "Enabled", "repeat enable is idempotent");
    ModDeployment.ValidateLaunch(game, [package]);
    Reject(() => ModDeployment.ValidateLaunch(game with { Version = "Unknown" }, [package]), "launch rechecks compatibility after game update");
    Reject(() => ModDeployment.ValidateLaunch(game, []), "launch rejects missing managed library package");
    var target = Path.Combine(folder, "Mods", package.FileName);
    File.AppendAllText(target, "external edit");
    Reject(() => ModDeployment.Disable(game, package), "disable preserves externally changed DLL");
    Reject(() => ModDeployment.Enable(game, package), "enable preserves conflicting DLL");
    Check(File.ReadAllText(target).EndsWith("external edit"), "conflicting file remains intact");
    File.Copy(package.DllPath, target, true);
    ModDeployment.Disable(game, package);
    Check(!File.Exists(target) && File.Exists(package.DllPath), "disable removes deployed copy and preserves library");
    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
    File.Copy(package.DllPath, target);
    Check(ModDeployment.Status(game, package).StartsWith("Installed externally"), "identical external mod is offered for adoption");
    Reject(() => ModDeployment.Disable(game, package), "external mod cannot be removed without adoption");
    ModDeployment.Enable(game, package);
    ModDeployment.Disable(game, package);
    Check(!File.Exists(target), "adopt then disable external identical mod");
    if (args.Length > 1)
    {
        var real = GameDetector.Detect(args[1]);
        Check(real.Version == "0.4.7f5" && real.Backend == "IL2CPP" && real.LoaderVersion == "0.7.3" && real.RuntimeInstalled, "real installation version, backend, loader and runtime detection");
        Check(ModDeployment.CompatibilityIssue(real, package) == "", "actual installed game matches imported mod");
        Console.WriteLine("REAL GAME STATUS (read only): " + ModDeployment.Status(real, package));
        if (GameDetector.IsRunning(real))
        {
            Reject(() => ModDeployment.Enable(real, package), "running real game blocks enabling mods");
            Reject(() => ModDeployment.Disable(real, package), "running real game blocks disabling mods");
        }
        if (args.Contains("--exercise-real"))
        {
            GameDetector.RequireStopped(real);
            if (ModDeployment.Status(real, package) != "Enabled") throw new Exception("Real game round trip requires an already adopted identical package.");
            try
            {
                ModDeployment.Disable(real, package);
                Check(ModDeployment.Status(real, package) == "Disabled", "disable managed mod in actual game folder");
            }
            finally { ModDeployment.Enable(real, package); }
            Check(ModDeployment.Status(real, package) == "Enabled" && ModLibrary.Hash(Path.Combine(real.Folder, "Mods", package.FileName)) == package.Sha256, "re-enable actual mod DLL with identical checksum");
        }
    }
    if (args.Length > 2)
    {
        var fresh = Path.Combine(root, "FreshGame");
        Directory.CreateDirectory(fresh);
        var freshGame = game with { Folder = fresh, Executable = Path.Combine(fresh, "Schedule I.exe") };
        DependencyInstaller.InstallLoaderArchive(freshGame, args[2]);
        Check(File.Exists(Path.Combine(fresh, "version.dll")) && File.Exists(Path.Combine(fresh, "MelonLoader", "net6", "MelonLoader.dll")), "install checksum-pinned actual loader into disposable game fixture");
        var bootstrapHash = ModLibrary.Hash(Path.Combine(fresh, "version.dll"));
        Reject(() => DependencyInstaller.InstallLoaderArchive(freshGame, args[2]), "loader refuses to overwrite existing installation");
        Check(ModLibrary.Hash(Path.Combine(fresh, "version.dll")) == bootstrapHash, "existing loader remains unchanged");
    }
    Console.WriteLine($"{count} checks passed. " + (args.Contains("--exercise-real") ? "Actual game mod restored enabled with identical checksum." : "No real game files modified."));
}
finally { Directory.Delete(root, true); }
