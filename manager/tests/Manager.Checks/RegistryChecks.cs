using System.Net;
using System.Text;
using System.Text.Json;
using ScheduleI.Manager;

internal static class RegistryChecks
{
    public static async Task Run(string fixtureZip, string root, Action<bool, string> check)
    {
        var bytes = File.ReadAllBytes(fixtureZip);
        RegistryMod Entry() => new() { Id = "fixture-mod", Name = "Fixture Mod", Author = "Test Author", Summary = "A test package.",
            Repository = "https://github.com/test-author/test-mod", PackageName = "FixtureMod" };
        var compatibility = new Compatibility { Game = "Schedule I", GameVersions = ["0.4.7f5"], Backend = "IL2CPP", Platforms = ["Windows x64"], Loader = new() { Name = "MelonLoader", Versions = ["0.7.3"] } };
        string Index(params RegistryMod[] mods) => JsonSerializer.Serialize(new RegistryDocument { SchemaVersion = 2, Mods = mods.ToList() }, JsonFiles.Options);
        object Release(string version, bool prerelease = false, string? url = null, string? digest = null) => new { tag_name = "v" + version, draft = false, prerelease,
            assets = new[] { new { name = "FixtureMod-" + version + "-IL2CPP.zip", size = bytes.Length, digest = digest ?? "sha256:" + ModLibrary.Hash(fixtureZip).ToLowerInvariant(),
                browser_download_url = url ?? "https://github.com/test-author/test-mod/releases/download/v" + version + "/FixtureMod-" + version + "-IL2CPP.zip" } } };
        async Task Reject(Func<Task> action, string name)
        {
            try { await action(); }
            catch (Exception exception) when (exception is InvalidDataException or IOException or HttpRequestException or OperationCanceledException) { check(true, name); return; }
            throw new Exception("Expected registry rejection: " + name);
        }
        RegistryClient Client(string index, object[]? releases = null, byte[]? payload = null, string latest = "v0.0.1", string? compatibilityJson = null) => new(new HttpClient(new StubHandler(request =>
        {
            var url = request.RequestUri!.AbsoluteUri;
            var text = url == RegistryClient.IndexUrl || url == RegistryClient.RawIndexUrl ? index :
                url.EndsWith("/latest") ? JsonSerializer.Serialize(new { tag_name = latest }) :
                url.Contains("/releases?") ? JsonSerializer.Serialize(url.Contains("page=2") ? Array.Empty<object>() : releases ?? [Release("0.0.1")]) :
                url.Contains("raw.githubusercontent") ? compatibilityJson ?? JsonSerializer.Serialize(compatibility, JsonFiles.Options) : null;
            return new(HttpStatusCode.OK) { RequestMessage = request, Content = text != null ? new StringContent(text) : new ByteArrayContent(payload ?? bytes) };
        })));
        RegistryRelease entry;
        using (var client = Client(Index(Entry()), [Release("0.0.1"), Release("0.0.2", true)]))
        {
            var list = await client.Fetch();
            check(list.Count == 1 && list[0].Versions.Count == 2, "v2 groups released versions under one mod");
            entry = list[0].Versions.Single(v => v.IsLatest);
            check(entry.Version == "0.0.1" && list[0].Versions[0].Prerelease, "publisher latest is independent of numeric order and prereleases");
            check(entry.Compatibility.GameVersions.Single() == "0.4.7f5", "compatibility is read from each release tag");
            var library = Path.Combine(root, "RegistryLibrary");
            var package = await client.Download(entry, library);
            check(package.Manifest.Version == "0.0.1", "release digest and package metadata verified before import");
            await client.Download(entry, library);
            check(ModLibrary.List(library).Count == 1, "repeated version download is idempotent");
        }
        using (var client = Client("{\"schema_version\":1,\"mods\":[]}")) await Reject(async () => await client.Fetch(), "v1 is rejected without a compatibility fallback");
        using (var client = Client("invalid")) await Reject(async () => await client.Fetch(), "reject malformed registry JSON");
        using (var client = Client(Index(Entry(), Entry()))) await Reject(async () => await client.Fetch(), "reject duplicate mod identities");
        var sameName = Entry(); sameName.Id = "other-mod";
        using (var client = Client(Index(Entry(), sameName))) await Reject(async () => await client.Fetch(), "reject duplicate package names");
        var badSource = Entry(); badSource.ReleaseSource.CompatibilityPath = "../bad.json";
        using (var client = Client(Index(badSource))) await Reject(async () => await client.Fetch(), "reject unsafe tagged metadata path");
        using (var client = Client(Index(Entry()), [Release("0.0.1", url: "https://github.com/other/repo/releases/download/v0.0.1/FixtureMod.zip")]))
        {
            var mod = (await client.Fetch()).Single();
            check(mod.Versions.Count == 0 && mod.ReleaseError != null, "cross-repository asset is unavailable with an explanation");
        }
        using (var client = Client(Index(Entry()), [Release("0.0.1", digest: "")]))
            check((await client.Fetch()).Single().ReleaseError != null, "missing release digest cannot bypass verification");
        using (var client = Client(Index(Entry()), [Release("0.0.1")], latest: "v0.0.9"))
        {
            var mod = (await client.Fetch()).Single();
            check(!mod.Versions.Any(v => v.IsLatest) && mod.ReleaseError!.Contains("latest"), "missing latest never silently selects an older release");
        }
        using (var client = Client(Index(Entry()), compatibilityJson: "{}"))
            check((await client.Fetch()).Single().Versions.Count == 0, "invalid tagged compatibility cannot become a downloadable release");
        using (var client = Client(Index(Entry()), Enumerable.Range(1, 100).Select(n => Release("0.0." + n)).ToArray()))
            check((await client.Fetch()).Single().Versions.Count == 100, "release discovery follows pagination through a full page");
        using (var client = Client(Index(Entry()), [Release("0.0.1"), Release("0.0.2", digest: "invalid")]))
        {
            var mod = (await client.Fetch()).Single();
            check(mod.Versions.Count == 1 && mod.Versions[0].IsLatest && mod.ReleaseError != null, "one invalid release preserves other valid versions with a warning");
        }
        using (var client = Client(Index(Entry()).Replace("\"release_source\"", "\"unknown_source\"")))
            await Reject(async () => await client.Fetch(), "strict v2 metadata rejects unknown or missing discovery fields");
        var failedLibrary = Path.Combine(root, "RejectedRegistryLibrary");
        using (var client = Client(Index(Entry()), payload: bytes.Concat(new byte[] { 0 }).ToArray()))
            await Reject(async () => await client.Download(entry, failedLibrary), "reject download size mismatch");
        var originalHash = entry.Sha256; entry.Sha256 = new string('0', 64);
        using (var client = Client(Index(Entry()))) await Reject(async () => await client.Download(entry, failedLibrary), "reject SHA-256 mismatch");
        entry.Sha256 = originalHash;
        entry.Compatibility.GameVersions = ["0.4.8f1"];
        using (var client = Client(Index(Entry()))) await Reject(async () => await client.Download(entry, failedLibrary), "reject package compatibility differing from tagged metadata");
        entry.Compatibility.GameVersions = ["0.4.7f5"];
        using (var client = Client(Index(Entry())))
        {
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            await Reject(async () => await client.Download(entry, failedLibrary, cancellation.Token), "cancelled downloads leave no installation");
        }
        check(!Directory.EnumerateFileSystemEntries(failedLibrary).Any(), "failed downloads leave no package or temporary file");
        using (var client = Client(new string(' ', 1024 * 1024 + 1))) await Reject(async () => await client.Fetch(), "reject oversized registry response");
    }
    public static async Task LocalCatalogSmoke(string indexPath)
    {
        using var http = new HttpClient(new LocalIndexHandler(File.ReadAllText(indexPath))) { Timeout = TimeSpan.FromSeconds(90) };
        using var registry = new RegistryClient(http);
        var mods = await registry.Fetch();
        var root = Path.Combine(Path.GetTempPath(), "ScheduleI-v2-smoke-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var mod in mods)
            {
                if (mod.ReleaseError != null) throw new Exception(mod.Name + ": " + mod.ReleaseError);
                if (!mod.Versions.Any(v => v.IsLatest)) throw new Exception("No latest release for " + mod.Name);
                Console.WriteLine($"PASS live releases: {mod.Name}: {string.Join(", ", mod.Versions.Select(v => v.Label))}");
                foreach (var version in mod.Versions)
                {
                    var package = await registry.Download(version, Path.Combine(root, "Library"));
                    Console.WriteLine("PASS live verified package and tagged compatibility: " + package.Identity);
                }
            }
            if (mods.Single(m => m.Id == "businesses").Versions.Count < 2) throw new Exception("Businesses history was not discovered.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private sealed class LocalIndexHandler(string index) : DelegatingHandler(new HttpClientHandler())
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            request.RequestUri!.AbsoluteUri == RegistryClient.IndexUrl
                ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = new StringContent(index) })
                : base.SendAsync(request, cancellationToken);
    }
    public static async Task RemoteSmoke()
    {
        var root = Path.Combine(Path.GetTempPath(), "ScheduleI-remote-registry-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var client = new RegistryClient();
            var mods = await client.Fetch();
            var mod = mods.Single(m => m.Id == "businesses");
            var package = await client.Download(mod.Versions.Single(v => v.IsLatest), Path.Combine(root, "Library"));
            Console.WriteLine($"PASS live index -> {mod.Repository} -> verified {package.Identity}, DLL {package.Sha256}");
            var folder = Path.Combine(root, "GameFixture");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "version.dll"), "fixture");
            var game = new GameInstallation(folder, Path.Combine(folder, "Schedule I.exe"), "0.4.7f5", "IL2CPP", "0.7.3", true);
            ModDeployment.Enable(game, package);
            ModDeployment.ValidateLaunch(game, [package]);
            if (ModDeployment.Status(game, package) != "Enabled") throw new Exception("Remote package enable failed.");
            ModDeployment.Disable(game, package);
            if (ModDeployment.Status(game, package) != "Disabled") throw new Exception("Remote package disable failed.");
            Console.WriteLine("PASS live downloaded mod enable/disable and launch validation in isolated installation fixture");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    public static async Task RealGameRoundTrip(string gameFolder, string library)
    {
        using var registry = new RegistryClient();
        var game = GameDetector.Detect(gameFolder);
        GameDetector.RequireStopped(game);
        ModLibrary.ValidateLibraryLocation(library, game.Folder);
        var mod = (await registry.Fetch()).Single(m => m.Id == "businesses");
        var package = await registry.Download(mod.Versions.Single(v => v.IsLatest), library);
        var target = Path.Combine(game.Folder, "Mods", package.FileName);
        if (!File.Exists(target) || ModLibrary.Hash(target) != package.Sha256 || ModDeployment.Status(game, package) != "Enabled")
            throw new InvalidOperationException("Real round trip requires the already managed identical Businesses DLL. No game files changed.");
        try
        {
            ModDeployment.Disable(game, package);
            if (File.Exists(target)) throw new Exception("Real game disable failed.");
            Console.WriteLine("PASS real game: disabled the managed mod downloaded from the registry");
        }
        finally { ModDeployment.Enable(game, package); }
        ModDeployment.ValidateLaunch(game, [package]);
        if (ModLibrary.Hash(target) != package.Sha256) throw new Exception("Restored real mod checksum mismatch.");
        Console.WriteLine("PASS real game: re-enabled identical remote mod and validated launch; no game launched by this command");
    }
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(send(request));
        }
    }
}
