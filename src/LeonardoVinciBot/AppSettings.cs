namespace LeonardoVinciBot;

public class TelegramSettings
{
    public string BotToken { get; init; } = string.Empty;
    public string ChatId { get; init; } = string.Empty;
}

public class TicketCheckerSettings
{
    public string TargetUrl { get; init; } = string.Empty;
    public int CheckIntervalMinutes { get; init; } = 5;
    public int TargetMonth { get; init; } = 5;
    public int TargetYear { get; init; } = 2026;
    // If empty, checks all days in the target month
    public List<int> TargetDays { get; init; } = [];
    public bool RunOnce { get; init; } = false;
}
