using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace ScheduleI.Manager;

public static partial class ModLibrary
{
    public static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    public static void ValidateLibraryLocation(string library, string game)
    {
        var fullLibrary = Path.GetFullPath(library).TrimEnd(Path.DirectorySeparatorChar);
        var fullGame = Path.GetFullPath(game).TrimEnd(Path.DirectorySeparatorChar);
        if (fullLibrary.Equals(fullGame, StringComparison.OrdinalIgnoreCase) || fullLibrary.StartsWith(fullGame + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || fullGame.StartsWith(fullLibrary + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Choose a mod library outside the game installation, with neither folder inside the other.");
        RejectLinks(fullLibrary);
    }
    public static void RejectLinks(string path)
    {
        for (var item = new DirectoryInfo(Path.GetFullPath(path)); item != null; item = item.Parent)
            if (item.Exists && (item.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked folders are not supported for managed files: " + item.FullName);
    }
    public static LibraryPackage Read(string folder)
    {
        RejectLinks(folder);
        var manifest = JsonFiles.Read<PackageManifest>(Path.Combine(folder, "manifest.json"));
        var compat = JsonFiles.Read<Compatibility>(Path.Combine(folder, "compatibility.json"));
        if (!SafeName().IsMatch(manifest.Name) || !SafeVersion().IsMatch(manifest.Version)) throw new InvalidDataException("Invalid package name or version.");
        if (compat.Game != "Schedule I" || compat.GameVersions.Length == 0 || compat.Loader.Name != "MelonLoader" || !compat.Platforms.Contains("Windows x64")) throw new InvalidDataException("Missing Schedule I compatibility metadata.");
        var modFolder = Path.Combine(folder, "Mods");
        RejectLinks(modFolder);
        var dlls = Directory.GetFiles(modFolder, "*.dll", SearchOption.TopDirectoryOnly);
        if (dlls.Length != 1 || Directory.GetFiles(modFolder).Length != 1 || Directory.GetDirectories(modFolder).Length != 0) throw new InvalidDataException("Prototype packages must contain exactly one Mods/*.dll.");
        if ((File.GetAttributes(dlls[0]) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked DLLs are not supported.");
        using (var stream = File.OpenRead(dlls[0])) if (stream.ReadByte() != 'M' || stream.ReadByte() != 'Z') throw new InvalidDataException("Invalid DLL header.");
        return new(folder, manifest, compat, dlls[0], Hash(dlls[0]));
    }
    public static IReadOnlyList<LibraryPackage> List(string library)
    {
        RejectLinks(library);
        return !Directory.Exists(library) ? [] : Directory.EnumerateDirectories(library).Where(d => !Path.GetFileName(d).StartsWith('.')).Select(Read).OrderBy(p => p.Manifest.Name).ThenBy(p => p.Manifest.Version).ToArray();
    }
    public static LibraryPackage Import(string zipPath, string library)
    {
        RejectLinks(library);
        Directory.CreateDirectory(library);
        var stage = Path.Combine(library, ".import-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                long total = 0;
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in zip.Entries)
                {
                    var name = entry.FullName.Replace('\\', '/');
                    if (!names.Add(name) || name.Contains(':') || name.Split('/').Any(s => s is ".." or ".") || name.StartsWith('/') || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                        throw new InvalidDataException("Unsafe ZIP entry.");
                    if (name.EndsWith('/')) { if (name != "Mods/") throw new InvalidDataException("Unexpected package directory."); continue; }
                    var allowed = name is "manifest.json" or "compatibility.json" or "README.md" or "CHANGELOG.md" or "THIRD-PARTY-NOTICES.md" or "icon.png" || (name.StartsWith("Mods/") && name.Count(c => c == '/') == 1 && name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
                    if (!allowed || entry.Length > 32 * 1024 * 1024 || (total += entry.Length) > 64 * 1024 * 1024) throw new InvalidDataException("Unsupported package contents or size.");
                    var destination = Path.Combine(stage, name.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    entry.ExtractToFile(destination);
                }
            }
            var package = Read(stage);
            var destinationFolder = Path.Combine(library, package.Identity);
            if (Directory.Exists(destinationFolder))
            {
                var existing = Read(destinationFolder);
                if (existing.Sha256 != package.Sha256 || File.ReadAllText(Path.Combine(existing.Directory, "compatibility.json")) != File.ReadAllText(Path.Combine(stage, "compatibility.json"))) throw new IOException("That package version already exists with different contents. Publish a new version.");
                return existing;
            }
            Directory.Move(stage, destinationFolder);
            return Read(destinationFolder);
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }
    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_]{0,63}$")] private static partial Regex SafeName();
    [GeneratedRegex(@"^\d+\.\d+\.\d+$")] private static partial Regex SafeVersion();
}
