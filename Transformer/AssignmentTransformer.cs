using moodle_importer.Models;

namespace moodle_importer.Transformer;

public class AssignmentTransformer
{
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
                    DueDate = assignment.DueDate
                };

                tasks.Add((assignment, task));
            }
        }

        return tasks;
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
        
        if (lower.Contains("開始") || lower.Contains("opens"))
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