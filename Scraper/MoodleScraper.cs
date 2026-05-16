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

            await FetchAssignmentsAsync(page, data);

            if (data.Assignments.Count > 0)
            {
                await ParseCalendarUpcomingAsync(page, data);
            }
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

    private async Task FetchAssignmentsAsync(IPage page, MoodleData data)
    {
        var myUrl = $"{_moodleUrl}/my/";
        await page.GotoAsync(myUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForTimeoutAsync(2000);

        Console.WriteLine($"At page: {page.Url}");

        var links = await page.Locator("a[href*='quiz/view.php'], a[href*='assign/view.php']").AllAsync();
        Console.WriteLine($"Found {links.Count} quiz/assign links");

        var processedIds = new HashSet<string>();

        for (int i = 0; i < links.Count; i++)
        {
            try
            {
                var link = links[i];

                var href = await link.GetAttributeAsync("href");
                if (string.IsNullOrWhiteSpace(href)) continue;

                var idParam = HttpUtility.ParseQueryString(new Uri(href).Query)["id"];
                if (string.IsNullOrWhiteSpace(idParam)) continue;
                if (processedIds.Contains(idParam)) continue;
                processedIds.Add(idParam);

                    var title = await link.TextContentAsync() ?? "";
                title = CleanTitle(title);
                if (string.IsNullOrWhiteSpace(title)) continue;

                var courseName = await ExtractCourseNameFromDomAsync(link);

                data.Assignments.Add(new MoodleAssignment
                {
                    Id = idParam,
                    Title = title,
                    CourseName = courseName ?? "",
                    Status = "pending",
                    Url = href
                });

                var courseTag = string.IsNullOrWhiteSpace(courseName) ? "?" : courseName;
                Console.WriteLine($"  [{courseTag}] {title}  (id={idParam})");
            }
            catch { }
        }

        Console.WriteLine($"Total assignments from dashboard: {data.Assignments.Count}");
    }

    private async Task<string?> ExtractCourseNameFromDomAsync(ILocator link)
    {
        try
        {
            return await link.EvaluateAsync<string?>(@"(el) => {
                const isBadText = (text) => {
                    if (!text || text.length < 3) return true;
                    if (/^\d{1,2}:\d{2}/.test(text)) return true;
                    if (/\d{4}\s*年/.test(text) || /\d{1,2}\s*月\s*\d{1,2}\s*日/.test(text)) return true;
                    if (/さらに/.test(text) || /件$/.test(text)) return true;
                    if (/^\d+\s*件/.test(text)) return true;
                    return false;
                };

                const eventItem = el.closest('[data-region=""event-list-item""], .event-list-item, .media, .activity-item, li, .list-group-item');
                if (eventItem) {
                    const courseEl = eventItem.querySelector('.course-name, .coursename, .text-muted:not(.badge), .text-white, small:not(.badge)');
                    if (courseEl && courseEl.textContent) {
                        const text = courseEl.textContent.trim();
                        if (text && !isBadText(text)) return text;
                    }
                    const cells = eventItem.querySelectorAll('div, span');
                    for (const cell of cells) {
                        if (cell.querySelector('a')) continue;
                        const t = (cell.textContent || '').trim();
                        if (t && t.length > 3 && !isBadText(t) && !t.includes('Due') && !t.includes('期限') && !t.includes('得点') && !t.includes('提出')) {
                            return t;
                        }
                    }
                }

                const card = el.closest('.card, .dashboard-card, [data-course-id], .course-card');
                if (card) {
                    const selectors = [
                        '.coursename a', '.coursename',
                        '.card-header h3', '.card-header a',
                        '.course-title', '.course_title',
                        'h3.coursename', 'h2.coursename',
                        '.card-body > h3', '.card-body > h2'
                    ];
                    for (const sel of selectors) {
                        const heading = card.querySelector(sel);
                        if (heading && heading.textContent) {
                            const text = heading.textContent.trim();
                            if (text && text.length > 1 && !isBadText(text)) return text;
                        }
                    }
                }

                let prev = el.parentElement;
                for (let i = 0; i < 8 && prev; i++) {
                    const headings = prev.querySelectorAll('h2, h3, h4, h5, h6, strong, .sectionname, .section-title');
                    for (const h of headings) {
                        if (h.textContent) {
                            const text = h.textContent.trim();
                            if (text && text.length > 2 && !isBadText(text)) return text;
                        }
                    }
                    prev = prev.parentElement;
                }

                return null;
            }");
        }
        catch
        {
            return null;
        }
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
                const seen = new Set();
                containers.forEach(el => {
                    const link = el.querySelector('a[href*=""mod/quiz/view.php""], a[href*=""mod/assign/view.php""]');
                    if (!link) return;
                    const href = link.getAttribute('href') || '';
                    const idMatch = href.match(/id=(\d+)/);
                    if (!idMatch) return;
                    const id = idMatch[1];
                    if (seen.has(id)) return;
                    seen.add(id);
                    const allText = el.textContent.replace(/\s+/g, ' ').trim();
                    events.push([id, allText]);
                });
                return JSON.stringify(events);
            }");

            var rawEvents = JsonSerializer.Deserialize<List<List<string>>>(rawJson ?? "[]") ?? [];

            Console.WriteLine($"Found {rawEvents.Count} calendar events");

            foreach (var pair in rawEvents)
            {
                if (pair.Count < 2) continue;
                var id = pair[0];
                var allText = pair[1];

                var assignment = data.Assignments.FirstOrDefault(a => a.Id == id);
                if (assignment == null)
                {
                    Console.WriteLine($"  Calendar event id={id} (no matching dashboard assignment)");
                    continue;
                }

                Console.WriteLine($"  Calendar event id={id}: '{assignment.Title}'");

                var dueDate = ParseDateText(allText);
                if (dueDate.HasValue)
                {
                    assignment.DueDate = dueDate.Value;
                    Console.WriteLine($"  -> DueDate: {dueDate.Value:yyyy/MM/dd HH:mm}");
                }

                var courseName = ParseCourseNameFromCalendarText(allText);
                if (!string.IsNullOrWhiteSpace(courseName))
                {
                    assignment.CourseName = courseName;
                    Console.WriteLine($"  -> CourseName: {courseName}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error parsing calendar: {ex.Message}");
        }
    }

    private string? ParseCourseNameFromCalendarText(string text)
    {
        // Course name format: "2026-Q1-コース名-教員名" or "2026-前-English Foundation..."
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

        // Japanese: 2026年05月18日(月) 23:59 or 2026年05月18日, 00:00 or 2026年05月18日 08:00
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

        // Japanese: 2026年5月18日 (date only) -> set to 23:59
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

    private static string NormalizeForComparison(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        return s.Trim().ToLowerInvariant();
    }
}
