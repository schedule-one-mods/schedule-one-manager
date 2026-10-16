using System.Security.Cryptography;

namespace ScheduleI.Manager;

public sealed class VerifiedDependencyDownloader : IDisposable
{
    public static string DefaultCache => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScheduleIModManager", "Downloads");
    private readonly HttpClient client;
    private readonly bool ownsClient;
    private readonly string cache;
    public VerifiedDependencyDownloader(HttpClient? client = null, string? cache = null)
    {
        ownsClient = client == null;
        this.client = client ?? new HttpClient(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(20) }) { Timeout = TimeSpan.FromSeconds(60) };
        this.cache = cache ?? DefaultCache;
    }
    public async Task<string> Get(DependencyArtifact artifact, IProgress<SetupProgress>? progress = null, CancellationToken cancellation = default)
    {
        var callerCancellation = cancellation;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromMinutes(15));
        cancellation = deadline.Token;
        if (!Uri.TryCreate(artifact.Url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || artifact.FileName != Path.GetFileName(artifact.FileName) || artifact.FileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || artifact.Size is <= 0 or > 256 * 1024 * 1024)
            throw new InvalidDataException("Invalid pinned dependency definition.");
        ModLibrary.RejectLinks(cache);
        Directory.CreateDirectory(cache);
        var path = Path.Combine(cache, artifact.FileName);
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked download cache files are not supported.");
        progress?.Report(new($"Checking cached {artifact.Name}…"));
        if (File.Exists(path) && await Valid(path, artifact, cancellation))
        {
            progress?.Report(new($"Using verified cached {artifact.Name}.", 100));
            return path;
        }
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            progress?.Report(new($"Downloading {artifact.Name}…", 0));
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("ScheduleIModManager/" + typeof(VerifiedDependencyDownloader).Assembly.GetName().Version?.ToString(3));
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
            response.EnsureSuccessStatusCode();
            if (response.RequestMessage?.RequestUri?.Scheme != "https") throw new InvalidDataException("Dependency download redirected away from HTTPS.");
            if (response.Content.Headers.ContentLength is long length && length != artifact.Size) throw new InvalidDataException($"{artifact.Name} size does not match the pinned release.");
            await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                await using var input = await response.Content.ReadAsStreamAsync(cancellation);
                var buffer = new byte[81920];
                long total = 0;
                int count, previousPercent = -1;
                while (true)
                {
                    using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                    readTimeout.CancelAfter(TimeSpan.FromSeconds(30));
                    count = await input.ReadAsync(buffer, readTimeout.Token);
                    if (count == 0) break;
                    total += count;
                    if (total > artifact.Size) throw new InvalidDataException($"{artifact.Name} exceeds the pinned download size.");
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellation);
                    var percent = (int)(total * 100 / artifact.Size);
                    if (percent != previousPercent) { progress?.Report(new($"Downloading {artifact.Name}… {percent}%", percent)); previousPercent = percent; }
                }
                if (total != artifact.Size) throw new InvalidDataException($"{artifact.Name} download was truncated.");
            }
            progress?.Report(new($"Verifying {artifact.Name}…"));
            if (!await Valid(temp, artifact, cancellation)) throw new IOException($"{artifact.Name} checksum does not match the pinned release. Nothing was installed.");
            cancellation.ThrowIfCancellationRequested();
            File.Move(temp, path, true);
            return path;
        }
        catch (OperationCanceledException exception) when (!callerCancellation.IsCancellationRequested)
        {
            throw new IOException($"{artifact.Name} download timed out. Retry setup; verified cached downloads will be reused.", exception);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private static async Task<bool> Valid(string path, DependencyArtifact artifact, CancellationToken cancellation)
    {
        if (new FileInfo(path).Length != artifact.Size) return false;
        await using var stream = File.OpenRead(path);
        var hash = artifact.Sha512 ? await SHA512.HashDataAsync(stream, cancellation) : await SHA256.HashDataAsync(stream, cancellation);
        return Convert.ToHexString(hash).Equals(artifact.Hash, StringComparison.OrdinalIgnoreCase);
    }
    public void Dispose() { if (ownsClient) client.Dispose(); }
}
