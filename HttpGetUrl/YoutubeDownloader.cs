using FFMpegCore;
using Microsoft.Extensions.FileProviders;
using YoutubeDLSharp;
using YoutubeDLSharp.Metadata;
using YoutubeDLSharp.Options;

namespace HttpGetUrl;

[Downloader("Youtube", ["youtube.com", "youtu.be"])]
public class YoutubeDownloader(Uri uri, Uri referrer, IFileProvider workingFolder, CancellationTokenSource cancellationTokenSource)
    : ContentDownloader(uri, referrer, workingFolder, cancellationTokenSource)
{
    ContentDownloader[] backDownloaders = null;
    string videoContainerName, videoId;
    bool isPlaylist = false;

    private async Task<RunResult<VideoData>> FetchVideo(string url)
    {
        var ytdl = new YoutubeDL { YoutubeDLPath = Path.Combine(".hg", Utils.YtDlpBinaryName) };
        var options = new OptionSet
        {
            Proxy = PwOptions.Proxy,
        };

        var result = await ytdl.RunVideoDataFetch(url, ct: CancellationTokenSource.Token, overrideOptions: options);
        result.EnsureSuccess();

        return result;
    }

    public override async Task Analysis()
    {
        var result = await FetchVideo(uri.ToString());

        isPlaylist = result.Data.Entries != null;
        if (isPlaylist)
            await AnalysisPlayList(result.Data.Entries);
        else
            await AnalysisVideo(result.Data);
    }

    private async Task AnalysisVideo(VideoData data)
    {
        // youtube_formats: https://gist.github.com/AgentOak/34d47c65b1d28829bb17c24c04a0096f
        var formats = data.Formats.Where(x => x.FileSize != null);
        // domain manifest.googlevideo.com means a DASH format that it's complicated
        var bestVideo = formats.Where(x => x.VideoCodec != "none" && !x.Url.Contains("manifest.googlevideo.com"))
            .OrderByDescending(x => x.Quality)
            .ThenBy(x => x.FileSize)
            .First();
        var bestAudio = formats.Where(x => x.AudioCodec != "none" && !x.Url.Contains("manifest.googlevideo.com"))
            .Select(x => { if (x.Extension == "m4a") x.Extension = "mp4"; return x; })
            .Where(x => x.Extension == bestVideo.Extension)
            .OrderByDescending(x => x.Quality)
            .ThenBy(x => x.FileSize)
            .First();

        videoId = data.ID;
        videoContainerName = bestVideo.Extension;
        if (bestVideo == bestAudio)
        {
            backDownloaders = [ForkToHttpDownloader(new Uri(bestVideo.Url), null)];
        }
        else
        {
            var n1 = $"{videoId}-video.{videoContainerName}";
            var n2 = $"{videoId}-audio.{videoContainerName}";
            var d1 = ForkToHttpDownloader(new Uri(bestVideo.Url), null);
            var d2 = ForkToHttpDownloader(new Uri(bestAudio.Url), null);
            d1.FinalFileNames = [n1];
            d2.FinalFileNames = [n2];
            backDownloaders = [d1, d2];
            FragmentFileNames = [n1, n2];
        }
        FinalFileNames = [$"{Utility.MakeValidFileName(data.Title)}.{videoContainerName}"];

        foreach (var downloader in backDownloaders)
        {
            await downloader.Analysis();
            EstimatedContentLength += downloader.EstimatedContentLength;
        }
    }

    private async Task AnalysisPlayList(IEnumerable<VideoData> entries)
    {
        var downloaders = new List<ContentDownloader>();
        var fragments = new List<string>();
        var finalFiles = new List<string>();
        foreach (var videoData in entries)
        {
            if (videoData.Duration == null)
                continue;
            var downloader = new YoutubeDownloader(new Uri(videoData.Url), uri, WorkingFolder, CancellationTokenSource);
            try
            {
                await downloader.Analysis();
                await Task.Delay(Random.Shared.Next(30_000, 60_000));
            }
            catch
            {
                continue;
            }
            downloaders.Add(downloader);
            fragments.AddRange(downloader.FragmentFileNames);
            finalFiles.Add(downloader.FinalFileNames[0]);
            EstimatedContentLength += downloader.EstimatedContentLength;
        }
        backDownloaders = downloaders.ToArray();
        FragmentFileNames = fragments.ToArray();
        FinalFileNames = finalFiles.ToArray();
    }

    public override async Task<long> Download()
    {
        var length = 0L;
        for (var i = 0; i < backDownloaders.Length; i++)
        {
            if (i % 3 == 2)
                await Task.Delay(Random.Shared.Next(30_000, 100_000));
            length += await backDownloaders[i].Download();
        }
        return length;
    }

    public override async Task<long> Merge()
    {
        if (isPlaylist)
        {
            var lengths = await Task.WhenAll(backDownloaders.Select(x => x.Merge()));
            return lengths.Sum();
        }

        if (backDownloaders.Length == 1)
            return -1;

        string videoFilePath = WorkingFolder.GetFileInfo($"{videoId}-video.{videoContainerName}").PhysicalPath;
        string audioFilePath = WorkingFolder.GetFileInfo($"{videoId}-audio.{videoContainerName}").PhysicalPath;
        string outputFilePath = WorkingFolder.GetFileInfo($"{videoId}-output.{videoContainerName}").PhysicalPath;

        var result = await FFMpegArguments
            .FromFileInput(videoFilePath)
            .AddFileInput(audioFilePath)
            .OutputToFile(outputFilePath, overwrite: true, options => options
                .WithVideoCodec("copy")
                .WithAudioCodec("copy"))
            .ProcessAsynchronously();

        File.Move(outputFilePath, WorkingFolder.GetFileInfo(FinalFileNames[0]).PhysicalPath);
        File.Delete(videoFilePath);
        File.Delete(audioFilePath);

        return WorkingFolder.GetFileInfo(FinalFileNames[0]).Length;
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
