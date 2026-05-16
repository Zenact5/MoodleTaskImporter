using System.Diagnostics;
using moodle_importer.Models;

namespace moodle_importer.Todo;

public class TodoClient
{
    private readonly string _todoPath;
    private readonly string _listName;

    public TodoClient(string todoPath, string listName = "Univ")
    {
        _todoPath = todoPath;
        _listName = listName;
    }

    public async Task<bool> CreateTaskAsync(TransformedTask task)
    {
        var args = BuildArguments(task);

        var result = await ExecuteAsync(args);

        if (result.ExitCode != 0)
        {
            Console.WriteLine($"Error creating task: {result.Output}");
            return false;
        }

        Console.WriteLine($"Created task: {task.Title}");
        return true;
    }

    private string BuildArguments(TransformedTask task)
    {
        var list = string.IsNullOrWhiteSpace(task.ListName) ? _listName : task.ListName;
        var args = $"add item \"{task.Title}\" --list \"{list}\"";

        if (task.DueDate.HasValue)
        {
            args += $" --due-date \"{task.DueDate.Value:yyyy-MM-dd}\"";
        }

        return args;
    }

    private async Task<ProcessResult> ExecuteAsync(string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _todoPath,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        try
        {
            var process = Process.Start(startInfo);
            if (process == null)
            {
                return new ProcessResult { ExitCode = -1, Output = "Failed to start process" };
            }

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            return new ProcessResult
            {
                ExitCode = process.ExitCode,
                Output = (output + error).Trim()
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
