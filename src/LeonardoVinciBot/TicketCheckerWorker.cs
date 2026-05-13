using Microsoft.Extensions.Options;

namespace LeonardoVinciBot;

public class TicketCheckerWorker : BackgroundService
{
    private readonly TicketCheckerService _checker;
    private readonly TelegramNotifier _telegram;
    private readonly IOptions<TicketCheckerSettings> _settings;
    private readonly ILogger<TicketCheckerWorker> _logger;
    private readonly IHostApplicationLifetime _lifetime;
    private int _checkCount;

    public TicketCheckerWorker(
        TicketCheckerService checker,
        TelegramNotifier telegram,
        IOptions<TicketCheckerSettings> settings,
        ILogger<TicketCheckerWorker> logger,
        IHostApplicationLifetime lifetime)
    {
        _checker = checker;
        _telegram = telegram;
        _settings = settings;
        _logger = logger;
        _lifetime = lifetime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var s = _settings.Value;
        _logger.LogInformation("=== Ticket checker started ===");
        _logger.LogInformation("Target: {Month}/{Year} days:{Days} | URL: {Url}",
            s.TargetMonth, s.TargetYear,
            s.TargetDays.Count > 0 ? string.Join(",", s.TargetDays) : "all",
            s.TargetUrl);
        _logger.LogInformation("Mode: {Mode}", s.RunOnce ? "run-once" : $"every {s.CheckIntervalMinutes} min");

        while (!stoppingToken.IsCancellationRequested)
        {
            _checkCount++;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            _logger.LogInformation("--- Check #{Count} at {Time} UTC ---", _checkCount, DateTime.UtcNow.ToString("HH:mm:ss"));

            try
            {
                var (available, message) = await _checker.CheckAsync(stoppingToken);
                sw.Stop();
                _logger.LogInformation("Check completed in {Elapsed}ms. Result: {Message}", sw.ElapsedMilliseconds, message);

                // if (available)
                // {
                    _logger.LogInformation("Sending Telegram alert...");
                    await _telegram.SendMessageAsync(message, stoppingToken);
                    _logger.LogInformation("Telegram alert sent");
                // }
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(ex, "Check #{Count} failed after {Elapsed}ms", _checkCount, sw.ElapsedMilliseconds);
            }

            if (s.RunOnce)
            {
                _logger.LogInformation("RunOnce mode — shutting down");
                _lifetime.StopApplication();
                return;
            }

            var nextCheck = DateTime.UtcNow.AddMinutes(s.CheckIntervalMinutes);
            _logger.LogInformation("Next check at {NextCheck} UTC", nextCheck.ToString("HH:mm:ss"));
            await Task.Delay(TimeSpan.FromMinutes(s.CheckIntervalMinutes), stoppingToken);
        }
    }
}