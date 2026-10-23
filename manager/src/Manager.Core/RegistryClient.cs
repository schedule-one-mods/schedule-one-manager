using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ScheduleI.Manager;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class RegistryDocument
{
    [JsonRequired, JsonPropertyName("schema_version")] public int SchemaVersion { get; set; }
    [JsonPropertyName("$schema")] public string? Schema { get; set; }
    [JsonRequired, JsonPropertyName("mods")] public List<RegistryMod>? Mods { get; set; }
}

public class RegistryIdentity
{
    [JsonRequired, JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonRequired, JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonRequired, JsonPropertyName("author")] public string Author { get; set; } = "";
    [JsonRequired, JsonPropertyName("summary")] public string Summary { get; set; } = "";
    [JsonRequired, JsonPropertyName("repository")] public string Repository { get; set; } = "";
    [JsonRequired, JsonPropertyName("package_name")] public string PackageName { get; set; } = "";
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ReleaseSource
{
    [JsonRequired, JsonPropertyName("type")] public string Type { get; set; } = "github_releases";
    [JsonRequired, JsonPropertyName("latest")] public string Latest { get; set; } = "github_latest";
    [JsonRequired, JsonPropertyName("compatibility_path")] public string CompatibilityPath { get; set; } = "packaging/compatibility.json";
    [JsonRequired, JsonPropertyName("asset_name")] public string AssetName { get; set; } = "{package_name}-{version}-{backend}.zip";
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class RegistryMod : RegistryIdentity
{
    [JsonRequired, JsonPropertyName("release_source")] public ReleaseSource ReleaseSource { get; set; } = new();
    [JsonIgnore] public List<RegistryRelease> Versions { get; set; } = [];
    [JsonIgnore] public string? ReleaseError { get; set; }
}
public sealed class RegistryRelease : RegistryIdentity
{
    public string Version { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long SizeBytes { get; set; }
    public Compatibility Compatibility { get; set; } = new();
    public bool IsLatest { get; set; }
    public bool Prerelease { get; set; }
    public string Label => Version + (IsLatest ? " · Latest" : "") + (Prerelease ? " · Prerelease" : "");
}

/// <summary>Reads the official index and imports verified release archives; never executes downloaded code.</summary>
public sealed partial class RegistryClient : IDisposable
{
    public const string IndexUrl = "https://api.github.com/repos/schedule-one-mods/schedule-one-mod-registry/contents/index.json?ref=main";
    public const string RawIndexUrl = "https://raw.githubusercontent.com/schedule-one-mods/schedule-one-mod-registry/main/index.json";
    private const int MaxIndexBytes = 1024 * 1024;
    private const int MaxPackageBytes = 64 * 1024 * 1024;
    private readonly HttpClient client;
    private readonly bool ownsClient;
    private readonly string indexUrl = IndexUrl, rawIndexUrl = RawIndexUrl;
    public RegistryClient(HttpClient? client = null)
    {
        ownsClient = client == null;
        this.client = client ?? new HttpClient(new HttpClientHandler { MaxAutomaticRedirections = 5 }) { Timeout = TimeSpan.FromSeconds(90) };
    }
    public async Task<IReadOnlyList<RegistryMod>> Fetch(CancellationToken cancellation = default)
    {
        byte[] bytes;
        try { bytes = await FetchBytes(indexUrl, cancellation); }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException && !cancellation.IsCancellationRequested)
        {
            bytes = await FetchBytes(rawIndexUrl, cancellation);
        }
#if MAINTAINER
        LastIndexBytes = bytes.ToArray();
        LastIndexSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
#endif
        RegistryDocument? document;
        try { document = JsonSerializer.Deserialize<RegistryDocument>(bytes, JsonFiles.Options); }
        catch (JsonException exception) { throw new InvalidDataException("Registry index is not valid JSON.", exception); }
        if (document?.SchemaVersion != 2) throw new InvalidDataException("Unsupported registry schema. Update the manager to read this index.");
        if (document.Mods == null || document.Mods.Count > 1000) throw new InvalidDataException("Invalid registry mod list.");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in document.Mods)
        {
            ValidateIdentity(mod);
            ValidateSource(mod.ReleaseSource);
            if (!ids.Add(mod.Id) || !names.Add(mod.PackageName)) throw new InvalidDataException("Registry contains duplicate mod identities.");
        }
        foreach (var mod in document.Mods)
        {
            try { mod.Versions = await FetchReleases(mod, cancellation); }
            catch (Exception exception) when (exception is HttpRequestException or InvalidDataException or JsonException or KeyNotFoundException or InvalidOperationException or OperationCanceledException && !cancellation.IsCancellationRequested)
            { mod.ReleaseError = exception.Message; }
        }
        return document.Mods.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    private async Task<byte[]> FetchBytes(string url, CancellationToken cancellation, string accept = "application/vnd.github.raw+json")
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        cancellation = timeout.Token;
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("ScheduleIModManager/" + typeof(RegistryClient).Assembly.GetName().Version?.ToString(3));
        request.Headers.Accept.ParseAdd(accept);
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
        if (response.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.TooManyRequests &&
            (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests || response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) && remaining.Contains("0")))
            throw new HttpRequestException("GitHub's request limit was reached. Wait before refreshing; your downloaded versions remain available offline.", null, response.StatusCode);
        response.EnsureSuccessStatusCode();
        RequireHttps(response);
        if (response.Content.Headers.ContentLength > MaxIndexBytes) throw new InvalidDataException("Registry index exceeds the size limit.");
        await using var source = await response.Content.ReadAsStreamAsync(cancellation);
        using var bytes = new MemoryStream();
        await CopyBounded(source, bytes, MaxIndexBytes, cancellation);
        return bytes.ToArray();
    }
    public static void Validate(RegistryRelease mod)
    {
        if (mod == null || !IdPattern().IsMatch(mod.Id ?? "") || !PackagePattern().IsMatch(mod.PackageName ?? "") || (!VersionPattern().IsMatch(mod.Version ?? "") || !System.Version.TryParse(mod.Version, out _)) || !HashPattern().IsMatch(mod.Sha256 ?? "") || mod.SizeBytes is <= 0 or > MaxPackageBytes)
            throw new InvalidDataException("Invalid registry mod identity, checksum or size.");
        ValidateIdentity(mod);
        var prefix = mod.Repository + "/releases/download/v" + mod.Version + "/";
        if (mod.DownloadUrl == null || !mod.DownloadUrl.StartsWith(prefix, StringComparison.Ordinal) || !AssetPattern().IsMatch(mod.DownloadUrl[prefix.Length..]))
            throw new InvalidDataException("Download must be a ZIP in the registered repository's matching version release.");
        var c = mod.Compatibility;
        if (c == null || c.Game != "Schedule I" || c.Backend is not ("IL2CPP" or "Mono") || c.Platforms is not { Length: 1 } || c.Platforms[0] != "Windows x64" || c.Loader?.Name != "MelonLoader" || !ValidVersions(c.GameVersions) || !ValidVersions(c.Loader.Versions))
            throw new InvalidDataException("Invalid registry compatibility metadata.");
    }
    private static void ValidateIdentity(RegistryIdentity mod)
    {
        if (mod == null) throw new InvalidDataException("Null mod entry.");
        if (!IdPattern().IsMatch(mod.Id ?? "") || !PackagePattern().IsMatch(mod.PackageName ?? "") || !RepositoryPattern().IsMatch(mod.Repository ?? ""))
            throw new InvalidDataException("Invalid mod identity or public GitHub repository.");
        foreach (var (value, max) in new[] { (mod.Name, 80), (mod.Author, 80), (mod.Summary, 200) })
            if (string.IsNullOrWhiteSpace(value) || value.Length > max || value.Any(char.IsControl)) throw new InvalidDataException("Invalid registry display text.");
    }
    private static void ValidateSource(ReleaseSource? source)
    {
        if (source == null || source.Type != "github_releases" || source.Latest != "github_latest" ||
            source.AssetName != "{package_name}-{version}-{backend}.zip" ||
            !Regex.IsMatch(source.CompatibilityPath ?? "", @"^[A-Za-z0-9_-]+(/[A-Za-z0-9_-]+)*\.json$"))
            throw new InvalidDataException("Invalid release discovery settings.");
    }
    public async Task<List<RegistryRelease>> FetchReleases(RegistryMod mod, CancellationToken cancellation = default)
    {
        ValidateIdentity(mod); ValidateSource(mod.ReleaseSource);
        var repo = mod.Repository["https://github.com/".Length..];
        var api = "https://api.github.com/repos/" + repo + "/releases";
        string? latestTag = null;
        try
        {
            using var latest = JsonDocument.Parse(await FetchBytes(api + "/latest", cancellation, "application/vnd.github+json"));
            latestTag = latest.RootElement.GetProperty("tag_name").GetString();
        }
        catch (HttpRequestException exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound) { }
        var versions = new List<RegistryRelease>();
        var errors = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var page = 1; page <= 10; page++)
        {
            using var document = JsonDocument.Parse(await FetchBytes(api + $"?per_page=100&page={page}", cancellation, "application/vnd.github+json"));
            var releases = document.RootElement;
            if (releases.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Invalid GitHub releases response.");
            foreach (var release in releases.EnumerateArray())
            {
                if (release.GetProperty("draft").GetBoolean()) continue;
                var tag = release.GetProperty("tag_name").GetString() ?? "";
                if (!tag.StartsWith('v') || !VersionPattern().IsMatch(tag[1..]) || !System.Version.TryParse(tag[1..], out _)) continue;
                try
                {
                    var version = tag[1..];
                    if (!seen.Add(version)) throw new InvalidDataException("Duplicate release version.");
                    var compatibilityBytes = await FetchBytes($"https://raw.githubusercontent.com/{repo}/{tag}/{mod.ReleaseSource.CompatibilityPath}", cancellation);
                    var compatibility = JsonSerializer.Deserialize<Compatibility>(compatibilityBytes, JsonFiles.Options) ?? throw new InvalidDataException("Missing compatibility metadata.");
                    var assetName = $"{mod.PackageName}-{version}-{compatibility.Backend}.zip";
                    var assets = release.GetProperty("assets").EnumerateArray().Where(a => a.GetProperty("name").GetString() == assetName).ToArray();
                    if (assets.Length != 1) throw new InvalidDataException("Expected one matching package asset: " + assetName);
                    var asset = assets[0];
                    var digest = asset.GetProperty("digest").GetString() ?? "";
                    if (!digest.StartsWith("sha256:", StringComparison.Ordinal)) throw new InvalidDataException("Release asset has no SHA-256 digest.");
                    var item = new RegistryRelease { Id = mod.Id, Name = mod.Name, Author = mod.Author, Summary = mod.Summary,
                        Repository = mod.Repository, PackageName = mod.PackageName, Version = version,
                        DownloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "", Sha256 = digest[7..],
                        SizeBytes = asset.GetProperty("size").GetInt64(), Compatibility = compatibility,
                        IsLatest = tag == latestTag, Prerelease = release.GetProperty("prerelease").GetBoolean() };
                    Validate(item); versions.Add(item);
                }
                catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidDataException or KeyNotFoundException or InvalidOperationException)
                { errors.Add(tag + ": " + exception.Message); }
            }
            if (releases.GetArrayLength() < 100) break;
            if (page == 10) errors.Add("Release history exceeds 1,000 entries; older releases are omitted.");
        }
        if (latestTag != null && !versions.Any(v => v.IsLatest)) errors.Add("The publisher's latest release is unavailable or invalid; select another version explicitly.");
        mod.ReleaseError = errors.Count == 0 ? null : string.Join("\n", errors);
        return versions.OrderByDescending(v => System.Version.Parse(v.Version)).ToList();
    }
    private static bool ValidVersions(string[]? versions) => versions is { Length: > 0 and <= 100 } && versions.All(v => !string.IsNullOrWhiteSpace(v) && v.Length <= 40 && !v.Any(char.IsControl)) && versions.Distinct(StringComparer.Ordinal).Count() == versions.Length;
    public static void ValidatePackage(RegistryRelease mod, LibraryPackage package)
    {
        Validate(mod);
        var actual = package.Compatibility;
        var expected = mod.Compatibility;
        static bool Same(string[] a, string[] b) => a.Order(StringComparer.Ordinal).SequenceEqual(b.Order(StringComparer.Ordinal));
        if (package.Manifest.Name != mod.PackageName || package.Manifest.Version != mod.Version || actual.Game != expected.Game || actual.Backend != expected.Backend || actual.Loader.Name != expected.Loader.Name || !Same(actual.GameVersions, expected.GameVersions) || !Same(actual.Platforms, expected.Platforms) || !Same(actual.Loader.Versions, expected.Loader.Versions))
            throw new InvalidDataException("Downloaded package identity or compatibility does not match the registry.");
        if (package.Manifest.Dependencies.Any(d => !expected.Loader.Versions.Select(v => "LavaGang-MelonLoader-" + v).Contains(d))) throw new InvalidDataException("Registry package declares unsupported additional dependencies.");
    }
    public async Task<LibraryPackage> Download(RegistryRelease mod, string library, CancellationToken cancellation = default)
    {
        Validate(mod);
        ModLibrary.RejectLinks(library);
        Directory.CreateDirectory(library);
        var temporary = Path.Combine(library, ".download-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            using var response = await client.GetAsync(mod.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellation);
            response.EnsureSuccessStatusCode();
            RequireHttps(response);
            if (response.Content.Headers.ContentLength is long size && size != mod.SizeBytes) throw new InvalidDataException("Downloaded package size does not match the registry.");
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                await using var input = await response.Content.ReadAsStreamAsync(cancellation);
                await CopyBounded(input, output, mod.SizeBytes, cancellation);
                if (output.Length != mod.SizeBytes) throw new InvalidDataException("Downloaded package was truncated.");
            }
            if (!ModLibrary.Hash(temporary).Equals(mod.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Downloaded package SHA-256 does not match the registry.");
            cancellation.ThrowIfCancellationRequested();
            return ModLibrary.Import(temporary, library, p => ValidatePackage(mod, p));
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static void RequireHttps(HttpResponseMessage response)
    {
        if (response.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps) throw new InvalidDataException("Registry downloads require HTTPS, including redirects.");
    }
    private static async Task CopyBounded(Stream input, Stream output, long maximum, CancellationToken cancellation)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellation)) != 0)
        {
            if ((total += read) > maximum) throw new InvalidDataException("Download exceeds its declared size limit.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellation);
        }
    }
    public void Dispose() { if (ownsClient) client.Dispose(); }
    [GeneratedRegex(@"^[a-z][a-z0-9-]{0,63}$")] private static partial Regex IdPattern();
    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_]{0,63}$")] private static partial Regex PackagePattern();
    [GeneratedRegex(@"^[0-9]+\.[0-9]+\.[0-9]+$")] private static partial Regex VersionPattern();
    [GeneratedRegex(@"^[a-f0-9]{64}$")] private static partial Regex HashPattern();
    [GeneratedRegex(@"^https://github\.com/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")] private static partial Regex RepositoryPattern();
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]*\.zip$")] private static partial Regex AssetPattern();
}
