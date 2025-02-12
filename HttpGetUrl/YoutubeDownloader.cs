using FFMpegCore;
using YoutubeDLSharp;
using YoutubeDLSharp.Metadata;
using YoutubeDLSharp.Options;

namespace HttpGetUrl;

[Downloader("Youtube", ["youtube.com", "youtu.be"])]
public class YoutubeDownloader(TaskFile task, CancellationTokenSource cancellationTokenSource, DownloaderFactory downloaderFactory, StorageService storageService, TaskService taskService, TaskStorageCache taskCache, ProxyService proxyService)
    : ContentDownloader(task, cancellationTokenSource, downloaderFactory, storageService, taskService, taskCache, proxyService)
{
    private static readonly Dictionary<string, int> _codecRank = new()
    {
        // https://developer.mozilla.org/en-US/docs/Web/Media/Formats/codecs_parameter
        // https://en.wikipedia.org/wiki/Container_format
        {"avc1", 1}, // Advanced Video Coding / H.264, MPEG, Contained by .MP4 at youtube
        {"av01", 2}, // AOMedia Video 1, Alliance for Open Media, Contained by .MP4 at youtube
        {"vp09", 3}, // Video Processing 9, Google, Contained by .WebM at youtube
        {"vp9", 3},  // same as vp09
    };

    private async Task<RunResult<VideoData>> FetchVideoAsync(Uri url)
    {
        var ytdl = new YoutubeDL { YoutubeDLPath = Path.Combine(".hg", Utils.YtDlpBinaryName) };
        var options = new OptionSet { Cookies = Path.Combine(".hg", "tokens.txt") };
        if (_proxyService.TestUseProxy(url.Host))
            options.Proxy = _proxyService.Proxy;

        var result = await ytdl.RunVideoDataFetch(url.ToString(), ct: CancellationTokenSource.Token, overrideOptions: options);

        return result;
    }

    public override async Task Analysis()
    {
        var result = await FetchVideoAsync(CurrentTask.Url);
        var isPlaylist = result.Data?.Entries != null;
        if (isPlaylist)
            AnalysisPlayList(result.Data.Entries);
        else if (result.Success)
        {
            var virtualTask = AddVirtualTask(result.Data.Title);
            var meta = AnalysisVideo(result.Data);
            var info = new TaskService.TaskInfo(virtualTask.TaskId, virtualTask.Seq,
                async () => await ExecDownloadAsync(meta, virtualTask), CancellationTokenSource);
            _taskService.QueueTask(info);
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

            var virtualTask = AddVirtualTask(videoData.Title);
            var info = new TaskService.TaskInfo(virtualTask.TaskId, virtualTask.Seq, async () =>
            {
                _taskCache.SaveTaskStatusDeferred(virtualTask, TaskStatus.Downloading);
                var result = await FetchVideoAsync(new Uri(videoData.Url));
                if (result.Success)
                {
                    var meta = AnalysisVideo(result.Data);
                    await ExecDownloadAsync(meta, virtualTask);
                }
                else
                {
                    virtualTask.ErrorMessage = string.Join('\n', result.ErrorOutput);
                    _taskCache.SaveTaskStatusDeferred(virtualTask, TaskStatus.Error);
                }
            }, CancellationTokenSource);
            _taskService.QueueTask(info);
        }
    }

    private TaskFile AddVirtualTask(string title)
    {
        // virtual task for show file line on web UI
        var virtualTask = _taskCache.GetNextTaskItemSequence(CurrentTask.TaskId);
        virtualTask.FileName = $"{title}";
        virtualTask.IsVirtual = true;
        _taskCache.SaveTaskStatusDeferred(virtualTask);
        return virtualTask;
    }

    private VideoMeta AnalysisVideo(VideoData data)
    {
        // youtube_formats: https://gist.github.com/AgentOak/34d47c65b1d28829bb17c24c04a0096f
        var formats = data.Formats.Where(x => x.FileSize != null);
        // domain manifest.googlevideo.com means a DASH format that it's complicated
        var bestVideo = formats.Where(x => x.VideoCodec != "none" && !x.Url.Contains("manifest.googlevideo.com"))
            .OrderByDescending(x => x.Quality)
            .ThenBy(x => _codecRank[x.VideoCodec.Split('.')[0]])
            .First();
        var bestAudio = formats.Where(x => x.AudioCodec != "none" && !x.Url.Contains("manifest.googlevideo.com"))
            .Select(x => { if (x.Extension == "m4a") x.Extension = "mp4"; return x; })
            .Where(x => x.Extension == bestVideo.Extension)
            .OrderByDescending(x => x.Quality)
            .ThenBy(x => x.FileSize)
            .First();

        var meta = new VideoMeta()
        {
            VideoId = data.ID,
            Title = Utility.MakeValidFileName(data.Title),
            ContainerName = bestVideo.Extension,
            VideoUrl = bestVideo.Url,
            AudioUrl = bestVideo != bestAudio ? bestAudio.Url : null,
        };
        return meta;
    }

    private async Task ExecDownloadAsync(VideoMeta meta, TaskFile virtualTask)
    {
        virtualTask.FileName = $"{meta.Title}.{meta.ContainerName}";
        _taskCache.SaveTaskStatusDeferred(virtualTask, TaskStatus.Downloading);

        var d1 = ForkToHttpDownloader(new Uri(meta.VideoUrl));
        d1.CurrentTask.ParentSeq = virtualTask.Seq;
        d1.CurrentTask.IsHide = true;
        d1.CurrentTask.FileName = $"{meta.VideoId}-video.{meta.ContainerName}";
        var d2 = ForkToHttpDownloader(new Uri(meta.AudioUrl));
        d2.CurrentTask.ParentSeq = virtualTask.Seq;
        d2.CurrentTask.IsHide = true;
        d2.CurrentTask.FileName = $"{meta.VideoId}-audio.{meta.ContainerName}";

        var task1 = d1.ExecuteDownloadProcessAsync();
        var task2 = d2.ExecuteDownloadProcessAsync();
        await Task.WhenAll(task1, task2);
        if (d1.CurrentTask.Status != TaskStatus.Error && d2.CurrentTask.Status != TaskStatus.Error)
        {
            _taskCache.SaveTaskStatusDeferred(virtualTask, TaskStatus.Merging);
            try
            {
                var distFilePath = await MergeAsync(virtualTask.TaskId, meta.VideoId, meta.Title, meta.ContainerName);
                var partially = d1.CurrentTask.Status == TaskStatus.PartiallyCompleted || d2.CurrentTask.Status == TaskStatus.PartiallyCompleted;
                virtualTask.DownloadedLength = new FileInfo(distFilePath).Length;
                _taskCache.SaveTaskStatusDeferred(virtualTask, partially ? TaskStatus.PartiallyCompleted : TaskStatus.Completed);
            }
            catch (Exception ex)
            {
                virtualTask.ErrorMessage = $"{ex.GetType().Name}: {Utility.TruncateString(ex.Message, 100, 200)}.";
                _taskCache.SaveTaskStatusDeferred(virtualTask, TaskStatus.Error);
            }
        }
        else
        {
            _taskCache.SaveTaskStatusDeferred(virtualTask, TaskStatus.Error);
        }
    }

    private async ValueTask<string> MergeAsync(string taskId, string videoId, string title, string containerName)
    {
        var videoFilePath = _storageService.GetFilePath(taskId, $"{videoId}-video.{containerName}");
        var audioFilePath = _storageService.GetFilePath(taskId, $"{videoId}-audio.{containerName}");
        var outputFilePath = _storageService.GetFilePath(taskId, $"{videoId}-output.{containerName}");
        var distFilePath = _storageService.GetFilePath(taskId, $"{Utility.TruncateStringInUtf8(title, 145, 100)}.{containerName}");

        var result = await FFMpegArguments
            .FromFileInput(videoFilePath)
            .AddFileInput(audioFilePath)
            .OutputToFile(outputFilePath, overwrite: true, options => options
                .WithVideoCodec("copy")
                .WithAudioCodec("copy"))
            .ProcessAsynchronously();

        File.Delete(videoFilePath);
        File.Delete(audioFilePath);
        File.Move(outputFilePath, distFilePath);
        return distFilePath;
    }

    public override async Task Download()
    {
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
