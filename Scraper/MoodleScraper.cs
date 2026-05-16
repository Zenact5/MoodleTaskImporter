using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using Microsoft.Playwright;
using moodle_importer.Models;

namespace moodle_importer.Scraper;

public class MoodleScraper
{
    private readonly string _moodleUrl;
    private readonly string _username;
    private readonly string _password;

    public MoodleScraper(string moodleUrl, string username, string password)
    {
        _moodleUrl = moodleUrl;
        _username = username;
        _password = password;
    }

    public async Task<MoodleData> ScrapeAsync()
    {
        var data = new MoodleData { Username = _username, FetchedAt = DateTime.Now };
        var playwright = await Playwright.CreateAsync();

        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();

        try
        {
            Console.WriteLine("Moodle login...");
            await LoginMoodleAsync(page);
            await ParseCalendarUpcomingAsync(page, data);
        }
        finally
        {
            await page.CloseAsync();
            await context.CloseAsync();
            playwright.Dispose();
        }

        SaveToJson(data);
        return data;
    }

    private void SaveToJson(MoodleData data)
    {
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        var dataDir = Path.Combine(Directory.GetCurrentDirectory(), "data");
        if (!Directory.Exists(dataDir))
        {
            Directory.CreateDirectory(dataDir);
        }

        var filePath = Path.Combine(dataDir, "moodle_assignments.json");
        File.WriteAllText(filePath, json);
        Console.WriteLine($"Saved to: {filePath}");
    }

    private async Task LoginMoodleAsync(IPage page)
    {
        var loginUrl = $"{_moodleUrl}/login/index.php";
        await page.GotoAsync(loginUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForTimeoutAsync(3000);

        var html = await page.ContentAsync();
        Console.WriteLine($"Page has 'Shibboleth': {html.Contains("Shibboleth")}");

        var csLink = page.Locator("a[href*='Shibboleth'], a[href*='shibboleth']");
        if (await csLink.CountAsync() > 0)
        {
            var href = await csLink.First.GetAttributeAsync("href");
            Console.WriteLine($"Clicking: {href}");
            await csLink.First.ClickAsync();
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            await page.WaitForTimeoutAsync(10000);
        }

        Console.WriteLine($"After click URL: {page.Url}");

        if (page.Url.Contains("adfs") || page.Url.Contains("idp"))
        {
            Console.WriteLine("On ADFS/IdP - entering credentials...");
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

            var userInput = page.Locator("input[name='UserName'], input[name='username'], #userNameInput, input[type='text']");
            var passInput = page.Locator("input[name='Password'], input[name='password'], #passwordInput, input[type='password']");

            Console.WriteLine($"Username fields: {await userInput.CountAsync()}, Password fields: {await passInput.CountAsync()}");

            if (await userInput.CountAsync() > 0)
            {
                await userInput.First.FillAsync(_username);
                Console.WriteLine("Filled username");
            }
            if (await passInput.CountAsync() > 0)
            {
                await passInput.First.FillAsync(_password);
                Console.WriteLine("Filled password");
            }

            await page.WaitForTimeoutAsync(500);

            await page.EvaluateAsync("document.querySelector('form')?.submit()");
            Console.WriteLine("Submitted form via JS");

            for (int i = 0; i < 15; i++)
            {
                await page.WaitForTimeoutAsync(2000);
                if (page.Url.Contains("moodle") && !page.Url.Contains("login"))
                {
                    Console.WriteLine("Successfully logged in to Moodle!");
                    break;
                }
                Console.WriteLine($"Waiting... URL: {page.Url}");
            }
        }

        Console.WriteLine($"Final URL: {page.Url}");
    }

    private async Task ParseCalendarUpcomingAsync(IPage page, MoodleData data)
    {
        var calendarUrl = $"{_moodleUrl}/calendar/view.php?view=upcoming";
        await page.GotoAsync(calendarUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForTimeoutAsync(3000);

        Console.WriteLine($"Calendar page URL: {page.Url}");

        try
        {
            var rawJson = await page.EvaluateAsync<string>(@"() => {
                const containers = document.querySelectorAll('.event');
                const events = [];
                const seenIds = new Set();
                containers.forEach(el => {
                    const link = el.querySelector('a[href*=""mod/quiz/view.php""], a[href*=""mod/assign/view.php""]');
                    if (!link) return;
                    const href = link.getAttribute('href') || '';
                    const idMatch = href.match(/id=(\d+)/);
                    if (!idMatch) return;
                    const id = idMatch[1];
                    if (seenIds.has(id)) return;
                    seenIds.add(id);

                    const titleEl = el.querySelector('.card-title, .event-title, h5, h3');
                    const title = titleEl ? titleEl.textContent.replace(/\s+/g, ' ').trim() : '';

                    const allText = el.textContent.replace(/\s+/g, ' ').trim();
                    events.push([id, title, allText, href]);
                });
                return JSON.stringify(events);
            }");

            var rawEvents = JsonSerializer.Deserialize<List<List<string>>>(rawJson ?? "[]") ?? [];

            Console.WriteLine($"Found {rawEvents.Count} calendar events");

            foreach (var pair in rawEvents)
            {
                if (pair.Count < 4) continue;
                var id = pair[0];
                var title = pair[1];
                var allText = pair[2];
                var href = pair[3];

                if (string.IsNullOrWhiteSpace(title))
                {
                    var dateMatch = Regex.Match(allText, @"\d{4}\s*年");
                    if (dateMatch.Success)
                        title = allText[..dateMatch.Index].Trim();
                }

                title = CleanCalendarTitle(title);
                if (string.IsNullOrWhiteSpace(title)) continue;

                var dueDate = ParseDateText(allText);
                var courseName = ParseCourseNameFromCalendarText(allText);

                data.Assignments.Add(new MoodleAssignment
                {
                    Id = id,
                    Title = title,
                    CourseName = courseName ?? "",
                    DueDate = dueDate,
                    Url = href,
                    Status = "pending"
                });

                Console.WriteLine($"  [{id}] {title}");
                if (dueDate.HasValue)
                    Console.WriteLine($"    Due: {dueDate.Value:yyyy/MM/dd HH:mm}");
                if (!string.IsNullOrWhiteSpace(courseName))
                    Console.WriteLine($"    Course: {courseName}");
            }

            Console.WriteLine($"Total assignments: {data.Assignments.Count}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error parsing calendar: {ex.Message}");
        }
    }

    private static string CleanCalendarTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "";
        title = Regex.Replace(title, @"\s*の受験可能期間の(?:終了|開始)$", "");
        title = Regex.Replace(title, @"\s*(?:opens|closes)$", "", RegexOptions.IgnoreCase);
        return title.Trim();
    }

    private string? ParseCourseNameFromCalendarText(string text)
    {
        var match = Regex.Match(text, @"(20\d{2}[-‾]\S+(?:\s+\S+)*)\s+(?:活動に移動|問題を受験|提出課題)");
        if (match.Success)
        {
            var name = match.Groups[1].Value.Trim();
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        return null;
    }

    private string CleanTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "";
        return title.Replace("\n", " ").Replace("\r", " ").Replace("\t", " ").Replace("\u00A0", " ").Trim();
    }

    private DateTime? ParseDateText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        text = text.Trim();

        var result = TryParseDateExact(text);
        if (result.HasValue) return result;

        var jaFull = Regex.Match(text, @"(\d{4})年\s*(\d{1,2})月\s*(\d{1,2})日\s*(?:[\(（][^)）]*[\)）])?\s*,?\s*(\d{1,2})[:：](\d{2})");
        if (jaFull.Success)
        {
            return new DateTime(
                int.Parse(jaFull.Groups[1].Value),
                int.Parse(jaFull.Groups[2].Value),
                int.Parse(jaFull.Groups[3].Value),
                int.Parse(jaFull.Groups[4].Value),
                int.Parse(jaFull.Groups[5].Value), 0);
        }

        var jaDateOnly = Regex.Match(text, @"(\d{4})年\s*(\d{1,2})月\s*(\d{1,2})日");
        if (jaDateOnly.Success)
        {
            return new DateTime(
                int.Parse(jaDateOnly.Groups[1].Value),
                int.Parse(jaDateOnly.Groups[2].Value),
                int.Parse(jaDateOnly.Groups[3].Value), 23, 59, 0);
        }

        return null;
    }

    private DateTime? TryParseDateExact(string dateText)
    {
        if (string.IsNullOrWhiteSpace(dateText)) return null;

        var invariantFormats = new[] {
            "dddd, d MMMM yyyy, h:mm tt",
            "d MMMM yyyy, h:mm tt",
            "dddd, d MMMM yyyy, HH:mm",
            "d MMMM yyyy, HH:mm",
            "yyyy/MM/dd HH:mm",
            "yyyy-MM-dd HH:mm",
            "d MMMM yyyy",
            "dddd, d MMMM yyyy",
        };

        foreach (var fmt in invariantFormats)
        {
            if (DateTime.TryParseExact(dateText, fmt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                return date;
        }

        var jaCulture = new CultureInfo("ja-JP");
        var jaFormats = new[] {
            "yyyy'年'M'月'd'日('ddd')' H:mm",
            "yyyy'年'M'月'd'日('ddd')'",
            "yyyy'年'M'月'd'日('dddd')' H:mm",
            "yyyy'年'M'月'd'日('dddd')'",
            "yyyy'年'M'月'd'日' H:mm",
            "yyyy'年'M'月'd'日'",
            "M'月'd'日('ddd')' H:mm",
            "M'月'd'日('ddd')'",
            "M'月'd'日' H:mm",
            "M'月'd'日'",
        };

        foreach (var fmt in jaFormats)
        {
            if (DateTime.TryParseExact(dateText, fmt, jaCulture, DateTimeStyles.None, out var date))
                return date;
        }

        var thisYear = DateTime.Now.Year;
        foreach (var fmt in jaFormats.Where(f => !f.StartsWith("yyyy")))
        {
            var withYear = $"{thisYear}年{fmt}";
            if (DateTime.TryParseExact(dateText, withYear, jaCulture, DateTimeStyles.None, out var date))
                return date;
        }

        if (DateTime.TryParse(dateText, out var fallback))
            return fallback;

        return null;
    }
}
