using Microsoft.Playwright;
using System.Text.RegularExpressions;

namespace HttpGetUrl.Downloaders;

public abstract class PwDownloader(TaskFile task, CancellationTokenSource cancellationTokenSource, DownloaderFactory downloaderFactory, StorageService storageService, TaskService taskService, TaskStorageCache taskCache, ProxyService proxyService, PwService pwService, IConfiguration configuration)
    : ContentDownloader(task, cancellationTokenSource, downloaderFactory, storageService, taskService, taskCache, proxyService, configuration)
{
    private readonly PwService _pwService = pwService;

    public override async Task Analysis()
    {
        var urls = new List<string>();
        var tcs = new TaskCompletionSource();
        var page = await _pwService.NewPageAsync();
        page.Response += async (_, response) =>
        {
            if (CancellationTokenSource.IsCancellationRequested)
            {
                tcs.TrySetCanceled();
                await page.CloseAsync();
                return;
            }

            if (tcs.Task.IsCompleted || page.IsClosed)
                return;

            var match = Regex.Match(response.Url, UrlRegex);
            if (match.Success)
            {
                try
                {
                    var responseBody = await response.TextAsync();
                    var urls = GetUrls(match, responseBody);

                    foreach (var (url, fileName) in urls)
                    {
                        var downloader = ForkToHttpDownloader(new Uri(url), filename: fileName);
                        _taskService.QueueTask(new TaskService.TaskInfo(downloader.CurrentTask.TaskId, downloader.CurrentTask.Seq, downloader.ExecuteDownloadProcessAsync, downloader.CancellationTokenSource));
                    }
                }
                catch (Exception ex)
                {
                    CurrentTask.ErrorMessage = ex.Message;
                    _taskCache.SaveTaskStatusDeferred(CurrentTask, TaskStatus.Error);
                }
                finally
                {
                    tcs.TrySetResult();
                }
            }
        };
        await page.GotoAsync(CurrentTask.Url.ToString(), new PageGotoOptions
        {
            Timeout = 60_000 * 2, // 2 minuts
            WaitUntil = WaitUntilState.DOMContentLoaded, // wait until the DOMContentLoaded event is fired, not all resources
        });
        await tcs.Task;
        await page.CloseAsync();
    }

    public override async Task Download()
    {
        await Task.CompletedTask;
    }

    public override async Task Resume()
    {
        var tasks = _taskCache.GetTaskItems(CurrentTask.TaskId);
        if (tasks[0].Status == TaskStatus.Completed)
        {
            for (var i = 1; i < tasks.Length; i++)
            {
                var task = tasks[i];
                if (task.Status != TaskStatus.Completed)
                {
                    task.ErrorMessage = null;
                    _taskCache.SaveTaskStatusDeferred(task, TaskStatus.Pending);
                    var downloader = ForkToHttpDownloader(task.Url, seq: task.Seq);
                    _taskService.QueueTask(new TaskService.TaskInfo(downloader.CurrentTask.TaskId, downloader.CurrentTask.Seq, downloader.ExecuteDownloadProcessAsync, downloader.CancellationTokenSource));
                }
            }
        }
        else
        {
            _taskCache.FlushTasks(tasks[..1]);

            CurrentTask.ContentText = null;
            CurrentTask.ErrorMessage = null;
            _taskCache.SaveTaskStatusDeferred(CurrentTask, TaskStatus.Pending);

            _taskService.QueueTask(new TaskService.TaskInfo(CurrentTask.TaskId, CurrentTask.Seq, ExecuteDownloadProcessAsync, CancellationTokenSource));
        }
        await Task.CompletedTask;
    }

    protected abstract string UrlRegex { get; }

    protected abstract List<(string Url, string FileName)> GetUrls(Match match, string content);
}
