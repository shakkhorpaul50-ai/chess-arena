namespace WebApplication1.Services;

public sealed class ClockService : BackgroundService
{
    private readonly GameSessionManager _manager;
    private readonly ILogger<ClockService> _logger;

    public ClockService(GameSessionManager manager, ILogger<ClockService> logger)
    {
        _manager = manager;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _manager.CheckClocksAsync();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Clock tick failed");
            }

            try
            {
                await Task.Delay(1000, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}