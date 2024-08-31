using FFMpegCore;
using Microsoft.Extensions.FileProviders;
using YoutubeDLSharp;
using YoutubeDLSharp.Options;

namespace HttpGetUrl;

[Downloader("Youtube", ["youtube.com", "youtu.be"])]
public class YoutubeDownloader(Uri uri, Uri referrer, IFileProvider workingFolder, CancellationTokenSource cancellationTokenSource)
    : ContentDownloader(uri, referrer, workingFolder, cancellationTokenSource)
{
    ContentDownloader[] backDownloaders = null;
    string videoContainerName;

    public override async Task Analysis()
    {
        var ytdl = new YoutubeDL { YoutubeDLPath = Path.Combine(".hg", Utils.YtDlpBinaryName) };
        var options = new OptionSet
        {
            Proxy = PwOptions.Proxy,
        };

        var result = await ytdl.RunVideoDataFetch(uri.ToString(), ct: CancellationTokenSource.Token, overrideOptions: options);
        result.EnsureSuccess();

        // youtube_formats: https://gist.github.com/AgentOak/34d47c65b1d28829bb17c24c04a0096f
        var formats = result.Data.Formats.Where(x => x.FileSize != null);
        var bestVideo = formats.Where(x => x.VideoCodec != "none")
            .OrderByDescending(x => x.Quality)
            .ThenBy(x => x.FileSize)
            .First();
        var bestAudio = formats.Where(f => f.AudioCodec != "none")
            .Select(x => { if (x.Extension == "m4a") x.Extension = "mp4"; return x; })
            .Where(x => x.Extension == bestVideo.Extension)
            .OrderByDescending(x => x.Quality)
            .ThenBy(x => x.FileSize)
            .First();

        videoContainerName = bestVideo.Extension;
        if (bestVideo == bestAudio)
        {
            backDownloaders = [ForkToHttpDownloader(new Uri(bestVideo.Url), null)];
        }
        else
        {
            var n1 = $"video.{videoContainerName}";
            var n2 = $"audio.{videoContainerName}";
            var d1 = ForkToHttpDownloader(new Uri(bestVideo.Url), null);
            var d2 = ForkToHttpDownloader(new Uri(bestAudio.Url), null);
            d1.FinalFileNames = [n1];
            d2.FinalFileNames = [n2];
            backDownloaders = [d1, d2];
            FragmentFileNames = [n1, n2];
        }
        FinalFileNames = [$"{Utility.MakeValidFileName(result.Data.Title)}.{videoContainerName}"];
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
