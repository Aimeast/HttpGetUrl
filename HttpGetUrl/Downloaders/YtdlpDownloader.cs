using HttpGetUrl.Models;
using System.Text.RegularExpressions;
using YoutubeDLSharp;
using YoutubeDLSharp.Metadata;
using YoutubeDLSharp.Options;

namespace HttpGetUrl.Downloaders;

[Downloader("Ytdlp", ["*"])]
public class YtdlpDownloader(TaskFile task, CancellationTokenSource cancellationTokenSource, DownloaderFactory downloaderFactory, StorageService storageService, TaskService taskService, TaskStorageCache taskCache, ProxyService proxyService, IConfiguration configuration)
    : ContentDownloader(task, cancellationTokenSource, downloaderFactory, storageService, taskService, taskCache, proxyService, configuration)
{
    private bool _isPlaylist;
    private readonly string _formatSelecter = "bestvideo+bestaudio/best";
    private readonly string _formatSort = "vcodec:h264:h265:av01:vp9:vp9.2";
    private readonly DownloadMergeFormat _mergeFormat = DownloadMergeFormat.Mp4;

    public bool UseCookie { get; set; }

    private async Task<RunResult<VideoData>> FetchVideoDataAsync(Uri url)
    {
        var ytdlp = new YoutubeDL { YoutubeDLPath = Path.Combine(".hg", Utils.YtDlpBinaryName) };
        var options = new OptionSet();
        if (UseCookie)
            options.Cookies = Path.Combine(".hg", "tokens.txt");
        if (_proxyService.TestUseProxy(url.Host))
            options.Proxy = _proxyService.Proxy;
        options.Format = _formatSelecter;
        options.FormatSort = _formatSort;
        options.MergeOutputFormat = _mergeFormat;

        var result = await ytdlp.RunVideoDataFetch(url.ToString(), ct: CancellationTokenSource.Token, overrideOptions: options);

        return result;
    }

    public override async Task Analysis()
    {
        var result = await FetchVideoDataAsync(CurrentTask.Url);
        if (!result.Success)
        {
            var errorMessage = string.Join('\n', result.ErrorOutput.Where(x => x.StartsWith("ERROR:")));
            if (errorMessage.Contains("--cookies") || errorMessage.Contains("--list-formats"))
                if (UseCookie)
                    throw new YtdlpException(errorMessage) { Restricted = true };
                else
                    throw new YtdlpException(errorMessage) { TryCookie = true };
        }

        if (result.Data == null || result.Data.FormatID == "0" // Unsupported url
            || result.Data.Direct) // Direct url
            throw new YtdlpException($"Ytdlp not support the Url '{CurrentTask.Url}'.") { NotSupported = true };

        _isPlaylist = result.Data.Entries != null;
        if (_isPlaylist)
        {
            CurrentTask.IsHide = true;
            CurrentTask.IsVirtual = true;
            CurrentTask.ContentText = result.Data.Title;
            if (!string.IsNullOrEmpty(result.Data.Description))
                CurrentTask.ContentText += $" -- {result.Data.Description}";
            _taskCache.SaveTaskStatusDeferred(CurrentTask);
            AnalysisPlayList(result.Data.Entries);
        }
        else if (result.Success)
        {
            CurrentTask.FileName = Utility.TruncateStringInUtf8(Utility.MakeValidFileName($"{result.Data.Title}.{result.Data.Extension}"), 145, 100);
            _taskCache.SaveTaskStatusDeferred(CurrentTask);
        }
        else
        {
            var errorMessage = string.Join('\n', result.ErrorOutput.Where(x => x.StartsWith("ERROR:")));
            throw new Exception(errorMessage);
        }
    }

    private void AnalysisPlayList(IEnumerable<VideoData> entries)
    {
        foreach (var videoData in entries)
        {
            if (videoData.Duration == null)
                continue;

            var subTask = AddSubTask(videoData.Title, videoData.Extension, new Uri(videoData.Url));
            var info = new TaskService.TaskInfo(subTask.UserSpace, subTask.TaskId, subTask.Seq, async () =>
            {
                _taskCache.SaveTaskStatusDeferred(subTask, TaskStatus.Downloading);
                var result = await FetchVideoDataAsync(new Uri(videoData.Url));
                if (result.Success)
                {
                    await ExecDownloadAsync(subTask);
                }
                else
                {
                    subTask.ErrorMessage = string.Join('\n', result.ErrorOutput.Where(x => x.StartsWith("ERROR:")));
                    _taskCache.SaveTaskStatusDeferred(subTask, TaskStatus.Error);
                }
            }, CancellationTokenSource);
            _taskService.QueueTask(info);
        }
    }

    private TaskFile AddSubTask(string title, string ext, Uri url)
    {
        var subTask = _taskCache.GetNextTaskItemSequence(CurrentTask.UserSpace, CurrentTask.TaskId);
        subTask.FileName = Utility.TruncateStringInUtf8(Utility.MakeValidFileName($"{title}.{ext}"), 145, 100);
        subTask.Url = url;
        _taskCache.SaveTaskStatusDeferred(subTask);
        return subTask;
    }

    private async Task ExecDownloadAsync(TaskFile subTask)
    {
        var downloader = _downloaderFactory.CreateDownloader(subTask, CancellationTokenSource);
        await downloader.ExecuteDownloadProcessAsync();
    }

    public override async Task Download()
    {
        if (_isPlaylist)
            return;

        var ytdlp = new YoutubeDL
        {
            YoutubeDLPath = Path.Combine(".hg", Utils.YtDlpBinaryName),
        };
        var options = new OptionSet
        {
            Progress = true,
            Format = _formatSelecter,
            FormatSort = _formatSort,
            MergeOutputFormat = _mergeFormat,
            Output = _storageService.GetFilePath(CurrentTask.UserSpace, CurrentTask.TaskId, ".")
                + Path.DirectorySeparatorChar
                + CurrentTask.FileName,
        };
        if (UseCookie)
            options.Cookies = Path.Combine(".hg", "tokens.txt");
        if (_proxyService.TestUseProxy(CurrentTask.Url.Host))
            options.Proxy = _proxyService.Proxy;
        var progress = new Progress<DownloadProgress>(x =>
        {
            var match = Regex.Match(x.TotalDownloadSize ?? "", @"([\d.]+)\s*(K|M|G|T)iB", RegexOptions.IgnoreCase);
            if (!match.Success)
                return;

            var sizeValue = double.Parse(match.Groups[1].Value);
            var unit = match.Groups[2].Value.ToUpper();
            var multiplier = unit switch
            {
                "K" => 1024L,
                "M" => 1024L * 1024L,
                "G" => 1024L * 1024L * 1024L,
                "T" => 1024L * 1024L * 1024L * 1024L,
                _ => throw new ArgumentException($"Unsupported unit: {unit}")
            };

            CurrentTask.EstimatedLength = (long)(sizeValue * multiplier);
            CurrentTask.DownloadedLength = (long)(CurrentTask.EstimatedLength * x.Progress);
            _taskCache.SaveTaskStatusDeferred(CurrentTask);
        });
        var result = await ytdlp.RunVideoDownload(CurrentTask.Url.ToString(),
            ct: CancellationTokenSource.Token,
            progress: progress,
            overrideOptions: options);
        if (result.Success)
            CurrentTask.DownloadedLength = new FileInfo(options.Output).Length;
        else
            throw new Exception(string.Join('\n', result.ErrorOutput.Where(x => x.StartsWith("ERROR:"))));
    }

    public override async Task Resume()
    {
        var tasks = _taskCache.GetTaskItems(CurrentTask.UserSpace, CurrentTask.TaskId);
        foreach (var task in tasks)
            if (task.Status != TaskStatus.Completed)
            {
                task.ErrorMessage = null;
                _taskCache.SaveTaskStatusDeferred(task, TaskStatus.Pending);
                var downloader = this;
                if (task.Seq != 0)
                {
                    downloader = (YtdlpDownloader)_downloaderFactory.CreateDownloader(task, CancellationTokenSource);
                    task.IsHide = false;
                    task.IsVirtual = false;
                    task.EstimatedLength = -1;
                }
                _taskService.QueueTask(new TaskService.TaskInfo(task.UserSpace, task.TaskId,
                    task.Seq, downloader.ExecuteDownloadProcessAsync, CancellationTokenSource));
            }
        await Task.CompletedTask;
    }

    private class VideoMeta
    {
        public string VideoId { get; set; }
        public string Title { get; set; }
        public string ContainerName { get; set; }
        public string VideoUrl { get; set; }
        public string AudioUrl { get; set; }
    }

}

public class YtdlpException(string message) : Exception(message)
{
    public bool NotSupported { get; set; }
    public bool TryCookie { get; set; }
    public bool Restricted { get; set; }
}
