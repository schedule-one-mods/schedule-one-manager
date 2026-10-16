using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScheduleI.Manager;

public sealed record SetupProgress(string Message, double? Percent = null, bool CanCancel = true);
public sealed record DependencyArtifact(string Name, string FileName, string Url, string Hash, long Size, bool Sha512 = false);
public sealed record LoaderFile(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("sha256")] string Hash,
    [property: JsonPropertyName("size_bytes")] long Size);
public enum LoaderState { Missing, Ready, Repairable, Blocked }
public sealed record LoaderStatus(LoaderState State, string Message, int MissingFiles = 0);
public sealed record RequirementStatus(GameInstallation Game, LoaderStatus Loader, bool Running)
{
    public bool Ready => Game.RuntimeInstalled && Game.VisualCppInstalled && Game.LoaderHealthy && Loader.State == LoaderState.Ready;
    public bool CanSetup => !Running && Loader.State != LoaderState.Blocked && !Ready;
}
public sealed record SetupResult(RequirementStatus Status, bool RestartRequired, string LogPath);

public static class RequirementCatalog
{
    public static readonly DependencyArtifact Loader = new("MelonLoader 0.7.3", "MelonLoader.x64.0.7.3.zip", "https://github.com/LavaGang/MelonLoader/releases/download/v0.7.3/MelonLoader.x64.zip", DependencyInstaller.LoaderHash, 20155622);
    public static readonly DependencyArtifact DotNet = new(".NET 6 Desktop Runtime x64", "windowsdesktop-runtime-6.0.36-win-x64.exe", "https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/6.0.36/windowsdesktop-runtime-6.0.36-win-x64.exe", "86fa63997e7e0dc6f3bf609e00880388dcf8d985c8f6417d07ebbbb1ecc957bf90214c8ff93f559a0e762b5626ba8c56c581f4d506aa4de7555f9792c2da254d", 57380656, true);
    public static readonly DependencyArtifact VisualCpp = new("Visual C++ x64 Runtime", "VC_redist.x64.14.44.35211.exe", "https://download.visualstudio.microsoft.com/download/pr/990c720a-46ce-4dc7-87b9-c2eb615d34b9/CC0FF0EB1DC3F5188AE6300FAEF32BF5BEEBA4BDD6E8E445A9184072096B713B/VC_redist.x64.exe", "CC0FF0EB1DC3F5188AE6300FAEF32BF5BEEBA4BDD6E8E445A9184072096B713B", 25635768);
    public static readonly IReadOnlyList<LoaderFile> LoaderFiles = ReadFiles();
    private static LoaderFile[] ReadFiles()
    {
        using var stream = typeof(RequirementCatalog).Assembly.GetManifestResourceStream("ScheduleI.Manager.LoaderFiles.json") ?? throw new IOException("Loader verification catalog is missing.");
        return JsonSerializer.Deserialize<LoaderFile[]>(stream, JsonFiles.Options) ?? throw new IOException("Loader catalog is empty.");
    }
    public static LoaderStatus InspectLoader(GameInstallation game, CancellationToken cancellation = default) => InspectLoader(game, LoaderFiles, cancellation);
    internal static LoaderStatus InspectLoader(GameInstallation game, IReadOnlyList<LoaderFile> files, CancellationToken cancellation = default)
    {
        if (game.Backend != "IL2CPP" || !SystemPrerequisites.IsX64Pe(game.Executable)) return new(LoaderState.Blocked, "Automatic setup supports the Windows x64 IL2CPP game only.");
        ModLibrary.RejectLinks(game.Folder);
        if (game.LoaderVersion is not ("Missing" or "0.7.3")) return new(LoaderState.Blocked, $"MelonLoader {game.LoaderVersion} is already installed. Automatic setup will not replace a different loader version.");
        int missing = 0, existing = 0;
        foreach (var file in files)
        {
            cancellation.ThrowIfCancellationRequested();
            var target = Path.Combine(game.Folder, file.Path);
            ModLibrary.RejectLinks(Path.GetDirectoryName(target)!);
            if (!File.Exists(target)) { missing++; continue; }
            if ((File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0 || new FileInfo(target).Length != file.Size || !ModLibrary.Hash(target).Equals(file.Hash, StringComparison.OrdinalIgnoreCase))
                return new(LoaderState.Blocked, $"Existing loader file differs from the verified release: {file.Path}. Setup leaves it untouched; use the original loader installer to repair or remove it.");
            existing++;
        }
        if (missing == 0) return new(LoaderState.Ready, "MelonLoader 0.7.3 files verified.");
        if (existing == 0 && Directory.Exists(Path.Combine(game.Folder, "MelonLoader")) && Directory.EnumerateFileSystemEntries(Path.Combine(game.Folder, "MelonLoader")).Any())
            return new(LoaderState.Blocked, "An unrecognized MelonLoader folder exists. Setup leaves it untouched.");
        return existing == 0 ? new(LoaderState.Missing, "MelonLoader is not installed.", missing) : new(LoaderState.Repairable, $"MelonLoader 0.7.3 is missing {missing} file(s). Existing verified files will be preserved.", missing);
    }
    public static bool HasCriticalLoaderFiles(string folder) => new[]
    {
        "version.dll", "MelonLoader/net6/MelonLoader.dll", "MelonLoader/net6/MelonLoader.NativeHost.dll",
        "MelonLoader/net6/MelonLoader.runtimeconfig.json", "MelonLoader/Dependencies/SupportModules/Il2Cpp.dll"
    }.All(relative => File.Exists(Path.Combine(folder, relative)));
}
