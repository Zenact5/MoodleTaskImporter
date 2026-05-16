using System.Diagnostics;
using moodle_importer.Models;

namespace moodle_importer.Todo;

public class TodoClient
{
    private readonly string _todoPath;

    public TodoClient(string todoPath = @"E:\file\デスクトップ\Projects Folder\.NET\moodle_importer\Todo\todo.exe")
    {
        _todoPath = todoPath;
    }

    public async Task CreateTaskAsync(TransformedTask task)
    {
        var args = BuildArguments(task);
        
        var result = await ExecuteAsync(args);
        
        if (result.ExitCode != 0)
        {
            Console.WriteLine($"Error creating task: {result.Output}");
            return;
        }
        
        Console.WriteLine($"Created task: {task.Title}");
    }

    public async Task CreateTasksAsync(List<TransformedTask> tasks)
    {
        foreach (var task in tasks)
        {
            await CreateTaskAsync(task);
        }
    }

    private string BuildArguments(TransformedTask task)
    {
        var listName = string.IsNullOrWhiteSpace(task.ListName) ? "Univ" : task.ListName;
        
        var args = $"--list \"{listName}\" add \"{task.Title}\"";
        
        if (task.DueDate.HasValue)
        {
            args += $" --due \"{task.DueDate.Value:yyyy-MM-ddTHH:mm:ssZ}\"";
        }
        
        if (!string.IsNullOrWhiteSpace(task.Description))
        {
            args += $" --body \"{task.Description}\"";
        }
        
        return args;
    }

    private async Task<ProcessResult> ExecuteAsync(string arguments)
    {
        var workDir = Path.GetDirectoryName(_todoPath) ?? Directory.GetCurrentDirectory();
        
        var startInfo = new ProcessStartInfo
        {
            FileName = _todoPath,
            Arguments = arguments,
            UseShellExecute = true,
            CreateNoWindow = true,
            WorkingDirectory = workDir
        };

        try
        {
            var process = Process.Start(startInfo);
            if (process == null)
            {
                return new ProcessResult { ExitCode = -1, Output = "Failed to start process" };
            }

            await process.WaitForExitAsync();
            
            return new ProcessResult 
            { 
                ExitCode = process.ExitCode, 
                Output = "" 
            };
        }
        catch (Exception ex)
        {
            return new ProcessResult { ExitCode = -1, Output = ex.Message };
        }
    }
}

public class ProcessResult
{
    public int ExitCode { get; set; }
    public string Output { get; set; } = string.Empty;
}