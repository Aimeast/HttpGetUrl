using HttpGetUrl.Models;
using System.Text.Json.Nodes;
using YoutubeDLSharp;

namespace HttpGetUrl;

public class HgetApp(DownloaderFactory downloaderFactory, StorageService storageService, TaskService taskService, TaskStorageCache taskCache, ProxyService proxyService, PwService pwService)
{
    private readonly DownloaderFactory _downloaderFactory = downloaderFactory;
    private readonly StorageService _storageService = storageService;
    private readonly TaskService _taskService = taskService;
    private readonly TaskStorageCache _taskCache = taskCache;
    private readonly PwService _pwService = pwService;
    private readonly DateTimeOffset _statupTime = DateTimeOffset.Now;

    public IEnumerable<TaskFile[]> GetTaskItems(string userSpace)
    {
        var ids = _storageService.GetAllTaskId(userSpace);
        var items = new List<TaskFile[]>();
        foreach (var id in ids)
        {
            var item = _taskCache.GetTaskItems(userSpace, id);
            foreach (var e in item)
            {
                e.UserSpace = userSpace;
            }
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
                if (item[i].Status == TaskStatus.Downloading)
                {
                    item[i].EstimatedLength = subTasks.Sum(x => x.EstimatedLength);
                    item[i].DownloadedLength = subTasks.Sum(x => x.DownloadedLength);
                }
                if (string.IsNullOrEmpty(item[i].ErrorMessage))
                    item[i].ErrorMessage = string.Join(" | ", subTasks.Select(x => x.ErrorMessage).Where(x => !string.IsNullOrEmpty(x)));
                if (subTasks.Length > 0 && item[i].Status == TaskStatus.Pending)
                    item[i].Status = (TaskStatus)subTasks.Min(x => (int)x.Status);
            }
        }
        return item.Take(1).Concat(item.Skip(1).Where(x => !x.IsHide)).ToArray();
    }

    public IResult CreateTask(string userSpace, TaskFile task)
    {
        if (!task.Url.IsAbsoluteUri || task.Url.Scheme != Uri.UriSchemeHttp && task.Url.Scheme != Uri.UriSchemeHttps)
        {
            return Results.BadRequest($"Only {string.Join('/', [Uri.UriSchemeHttp, Uri.UriSchemeHttps])} is supported.");
        }

        task.UserSpace = userSpace;
        task.TaskId = DateTime.Now.ToString("yyMMdd-HHmmss");
        task.EstimatedLength = -1;
        task.Status = TaskStatus.Pending;

        var downloader = _downloaderFactory.CreateDownloader(task);
        _storageService.PrepareDirectory(userSpace, task.TaskId);
        _storageService.SaveTasks(userSpace, [task]);
        _taskService.QueueTask(new TaskService.TaskInfo(userSpace, task.TaskId,
            task.Seq, downloader.ExecuteDownloadProcessAsync, downloader.CancellationTokenSource));

        return Results.Ok();
    }

    public IResult DeleteTask(string userSpace, string taskId)
    {
        try
        {
            _taskService.CancelTask(userSpace, taskId);
            _taskCache.DeleteTask(userSpace, taskId);

            return Results.Ok();
        }
        catch (Exception ex)
        {
            return Results.BadRequest(ex.Message);
        }
    }

    public IResult ResumeTask(string userSpace, string taskId)
    {
        if (_taskService.ExistTask(userSpace, taskId))
            return Results.Conflict($"Task {taskId} is running, not able to resume it.");

        var tasks = _taskCache.GetTaskItems(userSpace, taskId);
        var downloader = _downloaderFactory.CreateDownloader(tasks[0]);
        downloader.Resume();

        return Results.Ok();
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

    public async ValueTask<JsonObject> GetSystemInfoAsync(HttpContext context)
    {
        var parms = context.Request.QueryString.Value.TrimStart('?').ToLower().Split(',').Distinct().ToArray();
        var infos = new JsonObject();

        // Default infos
        {
            infos.Add("protocol", context.Request.Protocol);

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
            infos.Add("playwrightVersion", await _pwService.GetPlaywrightVersionAsync());
        }

        if (parms.Contains("ytdlp"))
        {
            infos.Add("ytdlpVersion", await Utility.RunCmdFirstLine(Path.Combine(".hg", Utils.YtDlpBinaryName), "--version"));
        }

        if (parms.Contains("ffmpeg"))
        {
            infos.Add("ffmpegVersion", await Utility.RunCmdFirstLine(Utils.FfmpegBinaryName, "-version"));
        }

        if (parms.Contains("startup"))
        {
            infos.Add("startupTime", _statupTime);
        }

        if (parms.Contains("tasks"))
        {
            infos.Add("remainedConcurrency", _taskService.RemainedConcurrency);
            infos.Add("tasks", new JsonArray(_taskService.ListTasks().Select(x => JsonValue.Create(x)).ToArray()));
        }

        return infos;
    }

    public async ValueTask<string> UpgradeYtdlp()
    {
        var arg = "-U";
        if (proxyService.TestUseProxy("github.com"))
            arg += " --proxy " + proxyService.Proxy;

        await Utility.RunCmdFirstLine(Path.Combine(".hg", Utils.YtDlpBinaryName), arg, true);
        return await Utility.RunCmdFirstLine(Path.Combine(".hg", Utils.YtDlpBinaryName), "--version");
    }
}
