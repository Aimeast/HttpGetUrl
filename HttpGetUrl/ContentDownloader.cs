using System.Net;
using System.Net.Http.Headers;

namespace HttpGetUrl;

public abstract class ContentDownloader(TaskFile task, CancellationTokenSource cancellationTokenSource, DownloaderFactory downloaderFactory, StorageService storageService, TaskService taskService, TaskStorageCache taskCache, IConfiguration configuration)
{
    protected readonly string _proxy = configuration.GetValue<string>("Hget:Proxy");
    protected readonly DownloaderFactory _downloaderFactory = downloaderFactory;
    protected readonly StorageService _storageService = storageService;
    protected readonly TaskService _taskService = taskService;
    protected readonly TaskStorageCache _taskCache = taskCache;

    protected HttpClientHandler _httpClientHandler;

    public TaskFile CurrentTask { get; set; } = task;
    public CancellationTokenSource CancellationTokenSource { get; } = cancellationTokenSource;

    public abstract Task Analysis();
    public abstract Task Download();

    protected virtual HttpClient CreateHttpClient()
    {
        if (_httpClientHandler == null)
        {
            _httpClientHandler = new HttpClientHandler();
            if (_proxy != null)
                _httpClientHandler.Proxy = new WebProxy(_proxy);
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
        _taskCache.SaveTaskStatusDeferred(CurrentTask, TaskStatus.Downloading);
        var retry = true;
        var times = 0;
        while (retry)
        {
            retry = false;
            try
            {
                await Analysis();
                await Download();
                _taskCache.SaveTaskStatusDeferred(CurrentTask, TaskStatus.Completed);
            }
            catch (Exception ex) when
            ((ex is IOException || ex is TimeoutException) && this is HttpDownloader httpDownloader && times++ < 3)
            {
                retry = true;
                httpDownloader.RequestRange = new RangeItemHeaderValue(CurrentTask.DownloadedLength, null);
            }
            catch (Exception ex)
            {
                CurrentTask.ErrorMessage = $"\"{CurrentTask.FileName}\" {Utility.FormatSize(CurrentTask.EstimatedLength - CurrentTask.DownloadedLength)}/{Utility.FormatSize(CurrentTask.EstimatedLength)} loss, retried {times} times. {ex.GetType().Name}: {Utility.TruncateString(ex.Message, 100, 200)}.";
                _taskCache.SaveTaskStatusDeferred(CurrentTask, TaskStatus.Error);
            }
        }
    }

    public HttpDownloader ForkToHttpDownloader(Uri url)
    {
        var newTask = _taskCache.GetNextTaskItemSequence(CurrentTask.TaskId);
        newTask.Url = url;
        var http = _downloaderFactory.CreateHttpDownloader(newTask, CancellationTokenSource);
        http._httpClientHandler = _httpClientHandler;
        return http;
    }
}
