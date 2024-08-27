using FFMpegCore;
using Microsoft.Extensions.FileProviders;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;

namespace HttpGetUrl;

[Downloader("Youtube", ["youtube.com", "youtu.be"])]
public class YoutubeDownloader(Uri uri, Uri referrer, IFileProvider workingFolder, CancellationTokenSource cancellationTokenSource)
    : ContentDownloader(uri, referrer, workingFolder, cancellationTokenSource)
{
    static readonly Dictionary<string, double> codecRank = new()
    {
        {"av01", 10},
        {"vp9", 11},
        {"avc1", 12},
    };

    ContentDownloader[] backDownloaders = null;
    string videoContainerName;

    public override async Task Analysis()
    {
        var httpClient = CreateHttpClient();
        var youtube = new YoutubeClient(httpClient);
        var video = await youtube.Videos.GetAsync(uri.ToString(), CancellationTokenSource.Token);
        var streamManifest = await youtube.Videos.Streams.GetManifestAsync(video.Id, CancellationTokenSource.Token);
        var videoInfo = streamManifest.GetVideoStreams()
            .OrderByDescending(x => x.VideoQuality)
            .ThenBy(x => codecRank.TryGetValue(x.VideoCodec.Split('.')[0], out var value) ? value : int.MaxValue)
            .ThenByDescending(x => x.Size.Bytes)
            .First();
        var audioInfo = videoInfo as IAudioStreamInfo ?? streamManifest.GetAudioStreams()
            .Where(x => x.Container == videoInfo.Container)
            .MaxBy(x => x.Size);

        videoContainerName = videoInfo.Container.Name;
        if (videoInfo == audioInfo)
        {
            backDownloaders = [ForkToHttpDownloader(new Uri(videoInfo.Url), null)];
        }
        else
        {
            var n1 = $"video.{videoContainerName}";
            var n2 = $"audio.{videoContainerName}";
            var d1 = ForkToHttpDownloader(new Uri(videoInfo.Url), null);
            var d2 = ForkToHttpDownloader(new Uri(audioInfo.Url), null);
            d1.FinalFileNames = [n1];
            d2.FinalFileNames = [n2];
            backDownloaders = [d1, d2];
            FragmentFileNames = [n1, n2];
        }
        FinalFileNames = [$"{Utility.MakeValidFileName(video.Title)}.{videoContainerName}"];
        foreach (var downloader in backDownloaders)
        {
            await downloader.Analysis();
            EstimatedContentLength += downloader.EstimatedContentLength;
        }
    }

    public override async Task<long> Download()
    {
        var lengths = await Task.WhenAll(backDownloaders.Select(x => x.Download()));
        return lengths.Sum();
    }

    public override async Task<long> Merge()
    {
        if (backDownloaders.Length == 1)
            return -1;

        string videoFilePath = WorkingFolder.GetFileInfo(backDownloaders[0].FinalFileNames[0]).PhysicalPath;
        string audioFilePath = WorkingFolder.GetFileInfo(backDownloaders[1].FinalFileNames[0]).PhysicalPath;
        string outputFilePath = $"output.{videoContainerName}";

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
