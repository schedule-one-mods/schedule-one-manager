using System.Text.Json;
using System.Text.RegularExpressions;

namespace ScheduleI.Manager;

public static class MaintainerCompatibility
{
    // Process-local, never saved as a package compatibility claim.
    public static bool AllowUnverifiedGameVersion { get; set; }
}

public sealed record RegistryReview(int Number, string PullRequestUrl, string Repository, string Commit)
{
    public string IndexUrl => $"https://api.github.com/repos/{Repository}/contents/index.json?ref={Commit}";
    public string RawIndexUrl => $"https://raw.githubusercontent.com/{Repository}/{Commit}/index.json";
    public string SessionDirectory => Path.Combine(ManagerPaths.Root, "Reviews", $"pr-{Number}-{Commit}");
}

public sealed partial class RegistryClient
{
    public byte[]? LastIndexBytes { get; private set; }
    public string? LastIndexSha256 { get; private set; }
    private RegistryClient(RegistryReview review, HttpClient? http) : this(http)
    {
        indexUrl = review.IndexUrl;
        rawIndexUrl = review.RawIndexUrl;
    }
    public static RegistryClient ForReview(RegistryReview review, HttpClient? http = null)
    {
        if (!Regex.IsMatch(review.Repository, @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$") || !Regex.IsMatch(review.Commit, "^[a-f0-9]{40}$"))
            throw new InvalidDataException("Review repository or commit is invalid.");
        return new(review, http);
    }
    public async Task<RegistryReview> ResolveReview(string url, CancellationToken cancellation = default)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "github.com" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 ||
            !Regex.IsMatch(uri.AbsolutePath, @"^/schedule-one-mods/schedule-one-mod-registry/pull/[1-9][0-9]*(/(files|commits|checks))?/?$", RegexOptions.IgnoreCase) ||
            !int.TryParse(uri.AbsolutePath.TrimEnd('/').Split('/')[4], out var number))
            throw new InvalidDataException("Paste a GitHub PR URL for schedule-one-mods/schedule-one-mod-registry.");
        var bytes = await FetchBytes($"https://api.github.com/repos/schedule-one-mods/schedule-one-mod-registry/pulls/{number}", cancellation, "application/vnd.github+json");
        using var json = JsonDocument.Parse(bytes);
        var root = json.RootElement;
        var target = root.GetProperty("base").GetProperty("repo").GetProperty("full_name").GetString();
        if (!string.Equals(target, "schedule-one-mods/schedule-one-mod-registry", StringComparison.OrdinalIgnoreCase) || root.GetProperty("number").GetInt32() != number)
            throw new InvalidDataException("GitHub returned a different registry PR.");
        var head = root.GetProperty("head");
        var repository = head.GetProperty("repo");
        if (repository.ValueKind == JsonValueKind.Null) throw new InvalidDataException("The PR source repository was deleted.");
        var review = new RegistryReview(number, $"https://github.com/schedule-one-mods/schedule-one-mod-registry/pull/{number}",
            repository.GetProperty("full_name").GetString() ?? "", head.GetProperty("sha").GetString() ?? "");
        using var validation = ForReview(review, client);
        return review;
    }
}
