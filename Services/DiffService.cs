using moodle_importer.Models;

namespace moodle_importer.Services;

public class DiffService
{
    public List<MoodleAssignment> GetNewAssignments(
        List<MoodleAssignment> existing,
        List<MoodleAssignment> current)
    {
        var existingIds = new HashSet<string>(
            existing.Select(a => a.Id).Where(id => !string.IsNullOrWhiteSpace(id)));

        var newAssignments = current
            .Where(a => !string.IsNullOrWhiteSpace(a.Id) && !existingIds.Contains(a.Id))
            .ToList();

        Console.WriteLine($"New assignments: {newAssignments.Count} (out of {current.Count})");
        return newAssignments;
    }

    public MoodleData Merge(MoodleData existing, MoodleData current)
    {
        var merged = new MoodleData
        {
            Username = current.Username,
            FetchedAt = current.FetchedAt
        };

        var mergedById = new Dictionary<string, MoodleAssignment>();

        foreach (var a in existing.Assignments)
        {
            if (!string.IsNullOrWhiteSpace(a.Id))
                mergedById[a.Id] = a;
        }

        foreach (var a in current.Assignments)
        {
            if (!string.IsNullOrWhiteSpace(a.Id))
                mergedById[a.Id] = a;
        }

        merged.Assignments = mergedById.Values.ToList();
        return merged;
    }
}
