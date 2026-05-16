using Microsoft.Extensions.Configuration;
using dotenv.net;
using moodle_importer.Models;
using moodle_importer.Scraper;
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
            var scraper = new MoodleScraper(moodleUrl, moodleUsername, moodlePassword);
            Console.WriteLine("Step 1: Scraping Moodle assignments...");
            var moodleData = await scraper.ScrapeAsync();

            Console.WriteLine($"\nFound {moodleData.Assignments.Count} assignments");

            if (moodleData.Assignments.Count == 0)
            {
                Console.WriteLine("No assignments found.");
                return 0;
            }

            var transformer = new AssignmentTransformer();
            Console.WriteLine("Step 2: Transforming data...");
            var tasks = transformer.Transform(moodleData);

            Console.WriteLine($"Created {tasks.Count} tasks");

            var todoClient = new TodoClient(todoCliPath);
            Console.WriteLine("Step 3: Creating tasks in Microsoft Todo...");
            await todoClient.CreateTasksAsync(tasks);

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