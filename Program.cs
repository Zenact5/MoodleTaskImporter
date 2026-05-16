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
        var todoCliPath = Environment.GetEnvironmentVariable("TODO_CLI_PATH") ?? @"E:\file\デスクトップ\Projects Folder\.NET\moodle_importer\Todo\todo.exe";

        if (string.IsNullOrWhiteSpace(moodleUsername) || string.IsNullOrWhiteSpace(moodlePassword))
        {
            Console.WriteLine("Error: MOODLE_USERNAME and MOODLE_PASSWORD must be set in .env");
            return 1;
        }

        Console.WriteLine($"Moodle URL: {moodleUrl}");
        Console.WriteLine($"Username: {moodleUsername}");
        Console.WriteLine();

        try
        {
            var dataPath = Path.Combine(Directory.GetCurrentDirectory(), "data", "moodle_assignments.json");
            var storage = new AssignmentStorage(dataPath);
            var diffService = new DiffService();

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

            // Step 3: Diff — find new assignments only
            Console.WriteLine("\nStep 3: Checking for new assignments...");
            var newAssignments = diffService.GetNewAssignments(existingAssignments, currentData.Assignments);

            if (newAssignments.Count == 0)
            {
                Console.WriteLine("No new assignments to register.");
            }
            else
            {
                // Step 4: Transform new assignments
                var newData = new MoodleData
                {
                    Assignments = newAssignments,
                    Username = currentData.Username,
                    FetchedAt = currentData.FetchedAt
                };

                var transformer = new AssignmentTransformer();
                Console.WriteLine("Step 4: Transforming new assignments...");
                var tasks = transformer.Transform(newData);

                Console.WriteLine($"Created {tasks.Count} new tasks");

                // Step 5: Register in Microsoft Todo
                var todoClient = new TodoClient(todoCliPath);
                Console.WriteLine("Step 5: Creating tasks in Microsoft Todo...");
                await todoClient.CreateTasksAsync(tasks);
            }

            // Step 6: Merge and save
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
