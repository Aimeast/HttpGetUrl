using Microsoft.Extensions.FileProviders;
using Microsoft.Playwright;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HttpGetUrl;

public class TwitterDownloader(Uri uri, Uri referrer, IFileProvider workingFolder, CancellationTokenSource cancellationTokenSource)
    : ContentDownloader(uri, referrer, workingFolder, cancellationTokenSource)
{
    ContentDownloader backDownloader = null;

    public override async Task Analysis()
    {
        var tcs = new TaskCompletionSource();
        var page = await PwService.GetInstance().NewPageAsync();
        page.Response += async (_, response) =>
        {
            if (CancellationTokenSource.IsCancellationRequested)
            {
                tcs.SetCanceled();
                await page.CloseAsync();
                CancellationTokenSource.Token.ThrowIfCancellationRequested();
            }

            if (Regex.IsMatch(response.Url, @"/graphql/.+/(TweetResultByRestId|TweetDetail)"))
            {
                var responseBody = await response.TextAsync();
                var doc = JsonDocument.Parse(responseBody);

                var urlNode = doc.RootElement.SearchJson("extended_entities").SearchJson("variants").SearchJson("url").LastOrDefault();
                if (urlNode.ValueKind == JsonValueKind.String)
                {
                    var videoUrl = urlNode.ToString();
                    backDownloader = Create(new Uri(videoUrl), null, workingFolder, CancellationTokenSource);
                }
                tcs.SetResult();
            }
        };
        await page.GotoAsync(uri.ToString(), new PageGotoOptions
        {
            Timeout = 60_000 * 5, // 5 minuts
            WaitUntil = WaitUntilState.DOMContentLoaded, // wait until the DOMContentLoaded event is fired, not all resources
        });
        await tcs.Task;
        await page.CloseAsync();

        if (backDownloader != null)
        {
            await backDownloader.Analysis();
            FinalFileName = backDownloader.FinalFileName;
            EstimatedContentLength = backDownloader.EstimatedContentLength;
        }
    }

    public override Task<long> Download()
    {
        return backDownloader.Download();
    }

    public override Task Merge()
    {
        throw new InvalidOperationException($"Merge not supported by {nameof(TwitterDownloader)}.");
    }

    public override void Dispose()
    {
        backDownloader?.Dispose();
        backDownloader = null;
    }

    public static async Task SetToken(string token)
    {
        await PwService.GetInstance().AddCookieAsync(new Cookie
        {
            Name = "auth_token",
            Value = token,
            Domain = ".x.com",
            Path = "/",
            Expires = DateTimeOffset.UtcNow.AddYears(1).ToUnixTimeSeconds(),
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteAttribute.None,
        });
    }
}
