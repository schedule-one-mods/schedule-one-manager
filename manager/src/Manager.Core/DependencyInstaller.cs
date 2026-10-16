using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;

namespace ScheduleI.Manager;

public static class DependencyInstaller
{
    public const string LoaderVersion = "0.7.3";
    public const string LoaderHash = "5B2B2F3D1CD42B59EC886C5BDC2663EDAE87A0097A4F4A8F58C0965A99DDA416";
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(10) };
    public static string Cache => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScheduleIModManager", "Downloads");
    public static async Task<string> Download(string url, string filename, string hash, bool sha512 = false)
    {
        Directory.CreateDirectory(Cache);
        var path = Path.Combine(Cache, filename);
        bool Valid(string file) { using var stream = File.OpenRead(file); return Convert.ToHexString(sha512 ? SHA512.HashData(stream) : SHA256.HashData(stream)).Equals(hash, StringComparison.OrdinalIgnoreCase); }
        if (File.Exists(path) && Valid(path)) return path;
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            await using (var output = File.Create(temp)) await response.Content.CopyToAsync(output);
            if (!Valid(temp)) throw new IOException("Downloaded file checksum did not match the pinned release.");
            File.Move(temp, path, true);
            return path;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static async Task InstallLoader(GameInstallation game)
    {
        GameDetector.RequireStopped(game);
        if (game.Backend != "IL2CPP" || game.Version != "0.4.7f5") throw new InvalidOperationException("This prototype provisions MelonLoader only for the tested IL2CPP game 0.4.7f5.");
        if (Directory.Exists(Path.Combine(game.Folder, "MelonLoader")) || File.Exists(Path.Combine(game.Folder, "version.dll"))) throw new IOException("An existing loader or version.dll is present. The prototype will not replace it. Use its original installer to repair it.");
        var archive = await Download("https://github.com/LavaGang/MelonLoader/releases/download/v0.7.3/MelonLoader.x64.zip", "MelonLoader.x64.0.7.3.zip", LoaderHash);
        InstallLoaderArchive(game, archive);
    }
    public static async Task InstallRuntime(GameInstallation game)
    {
        GameDetector.RequireStopped(game);
        var installer = await Download("https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/6.0.36/windowsdesktop-runtime-6.0.36-win-x64.exe", "windowsdesktop-runtime-6.0.36-win-x64.exe", "86fa63997e7e0dc6f3bf609e00880388dcf8d985c8f6417d07ebbbb1ecc957bf90214c8ff93f559a0e762b5626ba8c56c581f4d506aa4de7555f9792c2da254d", true);
        GameDetector.RequireStopped(game);
        using var process = Process.Start(new ProcessStartInfo(installer, "/install /passive /norestart") { UseShellExecute = true }) ?? throw new IOException("Could not start the runtime installer.");
        await process.WaitForExitAsync();
        if (process.ExitCode is not 0 and not 3010) throw new IOException("Runtime installer exited with code " + process.ExitCode);
    }
    public static void InstallLoaderArchive(GameInstallation game, string archive)
    {
        GameDetector.RequireStopped(game);
        ModLibrary.RejectLinks(game.Folder);
        if (ModLibrary.Hash(archive) != LoaderHash) throw new IOException("Loader archive checksum mismatch.");
        using var zip = ZipFile.OpenRead(archive);
        var files = zip.Entries.Where(e => !e.FullName.EndsWith('/')).ToArray();
        foreach (var entry in files)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (!(name == "version.dll" || name.StartsWith("MelonLoader/")) || name.Contains(':') || name.Split('/').Any(s => s is ".." or ".") || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new IOException("Unexpected loader archive path.");
            var target = Path.Combine(game.Folder, name);
            ModLibrary.RejectLinks(Path.GetDirectoryName(target)!);
            if (File.Exists(target)) throw new IOException("Loader installation would overwrite an existing file: " + name);
        }
        var created = new List<string>();
        try
        {
            foreach (var entry in files)
            {
                var target = Path.Combine(game.Folder, entry.FullName);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                created.Add(target);
                using var input = entry.Open();
                input.CopyTo(output);
            }
            JsonFiles.Write(Path.Combine(game.Folder, "UserData", "ScheduleIModManager", "loader-install.json"), created.Select(p => new { Path = Path.GetRelativePath(game.Folder, p), Sha256 = ModLibrary.Hash(p) }).ToArray());
        }
        catch { foreach (var path in created) if (File.Exists(path)) File.Delete(path); throw; }
    }
}
