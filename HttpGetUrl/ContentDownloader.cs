using Microsoft.Extensions.FileProviders;

namespace HttpGetUrl;

public abstract class ContentDownloader(Uri uri, Uri referrer, IFileProvider workingFolder, CancellationTokenSource cancellationTokenSource) : IDisposable
{
    protected Uri uri = uri;
    protected Uri referrer = referrer;
    public IFileProvider WorkingFolder { get; } = workingFolder;

    public CancellationTokenSource CancellationTokenSource { get; } = cancellationTokenSource ?? new();
    public string FinalFileName { get; protected set; } = string.Empty;
    public string[] FragmentFileNames { get; protected set; } = [];
    public long EstimatedContentLength { get; protected set; }

    public abstract Task Analysis();
    public abstract Task<long> Download();
    public abstract Task<bool> Merge();
    public abstract void Dispose();

    public static ContentDownloader Create(Uri uri, Uri referrer, IFileProvider workingFolder, CancellationTokenSource cancellationTokenSource = null)
    {
        switch (uri.Host)
        {
            case "x.com":
            case "t.co":
                return new TwitterDownloader(uri, referrer, workingFolder, cancellationTokenSource);
            case "youtu.be":
            default:
                return new HttpDownloader(uri, referrer, workingFolder, cancellationTokenSource);
        }
    }
}
