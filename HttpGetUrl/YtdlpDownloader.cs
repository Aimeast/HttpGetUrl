using System.Text.RegularExpressions;
using YoutubeDLSharp;
using YoutubeDLSharp.Metadata;
using YoutubeDLSharp.Options;

namespace HttpGetUrl;

public class YtdlpDownloader(TaskFile task, CancellationTokenSource cancellationTokenSource, DownloaderFactory downloaderFactory, StorageService storageService, TaskService taskService, TaskStorageCache taskCache, IConfiguration configuration)
    : ContentDownloader(task, cancellationTokenSource, downloaderFactory, storageService, taskService, taskCache, configuration)
{
    private bool _isPlaylist;

    private async Task<RunResult<VideoData>> FetchVideoDataAsync(string url)
    {
        var ytdlp = new YoutubeDL { YoutubeDLPath = Path.Combine(".hg", Utils.YtDlpBinaryName) };
        var options = new OptionSet { Proxy = _proxy, Cookies = Path.Combine(".hg", "tokens.txt") };
        var result = await ytdlp.RunVideoDataFetch(url, ct: CancellationTokenSource.Token, overrideOptions: options);

        return result;
    }

    public override async Task Analysis()
    {
        var result = await FetchVideoDataAsync(CurrentTask.Url.ToString());
        if (result.Data == null || result.Data.FormatID == "0" // Unsupported url
            || result.Data.Direct) // Direct url
            throw new NotSupportedException($"Ytdlp not support the Url '{CurrentTask.Url}'.");

        _isPlaylist = result.Data.Entries != null;
        if (_isPlaylist)
            AnalysisPlayList(result.Data.Entries);
        else if (result.Success)
        {
            CurrentTask.FileName = result.Data.Title;
            _taskCache.SaveTaskStatusDeferred(CurrentTask);
        }
        else
        {
            throw new Exception(string.Join('\n', result.ErrorOutput));
        }
    }

    private void AnalysisPlayList(IEnumerable<VideoData> entries)
    {
        foreach (var videoData in entries)
        {
            if (videoData.Duration == null)
                continue;

            var subTask = AddSubTask(videoData.Title, new Uri(videoData.Url));
            var info = new TaskService.TaskInfo(subTask.TaskId, subTask.Seq, async () =>
            {
                _taskCache.SaveTaskStatusDeferred(subTask, TaskStatus.Downloading);
                var result = await FetchVideoDataAsync(videoData.Url.ToString());
                if (result.Success)
                {
                    await ExecDownloadAsync(subTask);
                }
                else
                {
                    subTask.ErrorMessage = string.Join('\n', result.ErrorOutput);
                    _taskCache.SaveTaskStatusDeferred(subTask, TaskStatus.Error);
                }
            }, CancellationTokenSource);
            _taskService.QueueTask(info);
        }
    }

    private TaskFile AddSubTask(string title, Uri url)
    {
        var subTask = _taskCache.GetNextTaskItemSequence(CurrentTask.TaskId);
        subTask.FileName = $"{title}";
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
            Proxy = _proxy,
            Cookies = Path.Combine(".hg", "tokens.txt"),
            Output = _storageService.GetFilePath(CurrentTask.TaskId, ".")
                + Path.DirectorySeparatorChar
                + Utility.TruncateStringInUtf8(CurrentTask.FileName, 145, 100) + ".%(ext)s",
        };
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
        {
            CurrentTask.FileName = Path.GetFileName(result.Data);
            CurrentTask.DownloadedLength = new FileInfo(result.Data).Length;
        }
        else
            throw new Exception(string.Join('\n', result.ErrorOutput));
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
