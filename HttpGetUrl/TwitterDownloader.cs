using Microsoft.Extensions.FileProviders;
using Microsoft.Playwright;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HttpGetUrl;

[Downloader("Twitter", ["x.com", "t.co"])]
public class TwitterDownloader(Uri uri, Uri referrer, IFileProvider workingFolder, CancellationTokenSource cancellationTokenSource)
    : ContentDownloader(uri, referrer, workingFolder, cancellationTokenSource)
{
    ContentDownloader[] backDownloaders = null;

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

                var urlNodes = doc.RootElement.SearchJson("extended_entities").SearchJson("variants").Select(x => x.SearchJson("url").LastOrDefault()).ToArray();
                backDownloaders = new ContentDownloader[urlNodes.Length];
                for (var i = 0; i < urlNodes.Length; i++)
                {
                    var node = urlNodes[i];
                    if (node.ValueKind == JsonValueKind.String)
                    {
                        var videoUrl = node.ToString();
                        backDownloaders[i] = ForkToHttpDownloader(new Uri(videoUrl), null);
                    }
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

        FinalFileNames = new string[backDownloaders.Length];
        for (var i = 0; i < backDownloaders.Length; i++)
        {
            var downloader = backDownloaders[i];
            await downloader.Analysis();
            FinalFileNames[i] = downloader.FinalFileNames[0];
            EstimatedContentLength += downloader.EstimatedContentLength;
        }
    }

    public override async Task<long> Download()
    {
        var lengths = await Task.WhenAll(backDownloaders.Select(x => x.Download()));
        return lengths.Sum();
    }

    public override Task Merge()
    {
        throw new InvalidOperationException($"Merge not supported by {nameof(TwitterDownloader)}.");
    }

    public override void Dispose()
    {
        if (backDownloaders != null)
        {
            foreach (var downloader in backDownloaders)
            {
                downloader.Dispose();
            }
        }
        backDownloaders = null;
    }
}
