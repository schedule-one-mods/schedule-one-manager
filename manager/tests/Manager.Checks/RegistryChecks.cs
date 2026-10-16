using System.Net;
using System.Text;
using System.Text.Json;
using ScheduleI.Manager;

internal static class RegistryChecks
{
    public static async Task Run(string fixtureZip, string root, Action<bool, string> check)
    {
        var bytes = File.ReadAllBytes(fixtureZip);
        RegistryMod Entry() => new()
        {
            Id = "fixture-mod", Name = "Fixture Mod", Author = "Test Author", Summary = "Test fixture",
            Repository = "https://github.com/test-author/test-mod", Version = "0.0.1", PackageName = "FixtureMod",
            DownloadUrl = "https://github.com/test-author/test-mod/releases/download/v0.0.1/FixtureMod.zip",
            Sha256 = ModLibrary.Hash(fixtureZip).ToLowerInvariant(), SizeBytes = bytes.Length,
            Compatibility = new() { Game = "Schedule I", GameVersions = ["0.4.7f5"], Backend = "IL2CPP", Platforms = ["Windows x64"], Loader = new() { Name = "MelonLoader", Versions = ["0.7.3"] } }
        };
        var entry = Entry();
        string Index(params RegistryMod[] mods) => JsonSerializer.Serialize(new RegistryDocument { SchemaVersion = 1, Mods = mods.ToList() }, JsonFiles.Options);
        async Task Reject(Func<Task> action, string name)
        {
            try { await action(); }
            catch (Exception exception) when (exception is InvalidDataException or IOException or HttpRequestException or OperationCanceledException) { check(true, name); return; }
            throw new Exception("Expected registry rejection: " + name);
        }
        RegistryClient Client(string index, byte[]? payload = null, HttpStatusCode status = HttpStatusCode.OK) => new(new HttpClient(new StubHandler(request => new(status)
        {
            RequestMessage = request,
            Content = request.RequestUri!.AbsoluteUri == RegistryClient.IndexUrl ? new StringContent(index, Encoding.UTF8, "application/json") : new ByteArrayContent(payload ?? bytes)
        })));
        using (var client = Client(Index(entry)))
        {
            var list = await client.Fetch();
            check(list.Count == 1 && list[0].Id == entry.Id, "registry v1 fetch and parse");
            var library = Path.Combine(root, "RegistryLibrary");
            var package = await client.Download(list[0], library);
            check(package.Manifest.Name == "FixtureMod" && package.Manifest.Version == "0.0.1", "download verifies ZIP then imports into library");
            await client.Download(entry, library);
            check(ModLibrary.List(library).Count == 1, "repeat registry download is idempotent");
        }
        using (var client = Client("{\"schema_version\":2,\"mods\":[]}")) await Reject(async () => await client.Fetch(), "reject unsupported registry schema");
        using (var client = Client("invalid")) await Reject(async () => await client.Fetch(), "reject malformed registry JSON");
        using (var client = new RegistryClient(new HttpClient(new StubHandler(request => new(request.RequestUri!.AbsoluteUri == RegistryClient.IndexUrl ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
        {
            RequestMessage = request, Content = new StringContent(Index(entry))
        })))) check((await client.Fetch()).Single().Id == entry.Id, "fallback reads the same registry file when Contents API is unavailable");
        using (var client = new RegistryClient(new HttpClient(new StubHandler(request => new(HttpStatusCode.OK)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://insecure.example/index.json"), Content = new StringContent(Index(entry))
        })))) await Reject(async () => await client.Fetch(), "reject HTTPS downgrade on registry redirect");
        using (var client = Client(Index(entry, entry))) await Reject(async () => await client.Fetch(), "reject duplicate registry ids");
        var duplicateName = Entry(); duplicateName.Id = "different-id";
        using (var client = Client(Index(entry, duplicateName))) await Reject(async () => await client.Fetch(), "reject duplicate package names");
        var wrongUrl = Entry(); wrongUrl.DownloadUrl = "https://github.com/another/repo/releases/download/v0.0.1/FixtureMod.zip";
        using (var client = Client(Index(wrongUrl))) await Reject(async () => await client.Fetch(), "reject package URL outside registered repository");
        var wrongTag = Entry(); wrongTag.DownloadUrl = wrongTag.DownloadUrl.Replace("v0.0.1", "v0.0.2");
        using (var client = Client(Index(wrongTag))) await Reject(async () => await client.Fetch(), "reject package URL with mismatched version tag");
        var http = Entry(); http.DownloadUrl = http.DownloadUrl.Replace("https:", "http:");
        using (var client = Client(Index(http))) await Reject(async () => await client.Fetch(), "reject non-HTTPS package URL");
        var traversal = Entry(); traversal.DownloadUrl = traversal.DownloadUrl.Replace("FixtureMod.zip", "../FixtureMod.zip");
        using (var client = Client(Index(traversal))) await Reject(async () => await client.Fetch(), "reject normalized traversal in release URL");
        var wrongHash = Entry(); wrongHash.Sha256 = new string('0', 64);
        var failedLibrary = Path.Combine(root, "RejectedRegistryLibrary");
        using (var client = Client(Index(wrongHash))) await Reject(async () => await client.Download(wrongHash, failedLibrary), "reject archive SHA-256 mismatch");
        var wrongSize = Entry(); wrongSize.SizeBytes++;
        using (var client = Client(Index(wrongSize))) await Reject(async () => await client.Download(wrongSize, failedLibrary), "reject archive size mismatch");
        var wrongIdentity = Entry(); wrongIdentity.PackageName = "OtherMod";
        using (var client = Client(Index(wrongIdentity))) await Reject(async () => await client.Download(wrongIdentity, failedLibrary), "reject mismatched package manifest before library commit");
        var wrongCompatibility = Entry(); wrongCompatibility.Compatibility.GameVersions = ["0.4.8f1"];
        using (var client = Client(Index(wrongCompatibility))) await Reject(async () => await client.Download(wrongCompatibility, failedLibrary), "reject package compatibility differing from registry");
        using (var client = Client(Index(entry), status: HttpStatusCode.NotFound)) await Reject(async () => await client.Fetch(), "report unavailable registry HTTP response");
        using (var client = Client(Index(entry)))
        {
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            await Reject(async () => await client.Download(entry, failedLibrary, cancellation.Token), "cancelled download does not install a mod");
        }
        check(!Directory.EnumerateFileSystemEntries(failedLibrary).Any(), "rejected downloads leave no package or temporary payload");
        using (var client = Client(new string(' ', 1024 * 1024 + 1))) await Reject(async () => await client.Fetch(), "reject oversized registry response");
    }
    public static async Task RemoteSmoke()
    {
        var root = Path.Combine(Path.GetTempPath(), "ScheduleI-remote-registry-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var client = new RegistryClient();
            var mods = await client.Fetch();
            var mod = mods.Single(m => m.Id == "businesses");
            var package = await client.Download(mod, Path.Combine(root, "Library"));
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
        var package = await registry.Download(mod, library);
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
