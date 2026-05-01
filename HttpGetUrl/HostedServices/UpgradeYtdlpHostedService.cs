using YoutubeDLSharp;

namespace HttpGetUrl.HostedServices;

public class UpgradeYtdlpHostedService(ILogger<UpgradeYtdlpHostedService> logger, ProxyService proxyService) :
    IntervalHostedService(logger, INTERVAL, async () =>
    {
        var arg = "-U";
        if (proxyService.TestUseProxy("github.com"))
            arg += " --proxy " + proxyService.Proxy;

        try
        {
            var version = await Utility.RunCmdFirstLine(Path.Combine(".hg", Utils.YtDlpBinaryName), arg);
            logger.LogInformation($"Now yt-dlp {version}");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to upgrade yt-dlp");
        }
    })
{
    private const int INTERVAL = 3600 * 24 * 1000;
}
