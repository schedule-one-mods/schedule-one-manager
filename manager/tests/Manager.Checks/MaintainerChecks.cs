using System.Net;
using System.Text.Json;
using ScheduleI.Manager;

internal static class MaintainerChecks
{
    public static async Task RemoteSmoke(string url, string output)
    {
        using var resolver = new RegistryClient();
        var review = await resolver.ResolveReview(url);
        using var client = RegistryClient.ForReview(review);
        var evidence = new ReviewEvidence(review, Path.GetFullPath(output));
        var library = Path.Combine(Path.GetTempPath(), "ScheduleI-review-smoke-" + Guid.NewGuid().ToString("N"));
        try
        {
            var mods = await client.Fetch();
            evidence.SaveIndex(client);
            Console.WriteLine($"Pinned PR #{review.Number}: {review.Repository}@{review.Commit}; {mods.Count} entries; index SHA-256 {client.LastIndexSha256}");
            if (mods.Count > 30) throw new InvalidOperationException("Live smoke limits automatic downloads to 30 entries.");
            foreach (var mod in mods)
            {
                evidence.Record("Download requested", mod);
                var package = await client.Download(mod.Versions.Single(v => v.IsLatest), library);
                evidence.Record("Download verified", ReviewEvidence.Package(package));
                Console.WriteLine($"PASS PR index -> {mod.Versions.Single(v => v.IsLatest).DownloadUrl} -> verified {package.Identity}, DLL {package.Sha256}");
            }
            evidence.Record("Live review fetch/download smoke completed; no game launched or modified");
        }
        catch (Exception exception) { evidence.Record("Live smoke failed", new { Error = exception.Message }); throw; }
        finally { if (Directory.Exists(library)) Directory.Delete(library, true); }
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(handle(request));
    }
    public static async Task Run(string root, Action<bool, string> check)
    {
        async Task Reject(Func<Task> action, string name)
        {
            try { await action(); }
            catch (Exception exception) when (exception is InvalidDataException or IOException or InvalidOperationException or HttpRequestException or OperationCanceledException)
            { check(true, name); return; }
            throw new Exception("Expected rejection: " + name);
        }
        const string url = "https://github.com/schedule-one-mods/schedule-one-mod-registry/pull/23";
        var commit = new string('a', 40);
        var requests = new List<string>();
        var failIndex = false;
        using var http = new HttpClient(new Handler(request =>
        {
            var uri = request.RequestUri!.AbsoluteUri;
            requests.Add(uri);
            var metadata = JsonSerializer.Serialize(new { number = 23, @base = new { repo = new { full_name = "schedule-one-mods/schedule-one-mod-registry" } }, head = new { sha = commit, repo = new { full_name = "contributor/registry-fork" } } });
            return new(uri.Contains("/contents/") || failIndex && !uri.Contains("/pulls/") ? HttpStatusCode.NotFound : HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent(uri.Contains("/pulls/") ? metadata : "{\"schema_version\":2,\"mods\":[]}")
            };
        }));
        using var resolver = new RegistryClient(http);
        var review = await resolver.ResolveReview(url + "/files#diff-123");
        check(review.Repository == "contributor/registry-fork" && review.Commit == commit && review.PullRequestUrl == url, "review resolves fork HEAD SHA and canonical PR URL");
        check(Path.GetFileName(ManagerPaths.Root) == "ScheduleIModManager.Maintainer" && !ManagerSettings.FilePath.Contains("ScheduleIModManager\\settings"), "maintainer settings and caches are isolated from public profile");
        foreach (var bad in new[] { "http://github.com/schedule-one-mods/schedule-one-mod-registry/pull/23", "https://github.com/other/registry/pull/23", "https://github.com.evil.test/schedule-one-mods/schedule-one-mod-registry/pull/23", url + "0/../../issues/23" })
            await Reject(async () => await resolver.ResolveReview(bad), "reject non-registry or invalid review URL");
        using var client = RegistryClient.ForReview(review, http);
        check((await client.Fetch()).Count == 0 && requests.Contains(review.IndexUrl) && requests.Contains(review.RawIndexUrl) && !requests.Contains(RegistryClient.IndexUrl), "review API and raw fallback both use fork commit, never main");
        check(client.LastIndexSha256?.Length == 64, "review index bytes have a recorded SHA-256");
        failIndex = true;
        await Reject(async () => await client.Fetch(), "unavailable PR index fails without falling back to official main");
        check(!requests.Contains(RegistryClient.IndexUrl) && !requests.Contains(RegistryClient.RawIndexUrl), "failed PR fetch never requests production registry");

        var probeFolder = Path.Combine(root, "ProcessProbe");
        Directory.CreateDirectory(Path.Combine(probeFolder, "MelonLoader"));
        var probeGame = new GameInstallation(probeFolder, Environment.ProcessPath!, "fixture", "IL2CPP", "0.7.3", true);
        var probeLog = Path.Combine(probeFolder, "MelonLoader", "Latest.log");
        int lastPid = 0;
        System.Diagnostics.Process StartFixture(string logText, int lifetime, int exitCode)
        {
            var start = new System.Diagnostics.ProcessStartInfo(probeGame.Executable) { UseShellExecute = false, CreateNoWindow = true };
            start.Environment["DOTNET_ROOT_X64"] = new DirectoryInfo(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory()).Parent!.Parent!.Parent!.FullName;
            foreach (var argument in new[] { "--launch-fixture", probeLog, logText, lifetime.ToString(), exitCode.ToString() }) start.ArgumentList.Add(argument);
            var process = System.Diagnostics.Process.Start(start)!;
            lastPid = process.Id;
            return process;
        }
        var survival = await MaintainerLaunchSuite.ObserveProcess(probeGame, TimeSpan.FromSeconds(1), default, _ => StartFixture("loader ready", 30000, 0), TimeSpan.FromMilliseconds(100));
        check(survival.Outcome == "StartupObserved" && survival.FreshLoaderLog, "real isolated helper process produces a fresh-log startup observation");
        bool Exited(int pid) { try { using var process = System.Diagnostics.Process.GetProcessById(pid); return process.HasExited; } catch (ArgumentException) { return true; } }
        check(Exited(lastPid), "observation stops its owned helper process after the window");
        var earlyExit = await MaintainerLaunchSuite.ObserveProcess(probeGame, TimeSpan.FromSeconds(2), default, _ => StartFixture("loader failed", 50, 7), TimeSpan.FromMilliseconds(100));
        check(earlyExit.Outcome == "ExitedEarly" && earlyExit.ExitCode == 7, "real helper early exit records its exit code");
        var lateExit = await MaintainerLaunchSuite.ObserveProcess(probeGame, TimeSpan.FromSeconds(1), default, _ => StartFixture("late exit", 700, 8), TimeSpan.FromMilliseconds(100));
        check(lateExit.Outcome == "ExitedEarly" && lateExit.ExitCode == 8, "exit during the final polling interval is not reported as surviving startup");
        var noLog = await MaintainerLaunchSuite.ObserveProcess(probeGame, TimeSpan.FromSeconds(1), default, _ => StartFixture("no-log", 30000, 0), TimeSpan.FromMilliseconds(100));
        check(noLog.Outcome == "Inconclusive" && !noLog.FreshLoaderLog, "surviving helper without a fresh loader log is inconclusive");
        var loaderError = await MaintainerLaunchSuite.ObserveProcess(probeGame, TimeSpan.FromSeconds(1), default, _ => StartFixture("[ERROR] fixture failure", 30000, 0), TimeSpan.FromMilliseconds(100));
        check(loaderError.Outcome == "LoaderErrorsObserved", "fresh loader error markers are distinguished from clean startup observations");
        using (var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300)))
            await Reject(() => MaintainerLaunchSuite.ObserveProcess(probeGame, TimeSpan.FromSeconds(2), cancellation.Token, _ => StartFixture("loader ready", 30000, 0), TimeSpan.FromMilliseconds(100)), "real helper observation supports cancellation");
        check(Exited(lastPid), "cancellation stops the exact helper process before returning");

        var folder = Path.Combine(root, "ReviewGame");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "version.dll"), "fixture");
        var game = new GameInstallation(folder, Path.Combine(folder, "Schedule I.exe"), "0.4.7f12", "IL2CPP", "0.7.3", true);
        LibraryPackage Package(string name)
        {
            var directory = Path.Combine(root, "ReviewLibrary", name + "-0.0.1");
            Directory.CreateDirectory(Path.Combine(directory, "Mods"));
            var dll = Path.Combine(directory, "Mods", name + ".dll");
            File.Copy(typeof(ModLibrary).Assembly.Location, dll);
            return new(directory, new() { Name = name, Version = "0.0.1" }, new()
            {
                Game = "Schedule I", Backend = "IL2CPP", GameVersions = ["0.4.7f5"], Platforms = ["Windows x64"], Loader = new() { Name = "MelonLoader", Versions = ["0.7.3"] }
            }, dll, ModLibrary.Hash(dll));
        }
        var packages = new[] { Package("First"), Package("Second"), Package("Third") };
        check(ModDeployment.CompatibilityIssue(game, packages[0]).Contains("unverified"), "maintainer override defaults off");
        MaintainerCompatibility.AllowUnverifiedGameVersion = true;
        try
        {
            check(ModDeployment.CompatibilityIssue(game, packages[0]) == "", "maintainer can test an unlisted game version without editing package metadata");
            check(ModDeployment.CompatibilityIssue(game with { Backend = "Mono" }, packages[0]).Contains("IL2CPP"), "version override still rejects wrong backend");
            check(ModDeployment.CompatibilityIssue(game with { LoaderVersion = "0.7.2" }, packages[0]).Contains("MelonLoader"), "version override still rejects wrong loader");
            check(ModDeployment.CompatibilityIssue(game with { RuntimeInstalled = false }, packages[0]).Contains(".NET"), "version override still rejects missing runtimes");
            ModDeployment.Enable(game, packages[1]);
            ModDeployment.ValidateLaunch(game, packages);
            check(ModDeployment.Status(game, packages[1]) == "Enabled", "version override applies consistently to enable and launch validation");
            var plan = MaintainerLaunchSuite.Plan(packages);
            check(plan.Count == 6 && plan[0].Packages.Length == 0 && plan[^1].Packages.Length == 3 && plan.Count(p => p.Packages.Length == 1) == 3, "launch plan covers baseline, every mod alone and cumulative combinations");
            var evidence = new ReviewEvidence(review, Path.Combine(root, "Evidence"));
            var observed = new List<string[]>();
            var suite = new MaintainerLaunchSuite((current, window, token) =>
            {
                observed.Add(packages.Where(p => ModDeployment.Status(current, p) == "Enabled").Select(p => p.Identity).ToArray());
                return Task.FromResult(new LaunchObservation("StartupObserved", 15, null, true, "fixture"));
            });
            await suite.Run(game, packages, evidence, TimeSpan.FromSeconds(15));
            check(observed.Select(p => p.Length).SequenceEqual(new[] { 0, 1, 1, 1, 2, 3 }), "suite deploys the exact planned set for each observation");
            check(ModDeployment.Status(game, packages[1]) == "Enabled" && packages.Where(p => p != packages[1]).All(p => ModDeployment.Status(game, p) == "Disabled"), "successful suite restores original enabled set");
            var report = File.ReadAllText(Path.Combine(evidence.DirectoryPath, "report.json"));
            check(report.Contains(commit) && report.Contains("StartupObserved") && report.Contains("Original enabled set restored"), "exported report ties observations and restoration to exact PR commit");
            var attempts = 0;
            var broken = new MaintainerLaunchSuite((_, _, _) => ++attempts == 1 ? Task.FromResult(new LaunchObservation("StartupObserved", 15, null, true, "fixture")) : throw new IOException("fixture launch failure"));
            await Reject(() => broken.Run(game, packages, evidence, TimeSpan.FromSeconds(15)), "suite propagates launch failures");
            check(ModDeployment.Status(game, packages[1]) == "Enabled" && ModDeployment.Status(game, packages[0]) == "Disabled", "failed suite restores original enabled set");
            attempts = 0;
            var cancelled = new MaintainerLaunchSuite((_, _, _) => ++attempts == 1 ? Task.FromResult(new LaunchObservation("StartupObserved", 15, null, true, "fixture")) : throw new OperationCanceledException());
            await Reject(() => cancelled.Run(game, packages, evidence, TimeSpan.FromSeconds(15)), "suite propagates cancellation");
            check(ModDeployment.Status(game, packages[1]) == "Enabled" && ModDeployment.Status(game, packages[0]) == "Disabled", "cancelled suite restores original enabled set");
            var baselineFailure = new MaintainerLaunchSuite((_, _, _) => Task.FromResult(new LaunchObservation("ExitedEarly", 1, 1, false, "fixture")));
            await Reject(() => baselineFailure.Run(game, packages, evidence, TimeSpan.FromSeconds(15)), "failed baseline stops the suite before testing mods");
            File.WriteAllText(Path.Combine(folder, "Mods", "External.dll"), "external");
            await Reject(() => suite.Run(game, packages, evidence, TimeSpan.FromSeconds(15)), "suite refuses external DLLs instead of silently testing contaminated combinations");
            check(File.ReadAllText(Path.Combine(folder, "Mods", "External.dll")) == "external" && ModDeployment.Status(game, packages[1]) == "Enabled", "preflight rejection preserves external files and enabled mods");
            File.Delete(Path.Combine(folder, "Mods", "External.dll"));
            attempts = 0;
            var changed = new MaintainerLaunchSuite((_, _, _) =>
            {
                if (++attempts == 2) File.AppendAllText(Path.Combine(folder, "Mods", packages[0].FileName), "externally changed");
                return Task.FromResult(new LaunchObservation("StartupObserved", 15, null, true, "fixture"));
            });
            await Reject(() => changed.Run(game, packages, evidence, TimeSpan.FromSeconds(15)), "suite reports restoration failure when a deployed DLL changes during observation");
            check(File.ReadAllText(Path.Combine(evidence.DirectoryPath, "report.json")).Contains("RESTORATION REQUIRED") && File.ReadAllText(Path.Combine(folder, "Mods", packages[0].FileName)).EndsWith("externally changed"), "changed deployed files are preserved and incomplete restoration is explicit in evidence");
        }
        finally { MaintainerCompatibility.AllowUnverifiedGameVersion = false; }
    }
}
