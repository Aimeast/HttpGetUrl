using System.Text.Json;
using System.Text.RegularExpressions;

namespace HttpGetUrl.Downloaders;

[Downloader("Twitter", ["x.com", "t.co"])]
public class TwitterDownloader(TaskFile task, CancellationTokenSource cancellationTokenSource, DownloaderFactory downloaderFactory, StorageService storageService, TaskService taskService, TaskStorageCache taskCache, ProxyService proxyService, PwService pwService, IConfiguration configuration)
    : PwDownloader(task, cancellationTokenSource, downloaderFactory, storageService, taskService, taskCache, proxyService, pwService, configuration)
{
    protected override string UrlRegex => @"/graphql/.+/(?<act>TweetResultByRestId|TweetDetail)";

    protected override List<(string Url, string FileName)> GetUrls(Match match, string content)
    {
        // TweetResultByRestId: not login in
        // TweetDetail: logined
        var logined = match.Groups["act"].Value == "TweetDetail";
        var doc = JsonDocument.Parse(content);
        var selectedJson = default(JsonElement);
        if (logined)
        {
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
        }
        else
        {
            selectedJson = doc.RootElement.SearchJson("result").First();
        }

        var full_text = selectedJson.SearchJson("full_text").First().ToString();
        var urlNodes = selectedJson.SearchJson("entities").SearchJson("video_info").Select(x => x.SearchJson("url").LastOrDefault()).ToArray();

        CurrentTask.ContentText = full_text;

        var urls = new List<(string Url, string FileName)>();
        for (var i = 0; i < urlNodes.Length; i++)
        {
            var node = urlNodes[i];
            if (node.ValueKind == JsonValueKind.String)
            {
                var videoUrl = node.ToString();
                urls.Add((videoUrl, Path.GetFileName(new Uri(videoUrl).LocalPath)));
            }
        }
        return urls;
    }
}
