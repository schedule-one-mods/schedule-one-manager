using System.ComponentModel;
using System.Diagnostics;

namespace ScheduleI.Manager;

public sealed class DependencySetupService : IDisposable
{
    public static string LogDirectory => Path.Combine(ManagerPaths.Root, "Logs");
    private readonly VerifiedDependencyDownloader downloader;
    private readonly Func<string, GameInstallation> detect;
    private readonly Func<string, string, string, Task<int>> installMicrosoft;
    private readonly IReadOnlyList<LoaderFile> loaderFiles;
    private readonly DependencyArtifact loaderArtifact, dotnetArtifact, visualCppArtifact;
    private readonly string logs;
    public DependencySetupService() : this(new(), GameDetector.Detect, RunMicrosoftInstaller, RequirementCatalog.LoaderFiles,
        RequirementCatalog.Loader, RequirementCatalog.DotNet, RequirementCatalog.VisualCpp, LogDirectory) { }
    internal DependencySetupService(VerifiedDependencyDownloader downloader, Func<string, GameInstallation> detect, Func<string, string, string, Task<int>> installMicrosoft,
        IReadOnlyList<LoaderFile> files, DependencyArtifact loader, DependencyArtifact dotnet, DependencyArtifact visualCpp, string logs)
    {
        this.downloader = downloader; this.detect = detect; this.installMicrosoft = installMicrosoft; loaderFiles = files;
        loaderArtifact = loader; dotnetArtifact = dotnet; visualCppArtifact = visualCpp; this.logs = logs;
    }
    public Task<RequirementStatus> Inspect(string folder, CancellationToken cancellation = default) => Task.Run(() =>
    {
        var game = detect(folder);
        return new RequirementStatus(game, RequirementCatalog.InspectLoader(game, loaderFiles, cancellation), GameDetector.IsRunning(game));
    }, cancellation);

    public async Task<SetupResult> Setup(string folder, IProgress<SetupProgress>? progress = null, CancellationToken cancellation = default)
    {
        ModLibrary.RejectLinks(logs);
        Directory.CreateDirectory(logs);
        var logPath = Path.Combine(logs, "setup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8] + ".log");
        using var log = new StreamWriter(new FileStream(logPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
        void Report(SetupProgress value) { log.WriteLine($"{DateTimeOffset.Now:O} {value.Message}"); progress?.Report(value); }
        var forwarding = new InlineProgress(Report);
        try
        {
            Report(new("Checking game requirements…"));
            var status = await Inspect(folder, cancellation);
            GameDetector.RequireStopped(status.Game);
            if (status.Loader.State == LoaderState.Blocked) throw new InvalidOperationException(status.Loader.Message);
            if (status.Ready) { Report(new("All requirements are already installed. No changes made.", 100)); return new(status, false, logPath); }
            await Task.Run(() => CheckWriteAccess(status.Game), cancellation);
            foreach (var artifact in new[] { visualCppArtifact, dotnetArtifact })
            {
                cancellation.ThrowIfCancellationRequested();
                status = status with { Game = detect(folder) };
                var isVisualCpp = artifact == visualCppArtifact;
                if (isVisualCpp ? status.Game.VisualCppInstalled : status.Game.RuntimeInstalled) continue;
                var installer = await downloader.Get(artifact, forwarding, cancellation);
                GameDetector.RequireStopped(detect(folder));
                cancellation.ThrowIfCancellationRequested();
                Report(new($"Installing {artifact.Name}. Windows may ask for administrator approval. Complete or cancel the Windows installer to continue.", CanCancel: false));
                // Leave room for the bootstrapper's package and rollback log suffixes.
                var nativeLog = Path.Combine(logs, Path.GetFileNameWithoutExtension(logPath) + (isVisualCpp ? ".vc.log" : ".net6.log"));
                log.WriteLine("Windows installer log: " + nativeLog);
                var exitCode = await installMicrosoft(installer, artifact.Name, nativeLog);
                log.WriteLine($"{DateTimeOffset.Now:O} {artifact.Name} installer exit code: {exitCode}");
                if (exitCode == 1223 || exitCode == 1602) throw new OperationCanceledException("Windows installation was cancelled.");
                if (exitCode is 3010 or 1641)
                {
                    status = await Inspect(folder);
                    Report(new("Windows restart required. Restart the computer, then run Install missing requirements again.", 100));
                    return new(status, true, logPath);
                }
                if (exitCode == 1622) throw new IOException($"{artifact.Name}: Windows Installer could not open its installation log (exit 1622). Open the setup log to view installer details, then retry setup. Installer log: {nativeLog}");
                if (exitCode != 0) throw new IOException($"{artifact.Name} installer failed (exit {exitCode}). Open the setup log for details.");
                status = status with { Game = detect(folder) };
                if (!(isVisualCpp ? status.Game.VisualCppInstalled : status.Game.RuntimeInstalled)) throw new IOException($"{artifact.Name} installer finished, but the required x64 runtime was not detected. Restart Windows and recheck requirements.");
                Report(new($"{artifact.Name} installation verified."));
            }
            status = await Inspect(folder, cancellation);
            if (status.Loader.State == LoaderState.Blocked) throw new IOException(status.Loader.Message);
            if (status.Loader.State != LoaderState.Ready)
            {
                var archive = await downloader.Get(loaderArtifact, forwarding, cancellation);
                Report(new("Installing verified MelonLoader files…"));
                await Task.Run(() => DependencyInstaller.InstallPayload(detect(folder), archive, loaderArtifact, loaderFiles, true, cancellation), cancellation);
            }
            status = await Inspect(folder, cancellation);
            if (!status.Ready) throw new IOException("Requirements did not pass verification after setup. Open the setup log for details.");
            Report(new("Requirements installed and verified. MelonLoader may generate game assemblies on first launch.", 100));
            return new(status, false, logPath);
        }
        catch (Exception exception)
        {
            log.WriteLine($"{DateTimeOffset.Now:O} {exception}");
            exception.Data["SetupLogPath"] = logPath;
            throw;
        }
    }
    private static void CheckWriteAccess(GameInstallation game)
    {
        ModLibrary.RejectLinks(game.Folder);
        ModLibrary.RejectLinks(Path.Combine(game.Folder, "Mods"));
        ModLibrary.RejectLinks(Path.Combine(game.Folder, "UserData", "ScheduleIModManager"));
        var probe = Path.Combine(game.Folder, ".manager-write-check-" + Guid.NewGuid().ToString("N"));
        try { using var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose); }
        catch (UnauthorizedAccessException exception) { throw new IOException("The game folder is not writable. Choose an installation folder you can modify, or grant your Windows user write access before setup.", exception); }
    }
    private static async Task<int> RunMicrosoftInstaller(string file, string name, string log)
    {
        try
        {
            var info = MicrosoftInstallerStartInfo(file, log);
            using var process = await Task.Run(() => Process.Start(info) ?? throw new IOException("Could not start " + name));
            // Once Windows Installer starts, wait for its result; never kill a system installation on cancellation.
            await process.WaitForExitAsync();
            return process.ExitCode;
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223) { throw new OperationCanceledException("Administrator approval was cancelled. No further requirements were installed.", exception); }
    }
    internal static ProcessStartInfo MicrosoftInstallerStartInfo(string file, string log)
    {
        // Microsoft's bootstrapper requests elevation for its machine-wide packages itself.
        // ShellExecute's runas path can fail before the existing installer even starts.
        var info = new ProcessStartInfo(Path.GetFullPath(file))
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(file))!
        };
        foreach (var argument in new[] { "/install", "/passive", "/norestart", "/log", log }) info.ArgumentList.Add(argument);
        return info;
    }
    private sealed class InlineProgress(Action<SetupProgress> report) : IProgress<SetupProgress> { public void Report(SetupProgress value) => report(value); }
    public void Dispose() => downloader.Dispose();
}
