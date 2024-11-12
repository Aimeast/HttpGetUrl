using FFMpegCore;
using YoutubeDLSharp;
using YoutubeDLSharp.Metadata;
using YoutubeDLSharp.Options;

namespace HttpGetUrl;

[Downloader("Youtube", ["youtube.com", "youtu.be"])]
public class YoutubeDownloader(TaskFile task, CancellationTokenSource cancellationTokenSource, DownloaderFactory downloaderFactory, StorageService storageService, TaskService taskService, TaskStorageCache taskCache, IConfiguration configuration)
    : ContentDownloader(task, cancellationTokenSource, downloaderFactory, storageService, taskService, taskCache, configuration)
{
    private static readonly Dictionary<string, int> _codecRank = new()
    {
        {"av01", 3},
        {"vp09", 2},
        {"vp9", 2},
        {"avc1", 1},
    };

    private async Task<RunResult<VideoData>> FetchVideoAsync(string url)
    {
        var ytdl = new YoutubeDL { YoutubeDLPath = Path.Combine(".hg", Utils.YtDlpBinaryName) };
        var options = new OptionSet { Proxy = _proxy, Cookies = Path.Combine(".hg", "tokens.txt") };

        var result = await ytdl.RunVideoDataFetch(url, ct: CancellationTokenSource.Token, overrideOptions: options);

        return result;
    }

    public override async Task Analysis()
    {
        var result = await FetchVideoAsync(CurrentTask.Url.ToString());
        var isPlaylist = result.Data?.Entries != null;
        if (isPlaylist)
            AnalysisPlayList(result.Data.Entries);
        else
        {
            var virtualTask = AddVirtualTask(result.Data?.Title);
            if (result.Success)
            {
                var meta = AnalysisVideo(result.Data);
                var info = new TaskService.TaskInfo(virtualTask.TaskId, virtualTask.Seq, async () => await ExecDownloadAsync(meta, virtualTask), CancellationTokenSource);
                _taskService.QueueTask(info);
            }
            else
            {
                virtualTask.ErrorMessage = string.Join('\n', result.ErrorOutput);
                _taskCache.SaveTaskStatusDeferred(virtualTask, TaskStatus.Error);
            }
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
                var result = await FetchVideoAsync(videoData.Url.ToString());
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
                await MergeAsync(virtualTask.TaskId, meta.VideoId, meta.Title, meta.ContainerName);
                var partially = d1.CurrentTask.Status == TaskStatus.PartiallyCompleted || d2.CurrentTask.Status == TaskStatus.PartiallyCompleted;
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

    private async Task MergeAsync(string taskId, string videoId, string title, string containerName)
    {
        var videoFilePath = _storageService.GetFilePath(taskId, $"{videoId}-video.{containerName}");
        var audioFilePath = _storageService.GetFilePath(taskId, $"{videoId}-audio.{containerName}");
        var outputFilePath = _storageService.GetFilePath(taskId, $"{videoId}-output.{containerName}");
        var distFilePath = _storageService.GetFilePath(taskId, $"{title}.{containerName}");

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
