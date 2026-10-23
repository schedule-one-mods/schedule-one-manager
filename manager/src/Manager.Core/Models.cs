using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScheduleI.Manager;

public static class JsonFiles
{
    public static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private static void ValidatePath(string path)
    {
        ModLibrary.RejectLinks(Path.GetDirectoryName(Path.GetFullPath(path))!);
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked metadata files are not supported: " + path);
    }
    public static T Read<T>(string path)
    {
        ValidatePath(path);
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? throw new InvalidDataException($"Empty JSON: {path}");
    }
    public static void Write<T>(string path, T value)
    {
        ValidatePath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(value, Options)); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

public sealed class ManagerSettings
{
    public string GameFolder { get; set; } = "";
    public DateTimeOffset? DependencyRestartRequestedAt { get; set; }
    public string LibraryFolder { get; set; } = Path.Combine(ManagerPaths.Root, "Library");
    public static string FilePath => Path.Combine(ManagerPaths.Root, "settings.json");
}
public sealed record GameInstallation(string Folder, string Executable, string Version, string Backend, string LoaderVersion, bool RuntimeInstalled)
{
    public bool VisualCppInstalled { get; init; } = true;
    public bool LoaderHealthy { get; init; } = true;
}
public sealed class PackageManifest
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("version_number")] public string Version { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("dependencies")] public string[] Dependencies { get; set; } = [];
}
public sealed class Compatibility
{
    [JsonPropertyName("game")] public string Game { get; set; } = "";
    [JsonPropertyName("game_versions_tested")] public string[] GameVersions { get; set; } = [];
    [JsonPropertyName("scripting_backend")] public string Backend { get; set; } = "";
    [JsonPropertyName("loader")] public LoaderCompatibility Loader { get; set; } = new();
    [JsonPropertyName("platforms_tested")] public string[] Platforms { get; set; } = [];
}
public sealed class LoaderCompatibility
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("versions_tested")] public string[] Versions { get; set; } = [];
}
public sealed record LibraryPackage(string Directory, PackageManifest Manifest, Compatibility Compatibility, string DllPath, string Sha256)
{
    public string Identity => Manifest.Name + "-" + Manifest.Version;
    public string FileName => Path.GetFileName(DllPath);
}
public sealed record Deployment(string Package, string Hash);
