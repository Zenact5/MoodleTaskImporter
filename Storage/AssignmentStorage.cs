using System.Text.Json;
using moodle_importer.Models;
using moodle_importer.Services;

namespace moodle_importer.Storage;

public class AssignmentStorage
{
    private readonly string _filePath;

    public AssignmentStorage(string filePath)
    {
        _filePath = filePath;
    }

    public async Task<MoodleData?> LoadAsync()
    {
        if (!File.Exists(_filePath))
        {
            Logger.Detail("No existing data found.");
            return null;
        }

        var json = await File.ReadAllTextAsync(_filePath);
        var data = JsonSerializer.Deserialize<MoodleData>(json);
        Logger.Detail($"Loaded {data?.Assignments.Count ?? 0} existing assignments");
        return data;
    }

    public async Task SaveAsync(MoodleData data)
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        await File.WriteAllTextAsync(_filePath, json);
        Logger.Detail($"Saved {data.Assignments.Count} assignments to {_filePath}");
    }
}
