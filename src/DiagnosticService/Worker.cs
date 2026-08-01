namespace Diagnostic.Service;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly TimeProvider _timeProvider;

    public Worker(ILogger<Worker> logger, TimeProvider timeProvider)
    {
        _logger = logger;
        _timeProvider = timeProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Worker running at: {Time}", _timeProvider.GetLocalNow());
            await Task.Delay(1000, stoppingToken);
        }
    }
}
