using YoutubeDLSharp;

namespace HttpGetUrl.HostedServices;

public class UpgradeYtdlpHostedService(ILogger<UpgradeYtdlpHostedService> logger, ProxyService proxyService) :
    IntervalHostedService(logger, INTERVAL, async () =>
    {
        var useProxy = proxyService.TestUseProxy("github.com");

        var arg = "-U";
        if (useProxy)
            arg += " --proxy " + proxyService.Proxy;
        try
        {
            var version = await Utility.RunCmd(Path.Combine(".hg", Utils.YtDlpBinaryName), arg);
            logger.LogInformation($"Now yt-dlp {version}");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to upgrade yt-dlp");
        }

        try
        {
            var line = await Utility.RunCmd(Path.Combine(".hg", OperatingSystem.IsWindows() ? "deno.exe" : "deno"), "upgrade", firstOrLastLine: false, proxyUrl: useProxy ? proxyService.Proxy : null);
            logger.LogInformation(line);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to upgrade deno");
        }
    })
{
    private const int INTERVAL = 3600 * 24 * 1000;
}
