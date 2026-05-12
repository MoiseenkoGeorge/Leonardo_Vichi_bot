using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace LeonardoVinciBot;

public class TelegramNotifier
{
    private readonly HttpClient _httpClient;
    private readonly IOptions<TelegramSettings> _settings;
    private readonly ILogger<TelegramNotifier> _logger;

    public TelegramNotifier(
        HttpClient httpClient,
        IOptions<TelegramSettings> settings,
        ILogger<TelegramNotifier> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
    }

    public async Task SendMessageAsync(string message, CancellationToken ct)
    {
        var s = _settings.Value;

        if (string.IsNullOrWhiteSpace(s.BotToken) || string.IsNullOrWhiteSpace(s.ChatId))
        {
            _logger.LogWarning("Telegram BotToken or ChatId is not configured — skipping notification");
            return;
        }

        var url = $"https://api.telegram.org/bot{s.BotToken}/sendMessage";
        var body = JsonSerializer.Serialize(new
        {
            chat_id = s.ChatId,
            text = message
        });

        var response = await _httpClient.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json"), ct);

        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation("Telegram alert sent");
        }
        else
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Telegram API error {Status}: {Body}", (int)response.StatusCode, error);
        }
    }
}
