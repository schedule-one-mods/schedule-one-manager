namespace ScheduleI.Manager;

public sealed class ReviewEvidence
{
    public RegistryReview Review { get; }
    public string DirectoryPath { get; }
    public string? IndexSha256 { get; private set; }
    private readonly List<object> events = [];
    public ReviewEvidence(RegistryReview review) : this(review, Path.Combine(review.SessionDirectory, "Evidence", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"))) { }
    internal ReviewEvidence(RegistryReview review, string directory)
    {
        Review = review;
        DirectoryPath = directory;
        Directory.CreateDirectory(DirectoryPath);
        Record("Review opened", new { ManagerVersion = typeof(RegistryClient).Assembly.GetName().Version?.ToString() });
    }
    public void SaveIndex(RegistryClient client)
    {
        IndexSha256 = client.LastIndexSha256;
        if (client.LastIndexBytes != null) File.WriteAllBytes(Path.Combine(DirectoryPath, "index.json"), client.LastIndexBytes);
        Record("Index fetched", new { IndexSha256 });
    }
    public void Record(string action, object? detail = null)
    {
        events.Add(new { AtUtc = DateTimeOffset.UtcNow, Action = action, UnverifiedGameVersionAllowed = MaintainerCompatibility.AllowUnverifiedGameVersion, Detail = detail });
        JsonFiles.Write(Path.Combine(DirectoryPath, "report.json"), new
        {
            Review, IndexSha256, UnverifiedGameVersionAllowed = MaintainerCompatibility.AllowUnverifiedGameVersion,
            Meaning = "Startup observations are not gameplay or compatibility certification. Review loader logs and test gameplay manually.",
            Events = events
        });
    }
    public string CaptureLoaderLog(GameInstallation game, string label)
    {
        var source = Path.Combine(game.Folder, "MelonLoader", "Latest.log");
        if (!File.Exists(source)) return "Loader log missing";
        var name = label + "-" + Guid.NewGuid().ToString("N") + ".log";
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var output = File.Create(Path.Combine(DirectoryPath, name));
        input.CopyTo(output);
        return name;
    }
    public static object Package(LibraryPackage package) => new { package.Identity, package.FileName, DllSha256 = package.Sha256, package.Compatibility };
}
