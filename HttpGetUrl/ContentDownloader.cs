using Microsoft.Extensions.FileProviders;
using System.Net;

namespace HttpGetUrl;

public abstract class ContentDownloader(Uri uri, Uri referrer, IFileProvider workingFolder, CancellationTokenSource cancellationTokenSource) : IDisposable
{
    protected Uri uri = uri;
    protected Uri referrer = referrer;
    public IFileProvider WorkingFolder { get; } = workingFolder;

    public CancellationTokenSource CancellationTokenSource { get; } = cancellationTokenSource ?? new();
    public string FinalFileName { get; internal set; } = string.Empty;
    public string[] FragmentFileNames { get; protected set; } = [];
    public long EstimatedContentLength { get; protected set; }

    public abstract Task Analysis();
    public abstract Task<long> Download();
    public abstract Task Merge();
    public abstract void Dispose();

    public static ContentDownloader Create(Uri uri, Uri referrer, IFileProvider workingFolder, CancellationTokenSource cancellationTokenSource = null)
    {
        switch (uri.Host)
        {
            case "x.com":
            case "t.co":
                return new TwitterDownloader(uri, referrer, workingFolder, cancellationTokenSource);
            case "www.youtube.com":
            case "m.youtube.com":
            case "youtube.com":
            case "youtu.be":
                return new YoutubeDownloader(uri, referrer, workingFolder, cancellationTokenSource);
            default:
                return new HttpDownloader(uri, referrer, workingFolder, cancellationTokenSource);
        }
    }

    public static PwOptions PwOptions { get; private set; }

    public static void InitService(PwOptions pwOptions)
    {
        PwOptions = pwOptions;
        PwService.InitService(pwOptions);
    }
}
