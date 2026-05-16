using Microsoft.Extensions.Configuration;
using dotenv.net;
using moodle_importer.Models;
using moodle_importer.Scraper;
using moodle_importer.Services;
using moodle_importer.Storage;
using moodle_importer.Transformer;
using moodle_importer.Todo;

namespace moodle_importer;

class Program
{
    static async Task<int> Main(string[] args)
    {
        Logger.IsDetail = args.Contains("--detail");

        DotEnv.Load();

        var moodleUrl = Environment.GetEnvironmentVariable("MOODLE_URL") ?? "https://moodle41.lms.ehime-u.ac.jp/moodle";
        var moodleUsername = Environment.GetEnvironmentVariable("MOODLE_USERNAME") ?? "";
        var moodlePassword = Environment.GetEnvironmentVariable("MOODLE_PASSWORD") ?? "";
        var todoCliPath = Environment.GetEnvironmentVariable("TODO_CLI_PATH") ?? @".\Todo\todo.exe";
        var todoListName = Environment.GetEnvironmentVariable("TODO_LIST_NAME") ?? "Univ";

        if (string.IsNullOrWhiteSpace(moodleUsername) || string.IsNullOrWhiteSpace(moodlePassword))
        {
            Logger.Error("Error: MOODLE_USERNAME and MOODLE_PASSWORD must be set in .env");
            return 1;
        }

        if (!Path.IsPathRooted(todoCliPath))
        {
            todoCliPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), todoCliPath));
        }

        Logger.Detail($"Todo CLI: {todoCliPath}");
        Logger.Detail($"Todo List: {todoListName}");
        Logger.Detail($"Moodle URL: {moodleUrl}");
        Logger.Detail($"Username: {moodleUsername}");

        try
        {
            var dataPath = Path.Combine(Directory.GetCurrentDirectory(), "data", "moodle_assignments.json");
            var storage = new AssignmentStorage(dataPath);
            var diffService = new DiffService();
            var todoClient = new TodoClient(todoCliPath, todoListName);

            Logger.Detail("Loading existing data...");
            var existingData = await storage.LoadAsync();
            var existingAssignments = existingData?.Assignments ?? [];

            Logger.Info("Scraping Moodle calendar...");
            var scraper = new MoodleScraper(moodleUrl, moodleUsername, moodlePassword);
            var currentData = await scraper.ScrapeAsync();

            Logger.Detail($"Found {currentData.Assignments.Count} assignments");

            if (currentData.Assignments.Count == 0)
            {
                Logger.Info("No assignments found.");
                return 0;
            }

            Logger.Detail("Checking for unregistered assignments...");
            var unregistered = diffService.GetUnregistered(existingAssignments, currentData.Assignments);
            var newCount = 0;

            if (unregistered.Count > 0)
            {
                var newData = new MoodleData
                {
                    Assignments = unregistered,
                    Username = currentData.Username,
                    FetchedAt = currentData.FetchedAt
                };

                var transformer = new AssignmentTransformer();
                Logger.Detail("Transforming unregistered assignments...");
                var tasks = transformer.Transform(newData);

                Logger.Detail($"Created {tasks.Count} tasks");

                Logger.Detail("Creating tasks in Microsoft Todo...");
                for (int i = 0; i < unregistered.Count; i++)
                {
                    var success = await todoClient.CreateTaskAsync(tasks[i]);
                    if (success)
                    {
                        unregistered[i].Registered = true;
                        newCount++;
                    }
                }
            }

            Logger.Detail("Saving merged data...");
            var merged = diffService.Merge(
                existingData ?? new MoodleData(),
                currentData);
            await storage.SaveAsync(merged);

            Logger.Info($"Done! ({merged.Assignments.Count} assignments, {newCount} new)");
        }
        catch (Exception ex)
        {
            Logger.Error($"Error: {ex.Message}");
            Logger.Detail(ex.StackTrace ?? "");
            return 1;
        }

        return 0;
    }

    static IConfiguration BuildConfiguration()
    {
        return new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("Config/appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();
    }
}
