using Microsoft.Playwright;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HttpGetUrl;

[Downloader("Twitter", ["x.com", "t.co"])]
public class TwitterDownloader(TaskFile task, CancellationTokenSource cancellationTokenSource, DownloaderFactory downloaderFactory, StorageService storageService, TaskService taskService, TaskStorageCache taskCache, ProxyService proxyService, PwService pwService)
    : ContentDownloader(task, cancellationTokenSource, downloaderFactory, storageService, taskService, taskCache, proxyService)
{
    private readonly PwService _pwService = pwService;

    public override async Task Analysis()
    {
        var urls = new List<string>();
        var tcs = new TaskCompletionSource();
        var page = await _pwService.NewPageAsync();
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
                var selectedJson = default(JsonElement);
                var entriesJson = doc.RootElement.SearchJson("entries").First();
                foreach (var entry in entriesJson.EnumerateArray())
                {
                    var entryType = entry.SearchJson("entryType").First().GetString();
                    var entryId = entry.SearchJson("entryId").First().GetString();
                    if (entryType == "TimelineTimelineItem" && entryId.StartsWith("tweet-"))
                    {
                        selectedJson = entry;
                    }
                }
                var full_text = selectedJson.SearchJson("full_text").First().ToString();
                var urlNodes = selectedJson.SearchJson("entities").SearchJson("video_info").Select(x => x.SearchJson("url").LastOrDefault()).ToArray();

                CurrentTask.ContentText = full_text;
                for (var i = 0; i < urlNodes.Length; i++)
                {
                    var node = urlNodes[i];
                    if (node.ValueKind == JsonValueKind.String)
                    {
                        var videoUrl = node.ToString();
                        urls.Add(videoUrl);
                    }
                }
                tcs.SetResult();
            }
        };
        await page.GotoAsync(CurrentTask.Url.ToString(), new PageGotoOptions
        {
            Timeout = 60_000 * 2, // 5 minuts
            WaitUntil = WaitUntilState.DOMContentLoaded, // wait until the DOMContentLoaded event is fired, not all resources
        });
        await tcs.Task;
        await page.CloseAsync();

        foreach (var url in urls)
        {
            var downloader = ForkToHttpDownloader(new Uri(url));
            _taskService.QueueTask(new TaskService.TaskInfo(downloader.CurrentTask.TaskId, downloader.CurrentTask.Seq, downloader.ExecuteDownloadProcessAsync, downloader.CancellationTokenSource));
        }
    }

    public override async Task Download()
    {
        await Task.CompletedTask;
    }
}
