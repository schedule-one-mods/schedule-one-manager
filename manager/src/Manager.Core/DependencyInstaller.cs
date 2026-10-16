using System.IO.Compression;

namespace ScheduleI.Manager;

public static class DependencyInstaller
{
    public const string LoaderVersion = "0.7.3";
    public const string LoaderHash = "5B2B2F3D1CD42B59EC886C5BDC2663EDAE87A0097A4F4A8F58C0965A99DDA416";

    public static void InstallLoaderArchive(GameInstallation game, string archive, bool repairMissing = false, CancellationToken cancellation = default) =>
        InstallPayload(game, archive, RequirementCatalog.Loader, RequirementCatalog.LoaderFiles, repairMissing, cancellation);

    internal static void InstallPayload(GameInstallation game, string archive, DependencyArtifact artifact, IReadOnlyList<LoaderFile> files, bool repairMissing, CancellationToken cancellation)
    {
        GameDetector.RequireStopped(game);
        ModLibrary.RejectLinks(game.Folder);
        if (new FileInfo(archive).Length != artifact.Size || ModLibrary.Hash(archive) != artifact.Hash.ToUpperInvariant()) throw new IOException("Loader archive checksum mismatch.");
        var expected = files.ToDictionary(f => f.Path, StringComparer.Ordinal);
        using var zip = ZipFile.OpenRead(archive);
        var entries = zip.Entries.Where(e => !e.FullName.EndsWith('/')).ToArray();
        if (entries.Length != files.Count || entries.Select(e => e.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Length) throw new InvalidDataException("Unexpected loader archive entries.");
        foreach (var entry in entries)
        {
            cancellation.ThrowIfCancellationRequested();
            var name = entry.FullName;
            if (!(name == "version.dll" || name.StartsWith("MelonLoader/")) || name.Contains(':') || name.Contains('\\') || name.Split('/').Any(s => s is ".." or ".") || !expected.TryGetValue(name, out var definition) || entry.Length != definition.Size || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new IOException("Unexpected loader archive path or size.");
            var target = Path.Combine(game.Folder, name);
            ModLibrary.RejectLinks(Path.GetDirectoryName(target)!);
            if (File.Exists(target) && (!repairMissing || (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0 || !ModLibrary.Hash(target).Equals(definition.Hash, StringComparison.OrdinalIgnoreCase)))
                throw new IOException("Loader setup would replace an existing or changed file: " + name);
        }
        var created = new List<string>();
        var directories = new List<string>();
        void CreateDirectory(string path)
        {
            var missing = new Stack<string>();
            for (var directory = Path.GetFullPath(path); !Directory.Exists(directory); directory = Path.GetDirectoryName(directory)!)
            {
                if (!directory.StartsWith(Path.GetFullPath(game.Folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Loader directory escaped the game folder.");
                missing.Push(directory);
            }
            while (missing.TryPop(out var directory)) { Directory.CreateDirectory(directory); directories.Add(directory); }
        }
        try
        {
            foreach (var entry in entries)
            {
                cancellation.ThrowIfCancellationRequested();
                var target = Path.Combine(game.Folder, entry.FullName);
                if (File.Exists(target)) continue; // Only verified existing files reached this point.
                CreateDirectory(Path.GetDirectoryName(target)!);
                using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    created.Add(target);
                    using var input = entry.Open();
                    input.CopyTo(output);
                }
                if (!ModLibrary.Hash(target).Equals(expected[entry.FullName].Hash, StringComparison.OrdinalIgnoreCase)) throw new IOException("Extracted loader file verification failed: " + entry.FullName);
            }
            cancellation.ThrowIfCancellationRequested();
            ModLibrary.RejectLinks(Path.Combine(game.Folder, "Mods"));
            CreateDirectory(Path.Combine(game.Folder, "Mods"));
            CreateDirectory(Path.Combine(game.Folder, "UserData", "ScheduleIModManager"));
            JsonFiles.Write(Path.Combine(game.Folder, "UserData", "ScheduleIModManager", "loader-install.json"), files.Select(f => new { Path = f.Path, Sha256 = f.Hash }).ToArray());
        }
        catch
        {
            foreach (var path in created) if (File.Exists(path)) File.Delete(path);
            foreach (var directory in directories.OrderByDescending(p => p.Length))
                if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
            throw;
        }
    }
}
