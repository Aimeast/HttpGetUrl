using FFMpegCore;
using Microsoft.Extensions.FileProviders;
using System.Net;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;

namespace HttpGetUrl;

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

    public override async Task Analysis()
    {
        var handler = new HttpClientHandler();
        if (PwOptions.Proxy != null)
            handler.Proxy = new WebProxy(PwOptions.Proxy);
        var token = PwOptions.Tokens.First(x => x.Identity == "Youtube");
        handler.UseCookies = true;
        handler.CookieContainer.Add(new Cookie("GOOGLE_ABUSE_EXEMPTION", token.Value) { Domain = "youtube.com" });

        var httpClient = new HttpClient(handler);
        var youtube = new YoutubeClient(httpClient);
        var video = await youtube.Videos.GetAsync(uri.ToString(), CancellationTokenSource.Token);
        var streamManifest = await youtube.Videos.Streams.GetManifestAsync(video.Id);
        var videoInfo = streamManifest.GetVideoStreams()
            .OrderByDescending(x => x.VideoQuality)
            .ThenBy(x => codecRank.TryGetValue(x.VideoCodec.Split('.')[0], out var value) ? value : int.MaxValue)
            .ThenByDescending(x => x.Size.Bytes)
            .First();
        var audioInfo = videoInfo as IAudioStreamInfo ?? streamManifest.GetAudioStreams()
            .Where(x => x.Container == videoInfo.Container)
            .MaxBy(x => x.Size);

        if (videoInfo == audioInfo)
        {
            backDownloaders = [Create(new Uri(videoInfo.Url), null, workingFolder, CancellationTokenSource)];
        }
        else
        {
            var d1 = Create(new Uri(videoInfo.Url), null, workingFolder, CancellationTokenSource);
            var d2 = Create(new Uri(audioInfo.Url), null, workingFolder, CancellationTokenSource);
            var n1 = $"video.{videoInfo.Container.Name}";
            var n2 = $"audio.{videoInfo.Container.Name}";
            d1.FinalFileNames = [n1];
            d2.FinalFileNames = [n2];
            backDownloaders = [d1, d2];
            FragmentFileNames = [n1, n2];
        }
        FinalFileNames = [$"{MakeValidFileName(video.Title)}.{videoInfo.Container.Name}"];
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

    public override async Task Merge()
    {
        if (backDownloaders.Length == 1)
            return;

        string videoFilePath = WorkingFolder.GetFileInfo(backDownloaders[0].FinalFileNames[0]).PhysicalPath;
        string audioFilePath = WorkingFolder.GetFileInfo(backDownloaders[1].FinalFileNames[0]).PhysicalPath;
        string outputFilePath = WorkingFolder.GetFileInfo(FinalFileNames[0]).PhysicalPath;

        var result = await FFMpegArguments
            .FromFileInput(videoFilePath)
            .AddFileInput(audioFilePath)
            .OutputToFile(outputFilePath, overwrite: true, options => options
                .WithVideoCodec("copy")
                .WithAudioCodec("copy"))
            .ProcessAsynchronously();

        File.Delete(videoFilePath);
        File.Delete(audioFilePath);
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

    private static string MakeValidFileName(string fileName, char replacement = '_')
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var chars = fileName.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (invalidChars.Contains(chars[i]))
                chars[i] = replacement;
        }
        return new string(chars);
    }
}
