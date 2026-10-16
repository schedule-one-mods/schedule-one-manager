using System.Text;
using System.Text.RegularExpressions;

namespace ScheduleI.Manager;

public static class SetupLogs
{
    public static string? Latest(string directory) => Directory.Exists(directory)
        ? Directory.EnumerateFiles(directory, "setup-*.log")
            .Where(p => Regex.IsMatch(Path.GetFileName(p), @"^setup-\d{8}-\d{6}-[a-f0-9]{8}\.log$"))
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal).FirstOrDefault()
        : null;

    public static IReadOnlyList<string> Related(string path)
    {
        var prefix = Path.GetFileNameWithoutExtension(path) + ".";
        return Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.log")
            .Where(p => Path.GetFileName(p).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static string Read(string path)
    {
        // Both our writer and Windows Installer may still have the file open.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        const int limit = 4 * 1024 * 1024;
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        var buffer = new char[limit];
        var count = reader.ReadBlock(buffer, 0, buffer.Length);
        return new string(buffer, 0, count) + (reader.Peek() >= 0 ? "\n[Preview truncated at 4 million characters.]" : "");
    }
}
