using System.Net;
using System.Net.Http.Headers;

namespace HttpGetUrl.Downloaders;

public abstract class ContentDownloader
{
    protected readonly DownloaderFactory _downloaderFactory;
    protected readonly StorageService _storageService;
    protected readonly TaskService _taskService;
    protected readonly TaskStorageCache _taskCache;
    protected readonly ProxyService _proxyService;
    protected readonly int _maxRetry;

    protected HttpClientHandler _httpClientHandler;

    public ContentDownloader(TaskFile task, CancellationTokenSource cancellationTokenSource, DownloaderFactory downloaderFactory, StorageService storageService, TaskService taskService, TaskStorageCache taskCache, ProxyService proxyService, IConfiguration configuration)
    {
        _downloaderFactory = downloaderFactory;
        _storageService = storageService;
        _taskService = taskService;
        _taskCache = taskCache;
        _proxyService = proxyService;
        _maxRetry = configuration.GetValue<int>("Hget:MaxRetry", 5);
        CurrentTask = task;
        CancellationTokenSource = cancellationTokenSource;

        task.DownloaderType = GetType().FullName;
    }

    public TaskFile CurrentTask { get; set; }
    public CancellationTokenSource CancellationTokenSource { get; }

    public abstract Task Analysis();
    public abstract Task Download();
    public abstract Task Resume();

    protected virtual HttpClient CreateHttpClient(string domain)
    {
        if (_httpClientHandler == null)
        {
            _httpClientHandler = new HttpClientHandler();
            if (_proxyService.TestUseProxy(domain))
                _httpClientHandler.Proxy = new WebProxy(_proxyService.Proxy);
            _httpClientHandler.UseCookies = true;
            foreach (var token in _storageService.GetTokens())
            {
                _httpClientHandler.CookieContainer.Add(new Cookie(token.Name, token.Value) { Domain = token.Domain, Expires = token.Expires });
            }
        }

        var httpClient = new HttpClient(_httpClientHandler) { Timeout = TimeSpan.FromSeconds(30) };
        return httpClient;
    }

    public async Task ExecuteDownloadProcessAsync()
    {
        CurrentTask.ErrorMessage = null;
        _taskCache.SaveTaskStatusDeferred(CurrentTask, TaskStatus.Downloading);
        var retry = true;
        var times = 0;
        while (retry)
        {
            retry = false;
            try
            {
                if (CurrentTask.EstimatedLength == -1 || CurrentTask.DownloadedLength < CurrentTask.EstimatedLength)
                {
                    await Analysis();
                    await Download();
                }
                _taskCache.SaveTaskStatusDeferred(CurrentTask, TaskStatus.Completed);
            }
            catch (NotSupportedException ex) when (this is YtdlpDownloader)
            {
                var downloader = _downloaderFactory.CreateHttpDownloader(CurrentTask);
                _ = downloader.ExecuteDownloadProcessAsync();
            }
            catch (Exception ex) when
            ((ex is IOException || ex is TimeoutException || ex is HttpRequestException)
            && this is HttpDownloader httpDownloader && ++times < _maxRetry)
            {
                retry = true;
                httpDownloader.RequestRange = new RangeItemHeaderValue(CurrentTask.DownloadedLength, null);
                await Task.Delay(times * 15_000);
            }
            catch (Exception ex)
            {
                CurrentTask.ErrorMessage = $"\"{CurrentTask.FileName}\" {Utility.FormatSize(CurrentTask.EstimatedLength - CurrentTask.DownloadedLength)}/{Utility.FormatSize(CurrentTask.EstimatedLength)} loss, retried {times} times. {ex.GetType().Name}: {Utility.TruncateString(ex.Message, 100, 200)}.";
                _taskCache.SaveTaskStatusDeferred(CurrentTask, TaskStatus.Error);
            }
        }
    }

    public HttpDownloader ForkToHttpDownloader(Uri url, Uri referrer = null, string filename = null, int? seq = null)
    {
        TaskFile newTask = _taskCache.GetTaskItems(CurrentTask.TaskId).FirstOrDefault(x => x.FileName == filename || x.Seq == seq)
            ?? _taskCache.GetNextTaskItemSequence(CurrentTask.TaskId);
        newTask.Url = url;
        newTask.Referrer = referrer;
        newTask.FileName = filename;
        var http = _downloaderFactory.CreateHttpDownloader(newTask, CancellationTokenSource);
        http._httpClientHandler = _httpClientHandler;
        return http;
    }
}
