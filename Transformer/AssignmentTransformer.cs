using moodle_importer.Models;
using moodle_importer.Services;

namespace moodle_importer.Transformer;

public class AssignmentTransformer
{
    private readonly int _dueCutoffHour;
    private readonly List<string> _whitelist;
    private readonly List<string> _blacklist;

    public AssignmentTransformer(int dueCutoffHour = 4, List<string>? whitelist = null, List<string>? blacklist = null)
    {
        _dueCutoffHour = Math.Clamp(dueCutoffHour, 0, 23);
        _whitelist = whitelist ?? [];
        _blacklist = blacklist ?? EnvConfig.DefaultBlacklist.ToList();
    }

    public List<(MoodleAssignment Assignment, TransformedTask Task)> Transform(MoodleData moodleData)
    {
        var tasks = new List<(MoodleAssignment, TransformedTask)>();

        foreach (var assignment in moodleData.Assignments)
        {
            var cleanedTitle = CleanTitle(assignment.Title);
            if (string.IsNullOrWhiteSpace(cleanedTitle)) continue;

            if (IsValidAssignment(cleanedTitle))
            {
                var task = new TransformedTask
                {
                    Title = string.IsNullOrWhiteSpace(assignment.CourseName)
                        ? cleanedTitle
                        : $"{cleanedTitle} - {assignment.CourseName}",
                    Description = GenerateDescription(assignment),
                    DueDate = NormalizeDueDate(assignment.DueDate)
                };

                tasks.Add((assignment, task));
            }
        }

        return tasks;
    }

    private DateTime? NormalizeDueDate(DateTime? dueDate)
    {
        if (!dueDate.HasValue) return null;

        var value = dueDate.Value;
        if (_dueCutoffHour > 0 && value.TimeOfDay < TimeSpan.FromHours(_dueCutoffHour))
            return value.Date.AddDays(-1);

        return value;
    }

    private string CleanTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "";
        
        var cleaned = title
            .Replace("\n", " ")
            .Replace("\r", " ")
            .Replace("\t", " ")
            .Replace("\u00A0", " ")
            .Trim();
        
        while (cleaned.Contains("  "))
            cleaned = cleaned.Replace("  ", " ");
        
        return cleaned.Trim();
    }

    private bool IsValidAssignment(string title)
    {
        var lower = title.ToLower();

        if (_whitelist.Count > 0 && !_whitelist.Any(w => lower.Contains(w.ToLower())))
            return false;

        if (_blacklist.Any(b => lower.Contains(b.ToLower())))
            return false;

        return true;
    }

    private string GenerateDescription(MoodleAssignment assignment)
    {
        var desc = $"Moodle課題: {assignment.Title}";

        if (!string.IsNullOrWhiteSpace(assignment.CourseName))
        {
            desc += $"\nコース: {assignment.CourseName}";
        }

        if (assignment.DueDate.HasValue)
        {
            desc += $"\n期限: {assignment.DueDate:yyyy/MM/dd HH:mm}";
        }

        if (!string.IsNullOrWhiteSpace(assignment.Url))
        {
            desc += $"\nURL: {assignment.Url}";
        }

        return desc;
    }
}