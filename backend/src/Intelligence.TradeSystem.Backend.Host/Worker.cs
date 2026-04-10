namespace Intelligence.TradeSystem.Backend.Host;

public partial class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;

    public Worker(ILogger<Worker> logger) => _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            LogWorkerRunning(_logger, DateTimeOffset.Now);

            await Task.Delay(1000, stoppingToken);
        }
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "Worker running at: {Time}")]
    private static partial void LogWorkerRunning(ILogger logger, DateTimeOffset time);
}
