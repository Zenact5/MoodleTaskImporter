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

        var baseDir = GetBaseDirectory();
        Directory.SetCurrentDirectory(baseDir);

        if (args.Contains("--init"))
        {
            await InitAsync();
            return 0;
        }

        DotEnv.Load(new DotEnvOptions()
            .WithEnvFiles(Path.Combine(baseDir, ".env"))
            .WithOverwriteExistingVars());

        var envFilePath = Path.Combine(baseDir, ".env");
        if (File.Exists(envFilePath))
        {
            var envVersion = EnvConfig.DetectVersion(await File.ReadAllLinesAsync(envFilePath));
            if (envVersion < EnvConfig.CurrentVersion)
                Logger.Info($"Warning: .env is outdated (config v{envVersion}, current v{EnvConfig.CurrentVersion}). Run with --init to update.");
        }

        var moodleUrl = Environment.GetEnvironmentVariable("MOODLE_URL") ?? "https://moodle41.lms.ehime-u.ac.jp/moodle";
        var moodleUsername = Environment.GetEnvironmentVariable("MOODLE_USERNAME") ?? "";
        var moodlePassword = Environment.GetEnvironmentVariable("MOODLE_PASSWORD") ?? "";
        var todoCliPath = Environment.GetEnvironmentVariable("TODO_CLI_PATH") ?? @".\Todo\todo.exe";
        var todoListName = Environment.GetEnvironmentVariable("TODO_LIST_NAME") ?? "Univ";
        var dueCutoffHour = ParseCutoffHour(Environment.GetEnvironmentVariable("DUE_CUTOFF_HOUR"));
        var titleWhitelist = ParseList(Environment.GetEnvironmentVariable("TITLE_WHITELIST"));
        var blacklistEnv = Environment.GetEnvironmentVariable("TITLE_BLACKLIST");
        var titleBlacklist = blacklistEnv == null
            ? EnvConfig.DefaultBlacklist.ToList()
            : ParseList(blacklistEnv);

        if (string.IsNullOrWhiteSpace(moodleUsername) || string.IsNullOrWhiteSpace(moodlePassword))
        {
            Logger.Error("Error: MOODLE_USERNAME and MOODLE_PASSWORD must be set in .env");
            return 1;
        }

        if (!Path.IsPathRooted(todoCliPath))
        {
            todoCliPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), todoCliPath));
        }

        if (!File.Exists(todoCliPath))
        {
            Logger.Error($"Error: todo.exe not found at '{todoCliPath}'. Place it in the Todo\\ folder or set TODO_CLI_PATH in .env");
            return 1;
        }

        Logger.Detail($"Todo CLI: {todoCliPath}");
        Logger.Detail($"Todo List: {todoListName}");
        Logger.Detail($"Moodle URL: {moodleUrl}");
        Logger.Detail($"Username: {moodleUsername}");
        Logger.Detail($"Due cutoff hour: {dueCutoffHour}");
        Logger.Detail($"Title whitelist: [{string.Join(", ", titleWhitelist)}]");
        Logger.Detail($"Title blacklist: [{string.Join(", ", titleBlacklist)}]");

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

                var transformer = new AssignmentTransformer(dueCutoffHour, titleWhitelist, titleBlacklist);
                Logger.Detail("Transforming unregistered assignments...");
                var tasks = transformer.Transform(newData);

                Logger.Detail($"Created {tasks.Count} tasks");

                Logger.Detail("Creating tasks in Microsoft Todo...");
                foreach (var (assignment, task) in tasks)
                {
                    var success = await todoClient.CreateTaskAsync(task);
                    if (success)
                    {
                        assignment.Registered = true;
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

    static int ParseCutoffHour(string? value)
    {
        const int defaultValue = 4;
        if (string.IsNullOrWhiteSpace(value)) return defaultValue;

        if (int.TryParse(value, out var hour) && hour >= 0 && hour <= 23)
            return hour;

        Logger.Error($"Warning: invalid DUE_CUTOFF_HOUR '{value}' (expected 0-23). Using default {defaultValue}.");
        return defaultValue;
    }

    static List<string> ParseList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];

        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    static string GetBaseDirectory()
    {
        var dir = AppContext.BaseDirectory;
        var test = dir;
        while (test != null && !Directory.GetFiles(test, "*.csproj").Any())
            test = Path.GetDirectoryName(test);
        return test ?? dir;
    }

    static async Task InitAsync()
    {
        var todoDir = Path.Combine(Directory.GetCurrentDirectory(), "Todo");
        const string envPath = ".env";

        if (!File.Exists(envPath))
        {
            await File.WriteAllTextAsync(envPath, EnvConfig.GenerateTemplate());
            Logger.Info($"Created .env template (config v{EnvConfig.CurrentVersion}). Edit it with your credentials.");
        }
        else
        {
            var existing = await File.ReadAllTextAsync(envPath);
            var result = EnvConfig.Migrate(existing);

            if (!result.Changed)
            {
                Logger.Info($".env is up to date (config v{EnvConfig.CurrentVersion}).");
            }
            else
            {
                File.Copy(envPath, envPath + ".bak", overwrite: true);
                await File.WriteAllTextAsync(envPath, result.Text);

                Logger.Info($"Migrated .env from v{result.OldVersion} to v{EnvConfig.CurrentVersion} (backup: .env.bak)");
                foreach (var key in result.AddedKeys)
                    Logger.Info($"  + {key} (added with default value)");
                foreach (var key in result.DeprecatedKeys)
                    Logger.Info($"  - {key} (deprecated, commented out)");
            }
        }

        if (!Directory.Exists(todoDir))
        {
            Directory.CreateDirectory(todoDir);
        }

        var todoPath = Path.Combine(todoDir, "todo.exe");
        if (!File.Exists(todoPath))
        {
            Logger.Info($"Place todo.exe in: {todoPath}");
        }
        else
        {
            Logger.Info("todo.exe found.");
        }
    }
}
