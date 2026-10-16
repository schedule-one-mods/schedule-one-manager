using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using ScheduleI.Manager;

internal static class DependencyChecks
{
    public static async Task Run(string root, Action<bool, string> check)
    {
        var installerPath = Path.Combine(root, "Downloads with spaces", "desktop.exe");
        var installerLog = Path.Combine(root, "Logs with spaces", "native installer.log");
        var launch = DependencySetupService.MicrosoftInstallerStartInfo(installerPath, installerLog);
        check(!launch.UseShellExecute && string.IsNullOrEmpty(launch.Verb) && launch.FileName == installerPath && launch.WorkingDirectory == Path.GetDirectoryName(installerPath), "Microsoft bootstrapper starts directly from its download directory and handles its own elevation");
        check(launch.ArgumentList.SequenceEqual(new[] { "/install", "/passive", "/norestart", "/log", installerLog }), "Microsoft installer arguments preserve log paths with spaces and prevent automatic reboot");
        var logFolder = Path.Combine(root, "Viewer logs");
        Directory.CreateDirectory(logFolder);
        var mainLog = Path.Combine(logFolder, "setup-20261016-232140-166d4f79.log");
        var childLog = Path.Combine(logFolder, "setup-20261016-232140-166d4f79.net6_000_runtime.msi.log");
        File.WriteAllText(mainLog, "setup status");
        File.WriteAllText(childLog, "native details");
        check(SetupLogs.Latest(logFolder) == mainLog, "reopened manager selects the setup log instead of a native MSI child log");
        check(SetupLogs.Related(mainLog).Order().SequenceEqual(new[] { mainLog, childLog }.Order()), "log viewer includes the setup and native installer logs for its session");
        using (var activeLog = new FileStream(mainLog, FileMode.Open, FileAccess.Write, FileShare.Read))
            check(SetupLogs.Read(mainLog) == "setup status", "log viewer can read a setup log while installation still has it open");
        async Task<Exception> Reject(Func<Task> action, string name)
        {
            try { await action(); }
            catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException or OperationCanceledException) { check(true, name); return exception; }
            throw new Exception("Expected dependency rejection: " + name);
        }
        var runtimeRoot = Path.Combine(root, "RuntimeProbe");
        Directory.CreateDirectory(runtimeRoot);
        check(!SystemPrerequisites.IsRuntimeRootReady(runtimeRoot), "empty runtime directory is not an installed runtime");
        CreatePe(Path.Combine(runtimeRoot, "dotnet.exe"));
        foreach (var path in new[] { "shared/Microsoft.NETCore.App/6.0.36/coreclr.dll", "shared/Microsoft.NETCore.App/6.0.36/System.Private.CoreLib.dll", "shared/Microsoft.WindowsDesktop.App/6.0.36/PresentationFramework.dll", "shared/Microsoft.WindowsDesktop.App/6.0.36/WindowsBase.dll", "host/fxr/6.0.36/hostfxr.dll" }) CreatePe(Path.Combine(runtimeRoot, path));
        check(SystemPrerequisites.IsRuntimeRootReady(runtimeRoot), "x64 desktop, core and host components satisfy runtime detection");
        File.Delete(Path.Combine(runtimeRoot, "shared/Microsoft.NETCore.App/6.0.36/coreclr.dll"));
        check(!SystemPrerequisites.IsRuntimeRootReady(runtimeRoot), "desktop folder without core runtime is rejected");
        CreatePe(Path.Combine(runtimeRoot, "shared/Microsoft.NETCore.App/6.0.36/coreclr.dll"));
        Directory.Move(Path.Combine(runtimeRoot, "shared/Microsoft.WindowsDesktop.App/6.0.36"), Path.Combine(runtimeRoot, "shared/Microsoft.WindowsDesktop.App/6.0.10"));
        check(!SystemPrerequisites.IsRuntimeRootReady(runtimeRoot), "older dotnet 6 patch requires the pinned runtime update");
        Directory.Move(Path.Combine(runtimeRoot, "shared/Microsoft.WindowsDesktop.App/6.0.10"), Path.Combine(runtimeRoot, "shared/Microsoft.WindowsDesktop.App/8.0.36"));
        check(!SystemPrerequisites.IsRuntimeRootReady(runtimeRoot), "dotnet 8 desktop does not replace dotnet 6 desktop");
        Directory.Move(Path.Combine(runtimeRoot, "shared/Microsoft.WindowsDesktop.App/8.0.36"), Path.Combine(runtimeRoot, "shared/Microsoft.WindowsDesktop.App/6.0.36"));
        CreatePe(Path.Combine(runtimeRoot, "dotnet.exe"), x64: false);
        check(!SystemPrerequisites.IsRuntimeRootReady(runtimeRoot), "x86 host cannot satisfy x64 runtime requirement");
        check(!SystemPrerequisites.RestartStillRequired(null) && !SystemPrerequisites.RestartStillRequired(DateTimeOffset.MinValue), "restart notice clears when no request exists or Windows booted afterwards");
        check(SystemPrerequisites.RestartStillRequired(DateTimeOffset.UtcNow), "restart notice remains required within the same Windows session");
        using (var fixture = new Fixture(root))
        {
            var initial = await fixture.Service.Inspect(fixture.Folder);
            check(initial.CanSetup && !initial.Ready && initial.Loader.State == LoaderState.Missing, "fresh game identifies all missing prerequisites");
            var messages = new List<SetupProgress>();
            var result = await fixture.Service.Setup(fixture.Folder, new Reporter(messages.Add));
            check(result.Status.Ready && !result.RestartRequired, "one setup installs and verifies all missing dependencies");
            check(fixture.InstallerCalls.SequenceEqual(new[] { fixture.Vc.FileName, fixture.Dotnet.FileName }), "Visual C++ installs before dotnet and loader");
            check(Directory.Exists(Path.Combine(fixture.Folder, "Mods")) && File.Exists(result.LogPath), "setup creates Mods folder and persistent setup log");
            check(messages.Any(m => !m.CanCancel) && messages.Any(m => m.Percent == 100), "installer phase and download progress are reported");
            var requests = fixture.Requests;
            await fixture.Service.Setup(fixture.Folder);
            check(fixture.Requests == requests && fixture.InstallerCalls.Count == 2, "ready setup performs no downloads or reinstall");
            var bootstrap = Path.Combine(fixture.Folder, "version.dll");
            var hash = ModLibrary.Hash(bootstrap);
            File.Delete(Path.Combine(fixture.Folder, "MelonLoader/net6/MelonLoader.dll"));
            check((await fixture.Service.Inspect(fixture.Folder)).Loader.State == LoaderState.Repairable, "partial known loader is offered missing-file repair");
            await fixture.Service.Setup(fixture.Folder);
            check(ModLibrary.Hash(bootstrap) == hash && fixture.Requests == requests, "repair preserves existing loader bytes and uses verified cache");
            File.WriteAllText(bootstrap, "foreign bootstrap");
            check((await fixture.Service.Inspect(fixture.Folder)).Loader.State == LoaderState.Blocked, "modified bootstrap blocks automatic setup");
            await Reject(async () => await fixture.Service.Setup(fixture.Folder), "setup refuses to overwrite a changed existing loader");
            check(File.ReadAllText(bootstrap) == "foreign bootstrap", "conflicting bootstrap remains intact");
        }
        using (var fixture = new Fixture(root))
        {
            fixture.LoaderVersionOverride = "0.7.4";
            await Reject(async () => await fixture.Service.Setup(fixture.Folder), "setup never downgrades a different loader version");
            check(fixture.Requests == 0 && fixture.InstallerCalls.Count == 0, "conflict preflight runs before installers and downloads");
        }
        using (var fixture = new Fixture(root))
        {
            CreatePe(Path.Combine(fixture.Folder, "Schedule I.exe"), false);
            check((await fixture.Service.Inspect(fixture.Folder)).Loader.State == LoaderState.Blocked, "automatic setup rejects an x86 game");
        }
        using (var fixture = new Fixture(root))
        {
            var archive = await fixture.Downloader.Get(fixture.Loader);
            var changedCatalog = fixture.Definitions.Select(f => f.Path.StartsWith("MelonLoader/") ? f with { Hash = new string('0', 64) } : f).ToArray();
            await Reject(() => Task.Run(() => DependencyInstaller.InstallPayload(fixture.Game(), archive, fixture.Loader, changedCatalog, true, default)), "failed extraction verification rolls back newly created loader files");
            check(!Directory.Exists(Path.Combine(fixture.Folder, "MelonLoader")) && !File.Exists(Path.Combine(fixture.Folder, "version.dll")), "rollback removes only its empty new directories so setup can retry");
            check((await fixture.Service.Setup(fixture.Folder)).Status.Ready, "setup retries successfully after extraction rollback");
        }
        using (var fixture = new Fixture(root))
        {
            var first = await fixture.Downloader.Get(fixture.Dotnet);
            var requests = fixture.Requests;
            await fixture.Downloader.Get(fixture.Dotnet);
            check(fixture.Requests == requests, "verified dependency cache skips network");
            File.WriteAllText(first, "bad cache");
            await fixture.Downloader.Get(fixture.Dotnet);
            check(fixture.Requests == requests + 1 && new FileInfo(first).Length == fixture.Dotnet.Size, "corrupt cache is replaced only by verified bytes");
            File.WriteAllText(first, "old invalid cache");
            fixture.Payloads[fixture.Dotnet.Url] = new byte[fixture.Dotnet.Size];
            await Reject(async () => await fixture.Downloader.Get(fixture.Dotnet), "dependency checksum mismatch is rejected");
            check(File.ReadAllText(first) == "old invalid cache" && !Directory.GetFiles(fixture.Cache, "*.tmp").Any(), "failed download keeps cache and removes temporary payload");
            fixture.Payloads[fixture.Dotnet.Url] = new byte[1];
            await Reject(async () => await fixture.Downloader.Get(fixture.Dotnet), "truncated dependency size is rejected");
        }
        using (var fixture = new Fixture(root))
        {
            fixture.ExitCodes.Enqueue(1622);
            var exception = await Reject(async () => await fixture.Service.Setup(fixture.Folder), "Windows Installer log failure is handled separately");
            check(exception.Message.Contains("could not open its installation log") && File.Exists((string)exception.Data["SetupLogPath"]!), "1622 retains an accessible setup log and explains the native log failure");
        }
        using (var fixture = new Fixture(root))
        {
            fixture.ExitCodes.Enqueue(1603);
            var exception = await Reject(async () => await fixture.Service.Setup(fixture.Folder), "failed Microsoft installer stops setup");
            var log = (string)exception.Data["SetupLogPath"]!;
            check(File.ReadAllText(log).Contains("1603") && !File.Exists(Path.Combine(fixture.Folder, "version.dll")), "failure log records exit code and loader is not partially installed");
            check((await fixture.Service.Setup(fixture.Folder)).Status.Ready, "setup can retry after an installer failure");
        }
        using (var fixture = new Fixture(root))
        {
            using var cancellation = new CancellationTokenSource();
            await Reject(async () => await fixture.Downloader.Get(fixture.Dotnet, new Reporter(value => { if (value.Percent == 100) cancellation.Cancel(); }), cancellation.Token), "cancelling after transfer does not promote an unverified cache file");
            check(!Directory.GetFiles(fixture.Cache).Any(), "cancelled transfer removes its incomplete cache payload");
        }
        using (var fixture = new Fixture(root))
        {
            fixture.InsecureRedirect = true;
            await Reject(async () => await fixture.Downloader.Get(fixture.Dotnet), "dependency download rejects an HTTPS downgrade redirect");
        }
        using (var fixture = new Fixture(root))
        {
            fixture.ExitCodes.Enqueue(3010);
            var result = await fixture.Service.Setup(fixture.Folder);
            check(result.RestartRequired && !result.Status.Ready && fixture.InstallerCalls.Count == 1, "restart request stops remaining installs and is not reported ready");
        }
        using (var fixture = new Fixture(root))
        {
            fixture.ExitCodes.Enqueue(1223);
            await Reject(async () => await fixture.Service.Setup(fixture.Folder), "cancelled Windows elevation is handled without further installs");
            check(fixture.InstallerCalls.Count == 1 && !File.Exists(Path.Combine(fixture.Folder, "version.dll")), "cancelled installer does not proceed to loader installation");
        }
        using (var fixture = new Fixture(root))
        {
            fixture.IgnoreSuccessfulInstall = true;
            await Reject(async () => await fixture.Service.Setup(fixture.Folder), "installer success without detected runtime is rejected");
        }
        using (var fixture = new Fixture(root))
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Reject(async () => await fixture.Service.Setup(fixture.Folder, cancellation: cancellation.Token), "cancelled setup does not run installers");
            check(fixture.Requests == 0 && fixture.InstallerCalls.Count == 0, "cancelled setup leaves prerequisites untouched");
        }
        check(RequirementCatalog.LoaderFiles.Count == 164 && RequirementCatalog.LoaderFiles.Any(f => f.Path == "version.dll"), "embedded official loader catalog contains all release files");
    }
    public static async Task RemoteSmoke()
    {
        var root = Path.Combine(Path.GetTempPath(), "ScheduleI-dependency-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var game = Path.Combine(root, "Game");
            CreatePe(Path.Combine(game, "Schedule I.exe"));
            CreatePe(Path.Combine(game, "GameAssembly.dll"));
            Directory.CreateDirectory(Path.Combine(game, "Schedule I_Data/il2cpp_data/Metadata"));
            File.WriteAllText(Path.Combine(game, "Schedule I_Data/globalgamemanagers"), "0.4.7f5");
            File.WriteAllText(Path.Combine(game, "Schedule I_Data/il2cpp_data/Metadata/global-metadata.dat"), "fixture");
            using var downloader = new VerifiedDependencyDownloader();
            var archive = await downloader.Get(RequirementCatalog.Loader);
            foreach (var artifact in new[] { RequirementCatalog.DotNet, RequirementCatalog.VisualCpp })
                Console.WriteLine("PASS official installer downloaded and pinned checksum verified (not executed): " + await downloader.Get(artifact));
            using var service = new DependencySetupService(downloader, folder => GameDetector.Detect(folder) with { RuntimeInstalled = true, VisualCppInstalled = true },
                (_, _, _) => throw new Exception("Smoke test never executes Microsoft installers"), RequirementCatalog.LoaderFiles, RequirementCatalog.Loader, RequirementCatalog.DotNet, RequirementCatalog.VisualCpp, Path.Combine(root, "Logs"));
            var result = await service.Setup(game);
            if (!result.Status.Ready) throw new Exception("Official fresh loader setup failed.");
            Console.WriteLine("PASS official MelonLoader fresh setup and all 164 file hashes in isolated game fixture");
            var file = Path.Combine(game, "MelonLoader/net6/MelonLoader.NativeHost.dll");
            File.Delete(file);
            if ((await service.Inspect(game)).Loader.State != LoaderState.Repairable) throw new Exception("Missing-file repair not detected.");
            if (!(await service.Setup(game)).Status.Ready) throw new Exception("Repair verification failed.");
            Console.WriteLine("PASS official missing-file repair with verified cached archive; actual installed game and system runtimes untouched");
        }
        finally { Directory.Delete(root, true); }
    }
    internal static void CreatePe(string file, bool x64 = true)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var bytes = new byte[128];
        bytes[0] = (byte)'M'; bytes[1] = (byte)'Z'; bytes[0x3C] = 64;
        bytes[64] = (byte)'P'; bytes[65] = (byte)'E'; bytes[68] = x64 ? (byte)0x64 : (byte)0x4C; bytes[69] = x64 ? (byte)0x86 : (byte)0x01;
        File.WriteAllBytes(file, bytes);
    }
    private sealed class Reporter(Action<SetupProgress> report) : IProgress<SetupProgress> { public void Report(SetupProgress value) => report(value); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(send(request)); }
    }
    private sealed class Fixture : IDisposable
    {
        public string Folder { get; }
        public string Cache { get; }
        public int Requests { get; private set; }
        public List<string> InstallerCalls { get; } = [];
        public Queue<int> ExitCodes { get; } = [];
        public Dictionary<string, byte[]> Payloads { get; } = [];
        public DependencyArtifact Dotnet { get; }
        public DependencyArtifact Vc { get; }
        public DependencyArtifact Loader { get; }
        public LoaderFile[] Definitions { get; }
        public Func<GameInstallation> Game { get; }
        public VerifiedDependencyDownloader Downloader { get; }
        public DependencySetupService Service { get; }
        public string? LoaderVersionOverride { get; set; }
        public bool IgnoreSuccessfulInstall { get; set; }
        public bool InsecureRedirect { get; set; }
        private bool runtime, vc;
        private readonly HttpClient client;
        public Fixture(string root)
        {
            var local = Path.Combine(root, "DependencyFixture-" + Guid.NewGuid().ToString("N"));
            Folder = Path.Combine(local, "Game"); Cache = Path.Combine(local, "Cache");
            CreatePe(Path.Combine(Folder, "Schedule I.exe"));
            var loaderBytes = new Dictionary<string, byte[]> { ["version.dll"] = "bootstrap fixture"u8.ToArray(), ["MelonLoader/net6/MelonLoader.dll"] = "loader fixture"u8.ToArray() };
            Definitions = loaderBytes.Select(p => new LoaderFile(p.Key, Convert.ToHexString(SHA256.HashData(p.Value)).ToLowerInvariant(), p.Value.Length)).ToArray();
            var zip = new MemoryStream();
            using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, true))
                foreach (var (name, bytes) in loaderBytes) { using var entry = archive.CreateEntry(name).Open(); entry.Write(bytes); }
            DependencyArtifact Artifact(string name, byte[] bytes, bool sha512 = false)
            {
                var url = "https://fixtures.invalid/" + name;
                Payloads[url] = bytes;
                return new(name, name, url, Convert.ToHexString(sha512 ? SHA512.HashData(bytes) : SHA256.HashData(bytes)), bytes.Length, sha512);
            }
            Loader = Artifact("loader.zip", zip.ToArray());
            Dotnet = Artifact("desktop.exe", "dotnet installer fixture"u8.ToArray(), true);
            Vc = Artifact("vc.exe", "visual cpp installer fixture"u8.ToArray());
            client = new HttpClient(new Handler(request => { Requests++; return new(HttpStatusCode.OK) { RequestMessage = InsecureRedirect ? new HttpRequestMessage(HttpMethod.Get, "http://fixtures.invalid/file") : request, Content = new ByteArrayContent(Payloads[request.RequestUri!.AbsoluteUri]) }; }));
            Downloader = new(client, Cache);
            GameInstallation Detect(string folder) => new(folder, Path.Combine(folder, "Schedule I.exe"), "0.4.7f5", "IL2CPP", LoaderVersionOverride ?? (File.Exists(Path.Combine(folder, "MelonLoader/net6/MelonLoader.dll")) ? "0.7.3" : "Missing"), runtime) { VisualCppInstalled = vc };
            Game = () => Detect(Folder);
            Task<int> Install(string file, string name, string log)
            {
                InstallerCalls.Add(Path.GetFileName(file));
                var code = ExitCodes.TryDequeue(out var value) ? value : 0;
                if (code == 0 && !IgnoreSuccessfulInstall) { if (Path.GetFileName(file) == Dotnet.FileName) runtime = true; else vc = true; }
                return Task.FromResult(code);
            }
            Service = new(Downloader, Detect, Install, Definitions, Loader, Dotnet, Vc, Path.Combine(local, "Logs"));
        }
        public void Dispose() { Service.Dispose(); client.Dispose(); }
    }
}
