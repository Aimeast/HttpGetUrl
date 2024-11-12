namespace HttpGetUrl;

public class PwServiceHostedService(PwService pwService) : IHostedService
{
    private readonly PwService _pwService = pwService;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _pwService.InitializeAsync();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _pwService.CloseAsync();
    }
}
