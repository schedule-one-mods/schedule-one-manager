using System.Diagnostics;

namespace ScheduleI.Manager;

public sealed record LaunchCase(string Name, string[] Packages);
public sealed record LaunchObservation(string Outcome, double ObservedSeconds, int? ExitCode, bool FreshLoaderLog, string Detail);

public sealed class MaintainerLaunchSuite
{
    private readonly Func<GameInstallation, TimeSpan, CancellationToken, Task<LaunchObservation>> observe;
    public MaintainerLaunchSuite() : this(Observe) { }
    internal MaintainerLaunchSuite(Func<GameInstallation, TimeSpan, CancellationToken, Task<LaunchObservation>> observe) => this.observe = observe;
    public static IReadOnlyList<LaunchCase> Plan(IReadOnlyList<LibraryPackage> packages)
    {
        var cases = new List<LaunchCase> { new("baseline", []) };
        cases.AddRange(packages.Select(p => new LaunchCase("alone-" + p.Identity, [p.Identity])));
        for (var count = 2; count <= packages.Count; count++)
            cases.Add(new("together-" + count, packages.Take(count).Select(p => p.Identity).ToArray()));
        return cases;
    }
    public async Task Run(GameInstallation game, IReadOnlyList<LibraryPackage> packages, ReviewEvidence evidence, TimeSpan window,
        IProgress<string>? progress = null, CancellationToken cancellation = default)
    {
        if (packages.Count is 0 or > 30 || window < TimeSpan.FromSeconds(15) || window > TimeSpan.FromMinutes(10))
            throw new InvalidOperationException("Choose 1–30 downloaded mods and an observation window of 15–600 seconds.");
        GameDetector.RequireStopped(game);
        if (packages.Select(p => p.FileName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != packages.Count)
            throw new InvalidOperationException("Two packages use the same DLL filename. Test them separately.");
        ModDeployment.ValidateLaunch(game, packages);
        foreach (var package in packages)
        {
            if (ModDeployment.CompatibilityIssue(game, package) is { Length: > 0 } issue) throw new InvalidOperationException(package.Identity + ": " + issue);
            if (ModLibrary.Hash(package.DllPath) != package.Sha256) throw new IOException("Library DLL changed: " + package.Identity);
            var status = ModDeployment.Status(game, package);
            if (status is not ("Enabled" or "Disabled")) throw new InvalidOperationException("Resolve conflicting or external DLL first: " + package.Identity);
        }
        var original = packages.Where(p => ModDeployment.Status(game, p) == "Enabled").ToArray();
        var expected = original.Select(p => p.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var mods = Path.Combine(game.Folder, "Mods");
        ModLibrary.RejectLinks(mods);
        if (Directory.Exists(mods) && Directory.EnumerateFiles(mods, "*.dll", SearchOption.AllDirectories).Any(p => !expected.Contains(Path.GetFileName(p)) || Path.GetDirectoryName(p) != mods))
            throw new InvalidOperationException("The automated suite needs a game Mods folder containing only this review's managed DLLs. Use manual testing for other installed mods.");
        var plugins = Path.Combine(game.Folder, "Plugins");
        ModLibrary.RejectLinks(plugins);
        if (Directory.Exists(plugins) && Directory.EnumerateFiles(plugins, "*.dll", SearchOption.AllDirectories).Any())
            throw new InvalidOperationException("Use a test installation without external plugins for the automated suite.");
        evidence.Record("Suite started", new { Game = game, WindowSeconds = window.TotalSeconds, OriginalEnabled = original.Select(p => p.Identity), Packages = packages.Select(ReviewEvidence.Package), Cases = Plan(packages) });
        try
        {
            foreach (var test in Plan(packages))
            {
                cancellation.ThrowIfCancellationRequested();
                SetEnabled(game, packages, test.Packages);
                progress?.Report($"Observing {test.Name} for {window.TotalSeconds:0}s…");
                ModDeployment.ValidateLaunch(game, packages);
                LaunchObservation result;
                try { result = await observe(game, window, cancellation); }
                catch (Exception exception)
                {
                    evidence.Record("Launch observation interrupted", new { Test = test, Error = exception.Message, LoaderLog = evidence.CaptureLoaderLog(game, test.Name) });
                    throw;
                }
                evidence.Record("Launch observation", new { Test = test, Result = result, LoaderLog = evidence.CaptureLoaderLog(game, test.Name) });
                if (test.Name == "baseline" && result.Outcome != "StartupObserved")
                    throw new InvalidOperationException("Baseline did not produce startup evidence. Resolve the game/loader baseline before testing mods.");
            }
        }
        catch (Exception exception) { evidence.Record("Suite interrupted", new { Error = exception.Message }); throw; }
        finally
        {
            try
            {
                SetEnabled(game, packages, original.Select(p => p.Identity).ToArray());
                evidence.Record("Original enabled set restored");
            }
            catch (Exception exception)
            {
                evidence.Record("RESTORATION REQUIRED", new { Error = exception.Message, OriginalEnabled = original.Select(p => p.Identity) });
                throw new IOException("Could not restore the original enabled mods. Close the game and use the report's OriginalEnabled list to restore them. " + exception.Message, exception);
            }
        }
        evidence.Record("Suite completed");
    }
    private static void SetEnabled(GameInstallation game, IReadOnlyList<LibraryPackage> packages, string[] identities)
    {
        GameDetector.RequireStopped(game);
        foreach (var package in packages)
            if (ModDeployment.Status(game, package) == "Enabled" && !identities.Contains(package.Identity)) ModDeployment.Disable(game, package);
        foreach (var package in packages)
            if (identities.Contains(package.Identity) && ModDeployment.Status(game, package) != "Enabled") ModDeployment.Enable(game, package);
        foreach (var package in packages)
        {
            var expected = identities.Contains(package.Identity) ? "Enabled" : "Disabled";
            if (ModDeployment.Status(game, package) != expected)
                throw new IOException("Enabled-state restoration or case setup encountered a changed/external DLL: " + package.Identity);
        }
    }
    private static string? ReadLog(string path)
    {
        if (!File.Exists(path)) return null;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
    private static Task<LaunchObservation> Observe(GameInstallation game, TimeSpan window, CancellationToken cancellation) => ObserveProcess(game, window, cancellation, GameDetector.Launch, TimeSpan.FromSeconds(10));
    internal static async Task<LaunchObservation> ObserveProcess(GameInstallation game, TimeSpan window, CancellationToken cancellation,
        Func<GameInstallation, Process> launch, TimeSpan shutdownGrace)
    {
        var log = Path.Combine(game.Folder, "MelonLoader", "Latest.log");
        var previous = ReadLog(log);
        using var process = launch(game);
        var stopwatch = Stopwatch.StartNew();
        var owned = false;
        try
        {
            if (process.HasExited) return new("ExitedEarly", stopwatch.Elapsed.TotalSeconds, process.ExitCode, false, "Game exited immediately.");
            // Verify ownership before any automated stop. Never stop Steam or another launcher.
            if (!string.Equals(process.MainModule?.FileName, game.Executable, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Launch returned another process. Close the game manually; automatic testing was stopped.");
            owned = true;
            while (stopwatch.Elapsed < window)
            {
                cancellation.ThrowIfCancellationRequested();
                if (process.HasExited) return new("ExitedEarly", stopwatch.Elapsed.TotalSeconds, process.ExitCode, ReadLog(log) is { } exitedLog && exitedLog != previous, "The game exited before the observation window ended.");
                await Task.Delay(500, cancellation);
            }
            var current = ReadLog(log);
            var fresh = current != null && current != previous;
            if (process.HasExited) return new("ExitedEarly", stopwatch.Elapsed.TotalSeconds, process.ExitCode, fresh, "The game exited during the observation window.");
            var errors = current?.Contains("[ERROR]", StringComparison.OrdinalIgnoreCase) == true || current?.Contains("[EXCEPTION]", StringComparison.OrdinalIgnoreCase) == true;
            return new(!fresh ? "Inconclusive" : errors ? "LoaderErrorsObserved" : "StartupObserved", stopwatch.Elapsed.TotalSeconds, null, fresh,
                "Process survived the window. This does not confirm menu readiness, mod initialization, gameplay, or compatibility; inspect the captured log.");
        }
        finally
        {
            if (owned && !process.HasExited)
            {
                process.CloseMainWindow();
                using var timeout = new CancellationTokenSource(shutdownGrace);
                try { await process.WaitForExitAsync(timeout.Token); }
                catch (OperationCanceledException) { if (!process.HasExited) process.Kill(); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
            }
            GameDetector.RequireStopped(game);
        }
    }
}
