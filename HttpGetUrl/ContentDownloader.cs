using Microsoft.Extensions.FileProviders;

namespace HttpGetUrl;

public abstract class ContentDownloader(Uri uri, Uri referrer, IFileProvider workingFolder) : IDisposable
{
    protected readonly Uri uri = uri;
    protected readonly Uri referrer = referrer;
    public IFileProvider WorkingFolder { get; } = workingFolder;

    public CancellationTokenSource CancellationTokenSource { get; } = new();
    public string FinalFileName { get; protected set; } = string.Empty;
    public string[] FragmentFileNames { get; protected set; } = [];
    public long EstimatedContentLength { get; protected set; }

    public abstract Task Analysis();
    public abstract Task<long> Download();
    public abstract Task<bool> Merge();
    public abstract void Dispose();

    public static ContentDownloader Create(Uri uri, Uri referrer, IFileProvider workingFolder)
    {
        switch (uri.Host)
        {
            case "x.com":
            case "youtu.be":
            default:
                return new HttpDownloader(uri, referrer, workingFolder);
        }
    }
}
