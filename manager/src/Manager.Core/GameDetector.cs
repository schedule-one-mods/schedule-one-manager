using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace ScheduleI.Manager;

public static partial class GameDetector
{
    public static GameInstallation Detect(string folder)
    {
        folder = Path.GetFullPath(folder);
        var exe = Path.Combine(folder, "Schedule I.exe");
        var data = Path.Combine(folder, "Schedule I_Data");
        if (!File.Exists(exe) || !Directory.Exists(data)) throw new InvalidDataException("Select the folder containing Schedule I.exe and Schedule I_Data.");
        var backend = File.Exists(Path.Combine(folder, "GameAssembly.dll")) && File.Exists(Path.Combine(data, "il2cpp_data", "Metadata", "global-metadata.dat"))
            ? "IL2CPP" : File.Exists(Path.Combine(data, "Managed", "Assembly-CSharp.dll")) ? "Mono" : "Unknown";
        var metadata = Path.Combine(data, "globalgamemanagers");
        // Unity serializes PlayerSettings.bundleVersion here. Only accept a unique version.
        var versions = File.Exists(metadata) ? VersionPattern().Matches(Encoding.ASCII.GetString(File.ReadAllBytes(metadata))).Select(m => m.Value).Distinct().ToArray() : [];
        var loader = Path.Combine(folder, "MelonLoader", "net6", "MelonLoader.dll");
        var version = File.Exists(loader) ? FileVersionInfo.GetVersionInfo(loader).ProductVersion?.Split('+')[0] ?? "Unknown" : "Missing";
        return new(folder, exe, versions.Length == 1 ? versions[0] : "Unknown", backend, version, HasDesktopRuntime6())
        {
            VisualCppInstalled = SystemPrerequisites.HasVisualCppX64(),
            LoaderHealthy = RequirementCatalog.HasCriticalLoaderFiles(folder)
        };
    }

    public static bool HasDesktopRuntime6() => SystemPrerequisites.HasDesktopRuntime6();

    public static bool IsRunning(GameInstallation game)
    {
        foreach (var process in Process.GetProcessesByName("Schedule I"))
        {
            using (process)
            {
                try { if (string.Equals(process.MainModule?.FileName, game.Executable, StringComparison.OrdinalIgnoreCase)) return true; }
                catch { return true; } // If access is denied, do not risk editing a running installation.
            }
        }
        return false;
    }
    public static void RequireStopped(GameInstallation game)
    {
        if (IsRunning(game)) throw new InvalidOperationException("Close Schedule I before changing mods or dependencies.");
    }
    public static Process Launch(GameInstallation game)
    {
        RequireStopped(game);
        return Process.Start(new ProcessStartInfo(game.Executable) { WorkingDirectory = game.Folder, UseShellExecute = true }) ?? throw new IOException("Windows did not launch the game.");
    }
    [GeneratedRegex(@"(?<![0-9.])0\.\d+\.\d+[abfp]\d+", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();
}
