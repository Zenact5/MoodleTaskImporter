namespace moodle_importer.Models;

public class MoodleAssignment
{
    public string Id { get; set; } = string.Empty;
    public string CourseName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime? DueDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public bool Registered { get; set; }
}

public class MoodleData
{
    public List<MoodleAssignment> Assignments { get; set; } = new();
    public string Username { get; set; } = string.Empty;
    public DateTime FetchedAt { get; set; }
}

public class TransformedTask
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime? DueDate { get; set; }
    public string ListName { get; set; } = string.Empty;
}