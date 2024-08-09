using FFMpegCore;
using Microsoft.Extensions.FileProviders;
using System.Collections.ObjectModel;
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
            d1.FinalFileName = $"video.{videoInfo.Container.Name}";
            d2.FinalFileName = $"audio.{videoInfo.Container.Name}";
            backDownloaders = [d1, d2];
            FragmentFileNames = [d1.FinalFileName, d2.FinalFileName];
        }
        FinalFileName = $"{MakeValidFileName(video.Title)}.{videoInfo.Container.Name}";
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

        string videoFilePath = WorkingFolder.GetFileInfo(backDownloaders[0].FinalFileName).PhysicalPath;
        string audioFilePath = WorkingFolder.GetFileInfo(backDownloaders[1].FinalFileName).PhysicalPath;
        string outputFilePath = WorkingFolder.GetFileInfo(FinalFileName).PhysicalPath;

        var result = await FFMpegArguments
            .FromFileInput(videoFilePath)
            .AddFileInput(audioFilePath)
            .OutputToFile(outputFilePath, overwrite: true, options => options
                .WithVideoCodec("copy")
                .WithAudioCodec("copy"))
            .ProcessAsynchronously();
    }

    public override void Dispose()
    {
        throw new NotImplementedException();
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
