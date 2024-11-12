﻿using System.Net.Http.Headers;

namespace HttpGetUrl;

public class HttpDownloader(TaskFile task, CancellationTokenSource cancellationTokenSource, DownloaderFactory downloaderFactory, StorageService storageService, TaskService taskService, TaskStorageCache taskCache, IConfiguration configuration)
    : ContentDownloader(task, cancellationTokenSource, downloaderFactory, storageService, taskService, taskCache, configuration)
{
    private HttpResponseMessage httpResponseMessage = null;

    public RangeItemHeaderValue RequestRange { get; set; }

    public override async Task Analysis()
    {
        var httpClient = CreateHttpClient();
        if (CurrentTask.Referrer != null)
            httpClient.DefaultRequestHeaders.Referrer = CurrentTask.Referrer;
        if (RequestRange != null)
        {
            httpClient.DefaultRequestHeaders.Range = new RangeHeaderValue();
            httpClient.DefaultRequestHeaders.Range.Ranges.Add(RequestRange);
        }

        httpResponseMessage = await httpClient.GetAsync(CurrentTask.Url, HttpCompletionOption.ResponseHeadersRead, CancellationTokenSource.Token);
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
        CurrentTask.EstimatedLength = contentLength;
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
            using var fileStream = _storageService.OpenFileStream(CurrentTask.TaskId, CurrentTask.FileName);
            while ((bytesRead = await responseStream.ReadAsync(buffer, linkedCancelSource.Token)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                CurrentTask.DownloadedLength += bytesRead;
                _taskCache.SaveTaskStatusDeferred(CurrentTask);
                timeoutCancelSource.CancelAfter(timeout);
            }
        }
        catch (OperationCanceledException)
        {
            if (isTimeout) throw new TimeoutException($"Timeout in {timeout}ms.");
            else throw;
        }
    }
}
