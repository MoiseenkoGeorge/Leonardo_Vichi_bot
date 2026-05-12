using LeonardoVinciBot;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<TelegramSettings>(builder.Configuration.GetSection("Telegram"));
builder.Services.Configure<TicketCheckerSettings>(builder.Configuration.GetSection("TicketChecker"));

builder.Services.AddSingleton<TicketCheckerService>();
builder.Services.AddHttpClient<TelegramNotifier>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddHostedService<TicketCheckerWorker>();

await builder.Build().RunAsync();
