using Microsoft.Win32;
using System.Runtime.Versioning;

namespace ScheduleI.Manager;

public static class SystemPrerequisites
{
    public static readonly Version MinimumDotNet = new(6, 0, 36);
    public static readonly Version MinimumVisualCpp = new(14, 29, 30139);
    public static bool RestartStillRequired(DateTimeOffset? requestedAt) => requestedAt != null && DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64) <= requestedAt;

    public static bool HasDesktopRuntime6() => DotNetRoots().Any(IsRuntimeRootReady);
    internal static bool IsRuntimeRootReady(string root)
    {
        if (!IsX64Pe(Path.Combine(root, "dotnet.exe"))) return false;
        var core = Path.Combine(root, "shared", "Microsoft.NETCore.App");
        var desktop = Path.Combine(root, "shared", "Microsoft.WindowsDesktop.App");
        var host = Path.Combine(root, "host", "fxr");
        bool Has(string parent, params string[] files) => Directory.Exists(parent) && Directory.EnumerateDirectories(parent).Any(d =>
            Version.TryParse(Path.GetFileName(d), out var version) && version.Major == 6 && version >= MinimumDotNet && files.All(f => File.Exists(Path.Combine(d, f)) && new FileInfo(Path.Combine(d, f)).Length > 0 && (f is not ("coreclr.dll" or "hostfxr.dll") || IsX64Pe(Path.Combine(d, f)))));
        return Has(core, "coreclr.dll", "System.Private.CoreLib.dll") && Has(desktop, "PresentationFramework.dll", "WindowsBase.dll") && Has(host, "hostfxr.dll");
    }
    private static IEnumerable<string> DotNetRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet")
        };
        if (OperatingSystem.IsWindows())
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
            using var key = machine.OpenSubKey(@"SOFTWARE\dotnet\Setup\InstalledVersions\x64");
            if (key?.GetValue("InstallLocation") is string location) roots.Add(location);
        }
        // DOTNET_ROOT is not used: a private manager/SDK runtime is not necessarily visible to MelonLoader.
        return roots;
    }
    public static bool HasVisualCppX64()
    {
        if (!OperatingSystem.IsWindows()) return false;
        return HasVisualCppInRegistry();
    }
    [SupportedOSPlatform("windows")]
    private static bool HasVisualCppInRegistry()
    {
        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var key = machine.OpenSubKey(@"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64");
            if (key?.GetValue("Installed") is int installed && installed == 1 && Version.TryParse((key.GetValue("Version") as string)?.TrimStart('v'), out var version) && version >= MinimumVisualCpp) return true;
        }
        return false;
    }
    internal static bool IsX64Pe(string file)
    {
        if (!File.Exists(file)) return false;
        using var stream = File.OpenRead(file);
        using var reader = new BinaryReader(stream);
        if (stream.Length < 64 || reader.ReadUInt16() != 0x5A4D) return false;
        stream.Position = 0x3C;
        var offset = reader.ReadInt32();
        if (offset < 0 || offset > stream.Length - 6) return false;
        stream.Position = offset;
        return reader.ReadUInt32() == 0x4550 && reader.ReadUInt16() == 0x8664;
    }
}
