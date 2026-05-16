namespace moodle_importer.Services;

public static class Logger
{
    public static bool IsDetail { get; set; } = false;

    public static void Info(string message)
    {
        Console.WriteLine(message);
    }

    public static void Detail(string message)
    {
        if (IsDetail)
            Console.WriteLine(message);
    }

    public static void Error(string message)
    {
        Console.Error.WriteLine(message);
    }
}
