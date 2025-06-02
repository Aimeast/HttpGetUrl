namespace HttpGetUrl;

public class CleanUserSpaceHostedService(StorageService storageService) : IHostedService
{
    const int INTERVAL = 3600 * 24;
    private bool _canceled = false;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = Task.Run(() =>
        {
            var times = INTERVAL;
            while (!cancellationToken.IsCancellationRequested && !_canceled)
            {
                if (times++ > INTERVAL)
                {
                    var utcnow = DateTimeOffset.UtcNow;
                    var allSpace = storageService.GetAllUserSpace();
                    foreach (var userSpace in allSpace)
                    {
                        var space = storageService.GetUserSpace(userSpace);
                        if (space.Expires < utcnow)
                            storageService.DeleteUserSpace(userSpace);
                    }
                }
                Thread.Sleep(1000);
            }
        });
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _canceled = true;
        return Task.CompletedTask;
    }
}
