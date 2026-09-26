using System.Text.RegularExpressions;

namespace moodle_importer.Services;

public static class EnvConfig
{
    public const int CurrentVersion = 3;
    public const string VersionPrefix = "# config-version:";

    public static readonly string[] DefaultBlacklist = ["開始", "opens"];

    public record EnvVar(string Key, string Default, string? Comment);
    public record DeprecatedVar(string Key, int SinceVersion, string Note);

    public static readonly EnvVar[] Schema =
    [
        new("MOODLE_URL", "https://moodle41.lms.ehime-u.ac.jp/moodle", null),
        new("MOODLE_USERNAME", "your_username", null),
        new("MOODLE_PASSWORD", "your_password", null),
        new("TODO_CLI_PATH", @".\Todo\todo.exe", null),
        new("TODO_LIST_NAME", "Univ", null),
        new("DUE_CUTOFF_HOUR", "4",
            "Due times earlier than this hour (0-23) are registered as the previous day (0 = disabled)"),
        new("TITLE_WHITELIST", "",
            "Comma-separated title filters (case-insensitive partial match)\nIf not empty, only titles containing any entry are registered"),
        new("TITLE_BLACKLIST", string.Join(",", DefaultBlacklist),
            "Comma-separated; assignments whose title contains any entry are skipped (case-insensitive)\nSet to an empty value to disable blacklist filtering"),
    ];

    public static DeprecatedVar[] Deprecated { get; set; } = [];

    public record MigrationResult(
        bool Changed,
        int OldVersion,
        string Text,
        List<string> AddedKeys,
        List<string> DeprecatedKeys);

    public static string GenerateTemplate()
    {
        var lines = new List<string>
        {
            "# Moodle Importer Configuration",
            $"{VersionPrefix} {CurrentVersion}",
        };

        foreach (var v in Schema)
        {
            lines.Add("");
            lines.AddRange(RenderComment(v.Comment));
            lines.Add($"{v.Key}={v.Default}");
        }

        lines.Add("");
        return string.Join(Environment.NewLine, lines);
    }

    public static int DetectVersion(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith(VersionPrefix)) continue;
            var rest = trimmed[VersionPrefix.Length..].Trim();
            if (int.TryParse(rest, out var version)) return version;
        }
        return 1;
    }

    public static MigrationResult Migrate(string existingText)
    {
        var lines = new List<string>(existingText.Replace("\r\n", "\n").Split('\n'));
        var oldVersion = DetectVersion(lines);
        var added = new List<string>();
        var deprecated = new List<string>();

        if (oldVersion >= CurrentVersion)
            return new MigrationResult(false, oldVersion, existingText, added, deprecated);

        var activeKeys = GetActiveKeys(lines);

        ApplyVersionFixes(lines, oldVersion);

        foreach (var dep in Deprecated)
        {
            if (!activeKeys.Contains(dep.Key)) continue;

            for (int i = 0; i < lines.Count; i++)
            {
                if (IsActiveKeyLine(lines[i], dep.Key))
                {
                    lines[i] = $"# [deprecated since v{dep.SinceVersion}: {dep.Note}] {lines[i]}";
                    deprecated.Add(dep.Key);
                }
            }
            activeKeys.Remove(dep.Key);
        }

        var appended = new List<string>();
        foreach (var v in Schema)
        {
            if (activeKeys.Contains(v.Key)) continue;

            if (appended.Count > 0) appended.Add("");
            appended.AddRange(RenderComment(v.Comment));
            appended.Add($"{v.Key}={v.Default}");
            added.Add(v.Key);
        }

        var versionLine = $"{VersionPrefix} {CurrentVersion}";
        var idx = lines.FindIndex(l => l.Trim().StartsWith(VersionPrefix));
        if (idx >= 0)
            lines[idx] = versionLine;
        else
            lines.Insert(0, versionLine);

        if (appended.Count > 0)
        {
            if (lines.Count > 0 && lines[^1].Trim() != "") lines.Add("");
            lines.Add($"# --- added by migration to config v{CurrentVersion} ---");
            lines.AddRange(appended);
        }

        return new MigrationResult(
            true, oldVersion, string.Join(Environment.NewLine, lines), added, deprecated);
    }

    private static void ApplyVersionFixes(List<string> lines, int oldVersion)
    {
        if (oldVersion < 3)
        {
            var idx = lines.FindIndex(l => Regex.IsMatch(l, @"^TITLE_BLACKLIST\s*=\s*$"));
            if (idx >= 0)
                lines[idx] = $"TITLE_BLACKLIST={string.Join(",", DefaultBlacklist)}";
        }
    }

    private static HashSet<string> GetActiveKeys(IEnumerable<string> lines)
    {
        var keys = new HashSet<string>();
        foreach (var line in lines)
        {
            var match = Regex.Match(line, @"^([A-Za-z_][A-Za-z0-9_]*)\s*=");
            if (match.Success) keys.Add(match.Groups[1].Value);
        }
        return keys;
    }

    private static bool IsActiveKeyLine(string line, string key) =>
        Regex.IsMatch(line, $@"^{Regex.Escape(key)}\s*=");

    private static IEnumerable<string> RenderComment(string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment)) yield break;

        foreach (var line in comment.Split('\n'))
            yield return line.Length == 0 ? "#" : $"# {line}";
    }
}
