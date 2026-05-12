using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace LeonardoVinciBot;

public class TicketCheckerService : IAsyncDisposable
{
    private readonly IOptions<TicketCheckerSettings> _settings;
    private readonly ILogger<TicketCheckerService> _logger;
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private readonly SemaphoreSlim _lock = new(1, 1);

    // Captures: [1]=venueCode [2]=eventId [3]=year [4]=month [5]=day [6]=availableSeats(index5)
    private static readonly Regex EventiRegex = new(
        @"new Array\s*\('([^']+)','([^']+)',\s*new Date\s*\((\d+),\s*\((\d+)-1\),\s*(\d+)\),\s*'[^']*',\s*\d+,\s*'(\d+)'\)",
        RegexOptions.Compiled);

    // Matches available time slots: <li class="time"><a href="..." title="N seats">HH.MM</a>
    private static readonly Regex TimeSlotRegex = new(
        @"<li class=""time""><a href=""([^""]+)""\s+title=""(\d+) seat[^""]*"">(\d{2}\.\d{2})</a>",
        RegexOptions.Compiled);

    private static readonly Regex ShowIdRegex = new(
        @"var show_id\s*=\s*'(\d+)'",
        RegexOptions.Compiled);

    public TicketCheckerService(
        IOptions<TicketCheckerSettings> settings,
        ILogger<TicketCheckerService> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public async Task<(bool Available, string Message)> CheckAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            _playwright ??= await Playwright.CreateAsync();
            _browser ??= await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                Args = ["--no-sandbox", "--disable-setuid-sandbox", "--disable-dev-shm-usage",
                        "--disable-blink-features=AutomationControlled"]
            });

            await using var context = await _browser.NewContextAsync(new BrowserNewContextOptions
            {
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36",
                Locale = "en-US"
            });

            var page = await context.NewPageAsync();

            await page.AddInitScriptAsync(
                "Object.defineProperty(navigator, 'webdriver', { get: () => undefined })");

            _logger.LogInformation("Navigating to ticket page...");

            await page.GotoAsync(_settings.Value.TargetUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 45_000
            });

            await page.WaitForTimeoutAsync(3_000);

            var title = await page.TitleAsync();
            var html = await page.ContentAsync();
            _logger.LogInformation("Page title: {Title}", title);

            // Blocker checks
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            var pageText = doc.DocumentNode.InnerText;

            if (html.Contains("_Incapsula_Resource"))
                return (false, "Incapsula bot challenge — not yet bypassed");

            if (pageText.Contains("posto in coda", StringComparison.OrdinalIgnoreCase) ||
                pageText.Contains("queue position", StringComparison.OrdinalIgnoreCase))
                return (false, "Queue system active — tickets not yet accessible");

            var month = _settings.Value.TargetMonth;
            var year = _settings.Value.TargetYear;
            var monthName = new DateTime(year, month, 1).ToString("MMMM yyyy");

            // Parse eventi array — index 5 != '0' means seats are available
            var targetDays = _settings.Value.TargetDays;

            var availableDates = EventiRegex.Matches(html)
                .Where(em =>
                    int.Parse(em.Groups[3].Value) == year &&
                    int.Parse(em.Groups[4].Value) == month &&
                    em.Groups[6].Value != "0" &&
                    (targetDays.Count == 0 || targetDays.Contains(int.Parse(em.Groups[5].Value))))
                .Select(em => (
                    VenueCode: em.Groups[1].Value,
                    EventId: em.Groups[2].Value,
                    Day: int.Parse(em.Groups[5].Value),
                    Seats: int.Parse(em.Groups[6].Value)))
                .ToList();

            _logger.LogInformation("Available dates for {Month}: {Count}", monthName, availableDates.Count);

            if (availableDates.Count == 0)
                return (false, $"No available dates for {monthName}");

            var showIdMatch = ShowIdRegex.Match(html);
            var showId = showIdMatch.Success ? showIdMatch.Groups[1].Value : "151991";

            // For each available date, trigger showTimeCal and parse time slots
            var sb = new StringBuilder();
            sb.AppendLine($"Tickets available for {monthName}!");
            sb.AppendLine(_settings.Value.TargetUrl);

            foreach (var date in availableDates)
            {
                _logger.LogInformation("Fetching time slots for {Day}/{Month}/{Year}", date.Day, month, year);

                await page.EvaluateAsync(
                    $"showTimeCal(null, '{showId}', '{date.VenueCode}', '{date.EventId}', '{date.Day}')");

                // Wait for time calendar to become visible
                try
                {
                    await page.WaitForFunctionAsync(
                        $"document.querySelector('#timeCal_{showId}').style.display === 'block'",
                        null, new PageWaitForFunctionOptions { Timeout = 5_000 });
                }
                catch
                {
                    await page.WaitForTimeoutAsync(2_000);
                }

                var updatedHtml = await page.ContentAsync();
                var slots = TimeSlotRegex.Matches(updatedHtml)
                    .Select(s => (
                        Time: s.Groups[3].Value,
                        Seats: int.Parse(s.Groups[2].Value),
                        Url: "https://cenacolovinciano.vivaticket.it" + s.Groups[1].Value.Replace("&amp;", "&")))
                    .ToList();

                sb.AppendLine();
                sb.AppendLine($"{date.Day:D2}/{month:D2}/{year} ({date.Seats} slot(s) available):");

                if (slots.Count > 0)
                    foreach (var slot in slots)
                        sb.AppendLine($"  {slot.Time} — {slot.Seats} seat(s)\n  {slot.Url}");
                else
                    sb.AppendLine("  (time slots not loaded)");
            }

            return (true, sb.ToString());
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser != null) await _browser.DisposeAsync();
        _playwright?.Dispose();
        _lock.Dispose();
    }
}
