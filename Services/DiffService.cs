using moodle_importer.Models;

namespace moodle_importer.Services;

public class DiffService
{
    public List<MoodleAssignment> GetUnregistered(
        List<MoodleAssignment> existing,
        List<MoodleAssignment> current)
    {
        var registeredIds = existing
            .Where(a => a.Registered && !string.IsNullOrWhiteSpace(a.Id))
            .Select(a => a.Id)
            .ToHashSet();

        var unregistered = current
            .Where(a => !string.IsNullOrWhiteSpace(a.Id) && !registeredIds.Contains(a.Id))
            .ToList();

        Logger.Detail($"Unregistered assignments: {unregistered.Count} (out of {current.Count})");
        return unregistered;
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
            {
                if (mergedById.TryGetValue(a.Id, out var existingA))
                {
                    a.Registered = a.Registered || existingA.Registered;
                }
                mergedById[a.Id] = a;
            }
        }

        merged.Assignments = mergedById.Values.ToList();
        return merged;
    }
}
