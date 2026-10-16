using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ScheduleI.Manager;

public sealed class RegistryDocument
{
    [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; }
    [JsonPropertyName("mods")] public List<RegistryMod>? Mods { get; set; }
}

public sealed class RegistryMod
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("author")] public string Author { get; set; } = "";
    [JsonPropertyName("summary")] public string Summary { get; set; } = "";
    [JsonPropertyName("repository")] public string Repository { get; set; } = "";
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("package_name")] public string PackageName { get; set; } = "";
    [JsonPropertyName("download_url")] public string DownloadUrl { get; set; } = "";
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
    [JsonPropertyName("size_bytes")] public long SizeBytes { get; set; }
    [JsonPropertyName("compatibility")] public Compatibility Compatibility { get; set; } = new();
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
    public RegistryClient(HttpClient? client = null)
    {
        ownsClient = client == null;
        this.client = client ?? new HttpClient(new HttpClientHandler { MaxAutomaticRedirections = 5 }) { Timeout = TimeSpan.FromSeconds(90) };
    }
    public async Task<IReadOnlyList<RegistryMod>> Fetch(CancellationToken cancellation = default)
    {
        byte[] bytes;
        try { bytes = await FetchBytes(IndexUrl, cancellation); }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException && !cancellation.IsCancellationRequested)
        {
            bytes = await FetchBytes(RawIndexUrl, cancellation);
        }
        RegistryDocument? document;
        try { document = JsonSerializer.Deserialize<RegistryDocument>(bytes, JsonFiles.Options); }
        catch (JsonException exception) { throw new InvalidDataException("Registry index is not valid JSON.", exception); }
        if (document?.SchemaVersion != 1) throw new InvalidDataException("Unsupported registry schema. Update the manager to read this index.");
        if (document.Mods == null || document.Mods.Count > 1000) throw new InvalidDataException("Invalid registry mod list.");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in document.Mods)
        {
            Validate(mod);
            if (!ids.Add(mod.Id) || !names.Add(mod.PackageName)) throw new InvalidDataException("Registry contains duplicate mod identities.");
        }
        return document.Mods.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    private async Task<byte[]> FetchBytes(string url, CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        cancellation = timeout.Token;
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("ScheduleIModManager/" + typeof(RegistryClient).Assembly.GetName().Version?.ToString(3));
        request.Headers.Accept.ParseAdd("application/vnd.github.raw+json");
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
        response.EnsureSuccessStatusCode();
        RequireHttps(response);
        if (response.Content.Headers.ContentLength > MaxIndexBytes) throw new InvalidDataException("Registry index exceeds the size limit.");
        await using var source = await response.Content.ReadAsStreamAsync(cancellation);
        using var bytes = new MemoryStream();
        await CopyBounded(source, bytes, MaxIndexBytes, cancellation);
        return bytes.ToArray();
    }
    public static void Validate(RegistryMod mod)
    {
        if (mod == null || !IdPattern().IsMatch(mod.Id ?? "") || !PackagePattern().IsMatch(mod.PackageName ?? "") || !VersionPattern().IsMatch(mod.Version ?? "") || !HashPattern().IsMatch(mod.Sha256 ?? "") || mod.SizeBytes is <= 0 or > MaxPackageBytes)
            throw new InvalidDataException("Invalid registry mod identity, checksum or size.");
        foreach (var (value, max) in new[] { (mod.Name, 80), (mod.Author, 80), (mod.Summary, 200) })
            if (string.IsNullOrWhiteSpace(value) || value.Length > max || value.Any(char.IsControl)) throw new InvalidDataException("Invalid registry display text.");
        if (!RepositoryPattern().IsMatch(mod.Repository ?? "")) throw new InvalidDataException("Registry source must be a public GitHub repository URL.");
        var prefix = mod.Repository + "/releases/download/v" + mod.Version + "/";
        if (mod.DownloadUrl == null || !mod.DownloadUrl.StartsWith(prefix, StringComparison.Ordinal) || !AssetPattern().IsMatch(mod.DownloadUrl[prefix.Length..]))
            throw new InvalidDataException("Download must be a ZIP in the registered repository's matching version release.");
        var c = mod.Compatibility;
        if (c == null || c.Game != "Schedule I" || c.Backend is not ("IL2CPP" or "Mono") || c.Platforms is not { Length: 1 } || c.Platforms[0] != "Windows x64" || c.Loader?.Name != "MelonLoader" || !ValidVersions(c.GameVersions) || !ValidVersions(c.Loader.Versions))
            throw new InvalidDataException("Invalid registry compatibility metadata.");
    }
    private static bool ValidVersions(string[]? versions) => versions is { Length: > 0 and <= 100 } && versions.All(v => !string.IsNullOrWhiteSpace(v) && v.Length <= 40 && !v.Any(char.IsControl)) && versions.Distinct(StringComparer.Ordinal).Count() == versions.Length;
    public static void ValidatePackage(RegistryMod mod, LibraryPackage package)
    {
        Validate(mod);
        var actual = package.Compatibility;
        var expected = mod.Compatibility;
        static bool Same(string[] a, string[] b) => a.Order(StringComparer.Ordinal).SequenceEqual(b.Order(StringComparer.Ordinal));
        if (package.Manifest.Name != mod.PackageName || package.Manifest.Version != mod.Version || actual.Game != expected.Game || actual.Backend != expected.Backend || actual.Loader.Name != expected.Loader.Name || !Same(actual.GameVersions, expected.GameVersions) || !Same(actual.Platforms, expected.Platforms) || !Same(actual.Loader.Versions, expected.Loader.Versions))
            throw new InvalidDataException("Downloaded package identity or compatibility does not match the registry.");
        if (package.Manifest.Dependencies.Any(d => !expected.Loader.Versions.Select(v => "LavaGang-MelonLoader-" + v).Contains(d))) throw new InvalidDataException("Registry package declares unsupported additional dependencies.");
    }
    public async Task<LibraryPackage> Download(RegistryMod mod, string library, CancellationToken cancellation = default)
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
