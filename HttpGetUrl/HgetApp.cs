﻿using System.Text.Json.Nodes;
using YoutubeDLSharp;

namespace HttpGetUrl;

public class HgetApp(DownloaderFactory downloaderFactory, StorageService storageService, TaskService taskService, TaskStorageCache taskCache, PwService pwService)
{
    private readonly DownloaderFactory _downloaderFactory = downloaderFactory;
    private readonly StorageService _storageService = storageService;
    private readonly TaskService _taskService = taskService;
    private readonly TaskStorageCache _taskCache = taskCache;
    private readonly PwService _pwService = pwService;

    public IEnumerable<TaskFile[]> GetTaskItems()
    {
        var ids = _storageService.GetAllTaskId();
        var items = new List<TaskFile[]>();
        foreach (var id in ids)
        {
            var item = _taskCache.GetTaskItems(id);
            if (item != null)
            {
                item = GroupItem(item);
                items.Add(item);
            }
        }
        return items;
    }

    private TaskFile[] GroupItem(TaskFile[] item)
    {
        for (var i = 1; i < item.Length; i++)
        {
            if (item[i].IsVirtual)
            {
                var subTasks = item.Where(x => x.ParentSeq == item[i].Seq).ToArray();
                item[i].EstimatedLength = subTasks.Sum(x => x.EstimatedLength);
                item[i].DownloadedLength = subTasks.Sum(x => x.DownloadedLength);
                if (string.IsNullOrEmpty(item[i].ErrorMessage))
                    item[i].ErrorMessage = string.Join(" | ", subTasks.Select(x => x.ErrorMessage).Where(x => !string.IsNullOrEmpty(x)));
                if (subTasks.Length > 0 && item[i].Status == TaskStatus.Pending)
                    item[i].Status = (TaskStatus)subTasks.Min(x => (int)x.Status);
            }
        }
        return item.Take(1).Concat(item.Skip(1).Where(x => !x.IsHide)).ToArray();
    }

    public IResult CreateTask(TaskFile task)
    {
        if (task.Url.Scheme != Uri.UriSchemeHttp && task.Url.Scheme != Uri.UriSchemeHttps)
        {
            return Results.BadRequest($"Only supported {string.Join('/', [Uri.UriSchemeHttp, Uri.UriSchemeHttps])}.");
        }

        task.TaskId = DateTime.Now.ToString("yyMMdd-HHmmss");
        task.EstimatedLength = -1;
        task.Status = TaskStatus.Pending;

        var downloader = _downloaderFactory.CreateDownloader(task);
        _storageService.PrepareDirectory(task.TaskId);
        _storageService.SaveTasks([task]);
        _taskService.QueueTask(new TaskService.TaskInfo(task.TaskId, task.Seq, downloader.ExecuteDownloadProcessAsync, downloader.CancellationTokenSource));

        return Results.Ok();
    }

    public IResult DeleteTask(string taskId)
    {
        try
        {
            _taskService.CancelTask(taskId);
            _taskCache.DeleteTask(taskId);

            return Results.Ok();
        }
        catch (Exception ex)
        {
            return Results.BadRequest(ex.Message);
        }
    }

    public Token[] GetTokens()
    {
        return _storageService.GetTokens();
    }

    public async Task UpdateTokensAsync(Token[] tokens)
    {
        _storageService.SaveTokens(tokens);
        await _pwService.UpdateTokesAsync(tokens);
    }

    public async Task<JsonObject> GetSystemInfoAsync(string q)
    {
        var parms = (string.IsNullOrWhiteSpace(q) ? "diskusage" : q)
            .Split(',').Select(x => x.ToLower()).Distinct().OrderBy(x => x).ToArray();
        var infos = new JsonObject();
        if (parms.Contains("diskusage"))
        {
            var driveInfo = DriveInfo.GetDrives().FirstOrDefault(x => _storageService.GetContentRootPath().StartsWith(x.Name, StringComparison.OrdinalIgnoreCase));
            var info = new JsonObject
            {
                ["freeSpace"] = driveInfo?.AvailableFreeSpace ?? 0,
                ["diskSize"] = driveInfo?.TotalSize ?? 0,
            };
            infos.Add("diskUsage", info);
        }

        if (parms.Contains("hgetver"))
        {
            var info = new JsonObject
            {
                // Ignore the IntelliSense and first compilation error messages.
                // The compiler (MsBuild Target) will generate the corresponding code during the first compilation,
                // and the second compilation will pass.
                ["Version"] = VersionInfo.Version,
                ["GitLog"] = VersionInfo.GitLog,
                ["BuildDateTime"] = VersionInfo.BuildDateTime,
                ["Configuration"] = VersionInfo.Configuration,
            };
            infos.Add("hgetVer", info);
        }

        if (parms.Contains("playwright"))
        {
            infos.Add("playwrightVersion", await _pwService.GetBrowserVersionAsync());
        }

        if (parms.Contains("ytdlp"))
        {
            infos.Add("ytdlpVersion", await Utility.RunCmdFirstLine(Path.Combine(".hg", Utils.YtDlpBinaryName), "--version"));
        }

        if (parms.Contains("ffmpeg"))
        {
            infos.Add("ffmpegVersion", await Utility.RunCmdFirstLine(Utils.FfmpegBinaryName, "-version"));
        }

        if (parms.Contains("tasks"))
        {
            infos.Add("remainedConcurrency", _taskService.RemainedConcurrency);
            infos.Add("tasks", new JsonArray(_taskService.ListTasks().Select(x => JsonValue.Create(x)).ToArray()));
        }

        return infos;
    }
}
