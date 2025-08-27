namespace HttpGetUrl.HostedServices;

public class CleanUserSpaceHostedService(ILogger<CleanUserSpaceHostedService> logger, StorageService storageService) :
    IntervalHostedService(logger, INTERVAL, () =>
    {
        var utcnow = DateTimeOffset.UtcNow;
        var allSpace = storageService.GetAllUserSpace();
        foreach (var userSpace in allSpace)
        {
            var space = storageService.GetUserSpace(userSpace);
            if (space.Expires < utcnow)
                storageService.DeleteUserSpace(userSpace);
        }
    })
{
    private const int INTERVAL = 3600 * 24 * 1000;
}
