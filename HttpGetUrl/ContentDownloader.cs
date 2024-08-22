using Microsoft.Extensions.FileProviders;
using System.Net;

namespace HttpGetUrl;

public abstract class ContentDownloader(Uri uri, Uri referrer, IFileProvider workingFolder, CancellationTokenSource cancellationTokenSource) : IDisposable
{
    protected Uri uri = uri;
    protected Uri referrer = referrer;
    public IFileProvider WorkingFolder { get; } = workingFolder;

    protected HttpClientHandler httpClientHandler;

    public CancellationTokenSource CancellationTokenSource { get; } = cancellationTokenSource;
    public string[] FinalFileNames { get; internal set; } = [];
    public string[] FragmentFileNames { get; protected set; } = [];
    public long EstimatedContentLength { get; protected set; }

    public abstract Task Analysis();
    public abstract Task<long> Download();
    public abstract Task Merge();
    public abstract void Dispose();

    protected virtual HttpClient CreateHttpClient()
    {
        if (httpClientHandler == null)
        {
            httpClientHandler = new HttpClientHandler();
            if (PwOptions.Proxy != null)
                httpClientHandler.Proxy = new WebProxy(PwOptions.Proxy);
            httpClientHandler.UseCookies = PwOptions.Tokens.Length > 0;
            var attr = this.GetType().GetCustomAttribute<DownloaderAttribute>();
            if (attr != null)
            {
                var token = PwOptions.Tokens.FirstOrDefault(x => x.Identity == attr.Identity);
                if (token != null)
                {
                    foreach (var domain in attr.SupportedDomains)
                        httpClientHandler.CookieContainer.Add(new Cookie(token.Key, token.Value) { Domain = domain });
                }
            }
        }

        var httpClient = new HttpClient(httpClientHandler);
        return httpClient;
    }

    public HttpDownloader ForkToHttpDownloader(Uri uri, Uri referrer)
    {
        var http = new HttpDownloader(uri, referrer, WorkingFolder, CancellationTokenSource)
        {
            httpClientHandler = httpClientHandler
        };
        return http;
    }

    public static PwOptions PwOptions { get; private set; }

    public static void InitService(PwOptions pwOptions)
    {
        PwOptions = pwOptions;
        PwService.InitService(pwOptions);
    }
}
