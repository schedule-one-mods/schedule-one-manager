namespace ScheduleI.Manager;

public static class ModDeployment
{
    private static string ReceiptPath(GameInstallation game) => Path.Combine(game.Folder, "UserData", "ScheduleIModManager", "deployments.json");
    private static Dictionary<string, Deployment> Receipts(GameInstallation game) => File.Exists(ReceiptPath(game)) ? JsonFiles.Read<Dictionary<string, Deployment>>(ReceiptPath(game)) : new(StringComparer.OrdinalIgnoreCase);
    private static string Target(GameInstallation game, LibraryPackage package)
    {
        ModLibrary.RejectLinks(game.Folder);
        ModLibrary.RejectLinks(Path.Combine(game.Folder, "Mods"));
        ModLibrary.RejectLinks(Path.GetDirectoryName(ReceiptPath(game))!);
        var target = Path.Combine(game.Folder, "Mods", package.FileName);
        if (File.Exists(target) && (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked mod files are not supported.");
        return target;
    }
    public static string CompatibilityIssue(GameInstallation game, LibraryPackage package)
    {
        if (game.Backend != package.Compatibility.Backend) return $"Requires {package.Compatibility.Backend}; game is {game.Backend}.";
        if (!package.Compatibility.GameVersions.Contains(game.Version)) return $"Game {game.Version} is unverified. Tested: {string.Join(", ", package.Compatibility.GameVersions)}.";
        if (!package.Compatibility.Loader.Versions.Contains(game.LoaderVersion)) return $"Requires MelonLoader {string.Join(", ", package.Compatibility.Loader.Versions)}; found {game.LoaderVersion}.";
        if (!File.Exists(Path.Combine(game.Folder, "version.dll"))) return "MelonLoader bootstrap version.dll is missing.";
        if (!game.LoaderHealthy) return "MelonLoader is incomplete. Check requirements before enabling mods.";
        if (!game.RuntimeInstalled) return ".NET 6 Desktop Runtime (x64) is missing.";
        if (!game.VisualCppInstalled) return "Visual C++ x64 Runtime is missing. Install requirements before enabling mods.";
        if (package.Manifest.Dependencies.Any(d => !package.Compatibility.Loader.Versions.Select(v => "LavaGang-MelonLoader-" + v).Contains(d))) return "This package has unsupported additional dependencies.";
        return "";
    }
    public static string Status(GameInstallation game, LibraryPackage package)
    {
        var target = Target(game, package);
        var receipts = Receipts(game);
        if (!File.Exists(target)) return "Disabled";
        var hash = ModLibrary.Hash(target);
        if (hash != package.Sha256) return "Another version / file conflict";
        return receipts.TryGetValue(package.FileName, out var item) && item.Package == package.Identity && item.Hash == hash ? "Enabled" : "Installed externally — adopt to manage";
    }
    public static void Enable(GameInstallation game, LibraryPackage package)
    {
        GameDetector.RequireStopped(game);
        ModLibrary.ValidateLibraryLocation(package.Directory, game.Folder);
        var issue = CompatibilityIssue(game, package);
        if (issue.Length > 0) throw new InvalidOperationException(issue);
        if (ModLibrary.Hash(package.DllPath) != package.Sha256) throw new IOException("Library DLL changed; refresh the library.");
        var target = Target(game, package);
        var receipts = Receipts(game);
        if (File.Exists(target) && ModLibrary.Hash(target) != package.Sha256) throw new IOException("A different DLL already occupies " + target + ". Disable its managed version first; external files are never overwritten.");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var created = false;
        var stage = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            if (!File.Exists(target))
            {
                File.Copy(package.DllPath, stage);
                if (ModLibrary.Hash(stage) != package.Sha256) throw new IOException("Copy verification failed.");
                File.Move(stage, target);
                created = true;
            }
            receipts[package.FileName] = new(package.Identity, package.Sha256);
            JsonFiles.Write(ReceiptPath(game), receipts);
        }
        catch { if (created && File.Exists(target) && ModLibrary.Hash(target) == package.Sha256) File.Delete(target); throw; }
        finally { if (File.Exists(stage)) File.Delete(stage); }
    }
    public static void Disable(GameInstallation game, LibraryPackage package)
    {
        GameDetector.RequireStopped(game);
        var target = Target(game, package);
        var receipts = Receipts(game);
        if (!receipts.TryGetValue(package.FileName, out var item) || item.Package != package.Identity || item.Hash != package.Sha256) throw new IOException("This file is not managed by this package. Adopt an identical installed copy first.");
        if (File.Exists(target) && ModLibrary.Hash(target) != item.Hash) throw new IOException("Installed DLL was changed externally; leaving it untouched.");
        // Move out of Mods first. Restore it if committing the ownership receipt fails.
        var backup = target + "." + Guid.NewGuid().ToString("N") + ".disabled";
        var moved = File.Exists(target);
        if (moved) File.Move(target, backup);
        try { receipts.Remove(package.FileName); JsonFiles.Write(ReceiptPath(game), receipts); }
        catch { if (moved) File.Move(backup, target); throw; }
        if (moved) File.Delete(backup);
    }
    public static void ValidateLaunch(GameInstallation game, IEnumerable<LibraryPackage> packages)
    {
        var receipts = Receipts(game);
        var list = packages.ToArray();
        foreach (var (file, receipt) in receipts)
        {
            var package = list.FirstOrDefault(p => p.Identity == receipt.Package && p.FileName == file && p.Sha256 == receipt.Hash) ?? throw new InvalidOperationException("Restore the managed package to the library before launch: " + receipt.Package);
            if (Status(game, package) != "Enabled") throw new InvalidOperationException("Managed mod is missing or changed: " + receipt.Package);
            var issue = CompatibilityIssue(game, package);
            if (issue.Length > 0) throw new InvalidOperationException(package.Manifest.Name + ": " + issue + " Disable it before launching this game version.");
        }
    }
}
