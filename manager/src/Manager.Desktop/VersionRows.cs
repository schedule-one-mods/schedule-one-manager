using System.ComponentModel;
using ScheduleI.Manager;

namespace ScheduleI.Manager.Desktop;

public sealed class VersionChoice<T>(T value, string label)
{
    public T Value { get; } = value;
    public string Label { get; } = label;
}
public abstract class VersionRow<T> : INotifyPropertyChanged
{
    public required string Name { get; init; }
    public required IReadOnlyList<VersionChoice<T>> Versions { get; init; }
    private VersionChoice<T>? selected;
    public VersionChoice<T>? Selected
    {
        get => selected;
        set { selected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null)); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}
public sealed record LibraryVersionState(string Status, string Action, bool CanToggle, string ToggleReason);
public sealed class LibraryModRow : VersionRow<LibraryPackage>
{
    public required Func<LibraryPackage, LibraryVersionState> Inspect { get; init; }
    public LibraryPackage Package => Selected!.Value;
    public string Version => Package.Manifest.Version;
    private LibraryVersionState State => Inspect(Package);
    public string Status => State.Status + "\nTested: " + string.Join(", ", Package.Compatibility.GameVersions) +
        " · " + Package.Compatibility.Backend + " · MelonLoader " + string.Join(", ", Package.Compatibility.Loader.Versions);
    public string Action => State.Action;
    public bool CanToggle => State.CanToggle;
    public string ToggleReason => State.ToggleReason;
    public LibraryPackage[] AllPackages => Versions.Select(v => v.Value).ToArray();
}
public sealed class RegistryModRow : VersionRow<RegistryRelease>
{
    public required RegistryMod Entry { get; init; }
    public required Func<RegistryRelease, bool> IsDownloaded { get; init; }
    public GameInstallation? Game { get; init; }
    public string Author => Entry.Author;
    public RegistryRelease? Mod => Selected?.Value;
    public string Version => Mod?.Version ?? "";
    public string Action => Mod == null ? "Unavailable" : IsDownloaded(Mod) ? "Downloaded" : "Download";
    public bool CanDownload => Mod != null && !IsDownloaded(Mod);
    public string Compatibility => Mod == null ? Entry.ReleaseError ?? "No published packages" :
        Mod.Compatibility.Backend + " · Game " + string.Join(", ", Mod.Compatibility.GameVersions) +
        " · MelonLoader " + string.Join(", ", Mod.Compatibility.Loader.Versions) +
        (Game != null && (Game.Backend != Mod.Compatibility.Backend || !Mod.Compatibility.GameVersions.Contains(Game.Version) || !Mod.Compatibility.Loader.Versions.Contains(Game.LoaderVersion)) ? " · Unverified for this game" : "");
    public string? ReleaseError => Entry.ReleaseError;
}
