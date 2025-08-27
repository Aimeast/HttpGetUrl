namespace HttpGetUrl.HostedServices;

public abstract class IntervalHostedService(ILogger logger, int intervalMilliseconds, Action action) : IHostedService
{
    private CancellationTokenSource _cts = new();

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = Task.Run(async () =>
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    await Task.Run(action);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex.Message);
                }
                await Task.Delay(intervalMilliseconds, _cts.Token);
            }
        });
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _cts.Cancel();
        return Task.CompletedTask;
    }
}
