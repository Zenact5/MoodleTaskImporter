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
        Console.WriteLine("Moodle to Microsoft Todo Importer");
        Console.WriteLine("====================================");

        DotEnv.Load();

        var moodleUrl = Environment.GetEnvironmentVariable("MOODLE_URL") ?? "https://moodle41.lms.ehime-u.ac.jp/moodle";
        var moodleUsername = Environment.GetEnvironmentVariable("MOODLE_USERNAME") ?? "";
        var moodlePassword = Environment.GetEnvironmentVariable("MOODLE_PASSWORD") ?? "";
        var todoCliPath = Environment.GetEnvironmentVariable("TODO_CLI_PATH") ?? @".\Todo\todo.exe";
        var todoListName = Environment.GetEnvironmentVariable("TODO_LIST_NAME") ?? "Univ";

        if (string.IsNullOrWhiteSpace(moodleUsername) || string.IsNullOrWhiteSpace(moodlePassword))
        {
            Console.WriteLine("Error: MOODLE_USERNAME and MOODLE_PASSWORD must be set in .env");
            return 1;
        }

        if (!Path.IsPathRooted(todoCliPath))
        {
            todoCliPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), todoCliPath));
        }
        Console.WriteLine($"Todo CLI: {todoCliPath}");
        Console.WriteLine($"Todo List: {todoListName}");
        Console.WriteLine($"Moodle URL: {moodleUrl}");
        Console.WriteLine($"Username: {moodleUsername}");
        Console.WriteLine();

        try
        {
            var dataPath = Path.Combine(Directory.GetCurrentDirectory(), "data", "moodle_assignments.json");
            var storage = new AssignmentStorage(dataPath);
            var diffService = new DiffService();
            var todoClient = new TodoClient(todoCliPath, todoListName);

            // Step 1: Load existing data
            Console.WriteLine("Step 1: Loading existing data...");
            var existingData = await storage.LoadAsync();
            var existingAssignments = existingData?.Assignments ?? [];

            // Step 2: Scrape
            var scraper = new MoodleScraper(moodleUrl, moodleUsername, moodlePassword);
            Console.WriteLine("\nStep 2: Scraping Moodle calendar...");
            var currentData = await scraper.ScrapeAsync();

            Console.WriteLine($"\nFound {currentData.Assignments.Count} assignments");

            if (currentData.Assignments.Count == 0)
            {
                Console.WriteLine("No assignments found.");
                return 0;
            }

            // Step 3: Diff — unregistered only (by Registered flag)
            Console.WriteLine("\nStep 3: Checking for unregistered assignments...");
            var unregistered = diffService.GetUnregistered(existingAssignments, currentData.Assignments);

            if (unregistered.Count == 0)
            {
                Console.WriteLine("No unregistered assignments.");
            }
            else
            {
                var newData = new MoodleData
                {
                    Assignments = unregistered,
                    Username = currentData.Username,
                    FetchedAt = currentData.FetchedAt
                };

                var transformer = new AssignmentTransformer();
                Console.WriteLine("Step 4: Transforming unregistered assignments...");
                var tasks = transformer.Transform(newData);

                Console.WriteLine($"Created {tasks.Count} tasks");

                // Step 5: Register each, mark success
                Console.WriteLine("Step 5: Creating tasks in Microsoft Todo...");
                for (int i = 0; i < unregistered.Count; i++)
                {
                    var success = await todoClient.CreateTaskAsync(tasks[i]);
                    if (success)
                    {
                        unregistered[i].Registered = true;
                    }
                }
            }

            // Step 6: Merge and save (preserves Registered flags)
            Console.WriteLine("\nStep 6: Saving merged data...");
            var merged = diffService.Merge(
                existingData ?? new MoodleData(),
                currentData);
            await storage.SaveAsync(merged);

            Console.WriteLine("\nDone!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
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
