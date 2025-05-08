using System.Net.Http.Headers;

namespace HttpGetUrl.Downloaders;

[Downloader("Http", ["*"])]
public class HttpDownloader(TaskFile task, CancellationTokenSource cancellationTokenSource, DownloaderFactory downloaderFactory, StorageService storageService, TaskService taskService, TaskStorageCache taskCache, ProxyService proxyService, IConfiguration configuration)
    : ContentDownloader(task, cancellationTokenSource, downloaderFactory, storageService, taskService, taskCache, proxyService, configuration)
{
    private HttpResponseMessage httpResponseMessage = null;

    public RangeItemHeaderValue RequestRange { get; set; }

    public override async Task Analysis()
    {
        var httpClient = CreateHttpClient(CurrentTask.Url.Host);
        if (CurrentTask.Referrer != null)
            httpClient.DefaultRequestHeaders.Referrer = CurrentTask.Referrer;
        if (RequestRange != null && RequestRange.From != 0)
        {
            httpClient.DefaultRequestHeaders.Range = new RangeHeaderValue();
            httpClient.DefaultRequestHeaders.Range.Ranges.Add(RequestRange);
        }

        try
        {
            httpResponseMessage = await httpClient.GetAsync(CurrentTask.Url, HttpCompletionOption.ResponseHeadersRead, CancellationTokenSource.Token);
        }
        catch (TaskCanceledException ex)
        {
            throw new TimeoutException($"Timeout on connecting to HTTP server.");
        }
        httpResponseMessage.EnsureSuccessStatusCode();

        var contentLength = httpResponseMessage.Content.Headers.ContentLength ?? -1;
        var filename = httpResponseMessage.Content.Headers.ContentDisposition?.FileName?.Trim('"') ?? "";
        if (string.IsNullOrEmpty(filename))
        {
            filename = Path.GetFileName(httpResponseMessage.RequestMessage?.RequestUri?.LocalPath)?.Trim();
            if (string.IsNullOrEmpty(filename))
            {
                filename = "default";
            }
            var ext = Path.GetExtension(filename);
            var mediaType = httpResponseMessage.Content.Headers.ContentType?.MediaType;
            if (mediaType != "application/octet-stream" || string.IsNullOrEmpty(ext))
            {
                if (mediaType != null && Utility.Mimes.TryGetValue(mediaType, out ext))
                    ext = "." + ext.Split(',')[0];
                else
                    ext = ".bin";
                if (!filename.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                    filename += ext;
            }
        }
        CurrentTask.FileName ??= filename;
        if (RequestRange == null || RequestRange.From == 0)
        {
            CurrentTask.EstimatedLength = contentLength;
            CurrentTask.DownloadedLength = 0;
        }
        _taskCache.SaveTaskStatusDeferred(CurrentTask);
    }

    public override async Task Download()
    {
        var isTimeout = false;
        var timeout = 20_000;
        var timeoutCancelSource = new CancellationTokenSource(timeout);
        timeoutCancelSource.Token.Register(() => isTimeout = true);
        var linkedCancelSource = CancellationTokenSource
            .CreateLinkedTokenSource(CancellationTokenSource.Token, timeoutCancelSource.Token);
        try
        {
            var bytesRead = 0;
            var buffer = new byte[16 * 1024];
            using var responseStream = await httpResponseMessage.Content.ReadAsStreamAsync(linkedCancelSource.Token);
            using var fileStream = _storageService.OpenFileStream(CurrentTask.TaskId, CurrentTask.FileName, RequestRange?.From);
            while ((bytesRead = await responseStream.ReadAsync(buffer, linkedCancelSource.Token)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                CurrentTask.DownloadedLength += bytesRead;
                _taskCache.SaveTaskStatusDeferred(CurrentTask);
                timeoutCancelSource.CancelAfter(timeout);
            }
        }
        catch (OperationCanceledException) when (isTimeout)
        {
            throw new TimeoutException($"Timeout in {timeout}ms while read stream.");
        }
    }

    public override async Task Resume()
    {
        if (CurrentTask.Status == TaskStatus.Completed)
            return;

        CurrentTask.IsHide = false;
        CurrentTask.IsVirtual = false;
        CurrentTask.FileName = null;
        _taskCache.SaveTaskStatusDeferred(CurrentTask, TaskStatus.Pending);

        _taskService.QueueTask(new TaskService.TaskInfo(CurrentTask.TaskId, CurrentTask.Seq, ExecuteDownloadProcessAsync, CancellationTokenSource));
        await Task.CompletedTask;
    }
}
