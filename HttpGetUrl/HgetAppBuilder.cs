using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.FileProviders.Physical;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using YoutubeDLSharp;

namespace HttpGetUrl;

public class HgetAppBuilder(WebApplicationBuilder builder)
{
    private readonly WebApplicationBuilder _builder = builder;
    private WebApplication _app;
    private ConcurrentDictionary<string, ContentDownloader> _downloaders;
    private DownloaderService _downloaderService;

    public HgetAppBuilder ConfigureApplication()
    {
        _app = _builder.Build();
        _downloaders = new ConcurrentDictionary<string, ContentDownloader>();

        if (!_builder.Environment.IsDevelopment())
        {
            _app.UseHttpsRedirection()
                .UseHsts();
        }

        _app.UseFileServer(new FileServerOptions
        {
            StaticFileOptions =
            {
                ServeUnknownFileTypes = true,
                DefaultContentType = "application/octet-stream"
            }
        });

        InitializeTokenFile();
        return this;
    }

    public HgetAppBuilder RegisterDownloaderService()
    {
        var dataProvider = new PhysicalFileProvider(Path.Combine(_builder.Environment.ContentRootPath, ".hg"), ExclusionFilters.None);
        var tokens = Token.GetTokens(dataProvider.GetFileInfo("tokens.json"));
        ContentDownloader.InitService(new PwOptions
        {
            UserDataDir = dataProvider.Root,
            Proxy = _builder.Configuration["HttpGetUrl:Proxy"],
            Tokens = tokens,
        });

        _downloaderService = new DownloaderService().RegisterAll();
        return this;
    }

    public HgetAppBuilder RegisterEndpoints()
    {
        _app.MapGet("/task", () => GetTaskItems());
        _app.MapPost("/task", async (TaskItem item) => await HandleTaskPost(item));
        _app.MapDelete("/task", async (string datetime) => await HandleTaskDelete(datetime));
        _app.MapGet("/tokens", () => GetTokens());
        _app.MapPost("/tokens", async (Token[] tokens) => await HandleTokensPost(tokens));
        _app.MapGet("/info", async (string q) => await GetSystemInfo(q));

        return this;
    }

    public WebApplication Build()
    {
        return _app;
    }

    private void InitializeTokenFile()
    {
        var dataProvider = new PhysicalFileProvider(Path.Combine(_builder.Environment.ContentRootPath, ".hg"), ExclusionFilters.None);
        var tokenFileInfo = dataProvider.GetFileInfo("tokens.json");
        if (!tokenFileInfo.Exists)
        {
            File.WriteAllText(tokenFileInfo.PhysicalPath, "[]");
        }
    }

    private IEnumerable<TaskItem> GetTaskItems()
    {
        return _builder.Environment.ContentRootFileProvider
            .GetDirectoryContents("wwwroot")
            .Where(x => x.IsDirectory && Regex.IsMatch(x.Name, @"^hg-\d{8}-\d{6}$"))
            .Select(x => LoadTaskItem(x))
            .Where(x => x != null)
            .OrderByDescending(x => x.DateTime);
    }

    private TaskItem LoadTaskItem(IFileInfo x)
    {
        using var provider = new PhysicalFileProvider(x.PhysicalPath, ExclusionFilters.None);
        var item = LoadTaskItemFromJson(provider, x.Name);

        if (item == null) return null;

        if (item.Status == DownloadStatus.Downloading || item.Status == DownloadStatus.Merging)
        {
            UpdateTaskItemStatus(item, provider);
        }

        return item;
    }

    private TaskItem LoadTaskItemFromJson(IFileProvider provider, string directoryName)
    {
        try
        {
            using var reader = new StreamReader(provider.GetFileInfo($".{directoryName}.json").CreateReadStream());
            var item = JsonSerializer.Deserialize<TaskItem>(reader.ReadToEnd());
            ArgumentNullException.ThrowIfNull(item);
            return item;
        }
        catch
        {
            return null;
        }
    }

    private void UpdateTaskItemStatus(TaskItem item, IFileProvider provider)
    {
        if (!_downloaders.TryGetValue(item.DateTime, out var downloader))
        {
            item.Status = DownloadStatus.Error;
        }
        else
        {
            item.DownloadedLength = downloader.FinalFileNames.Concat(downloader.FragmentFileNames)
                .Select(provider.GetFileInfo)
                .Where(info => info.Exists)
                .Sum(info => info.Length);
        }
    }

    private async Task<IResult> HandleTaskPost(TaskItem item)
    {
        if (item.Url.Scheme != Uri.UriSchemeHttp && item.Url.Scheme != Uri.UriSchemeHttps)
        {
            return Results.BadRequest($"Only supported {string.Join('/', [Uri.UriSchemeHttp, Uri.UriSchemeHttps])}.");
        }

        item.DateTime = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        item.Files = [];
        item.EstimatedLength = -1;
        var folderPath = Path.Combine(_builder.Environment.ContentRootPath, "wwwroot", $"hg-{item.DateTime}");
        Directory.CreateDirectory(folderPath);

        var downloader = _downloaderService.CreateDownloader(item.Url, item.Referrer, new PhysicalFileProvider(folderPath, ExclusionFilters.None));
        _downloaders.TryAdd(item.DateTime, downloader);

        await SaveTaskToJson(downloader.WorkingFolder, item);
        StartDownloadTask(item, downloader);

        return Results.Ok();
    }

    private void StartDownloadTask(TaskItem item, ContentDownloader downloader)
    {
        _ = Task.Factory.StartNew(async () =>
        {
            using (downloader)
            {
                try
                {
                    item.Status = DownloadStatus.Downloading;
                    await SaveTaskToJson(downloader.WorkingFolder, item);

                    await downloader.Analysis();
                    if (downloader.FinalFileNames.Length == 0)
                    {
                        item.Status = DownloadStatus.NotFound;
                    }
                    else
                    {
                        item.Files = downloader.FinalFileNames;
                        item.EstimatedLength = downloader.EstimatedContentLength;
                    }
                    await SaveTaskToJson(downloader.WorkingFolder, item);

                    item.DownloadedLength = await downloader.Download();
                    item.Status = downloader.FragmentFileNames.Length == 0 ? DownloadStatus.Completed : DownloadStatus.Merging;
                    await SaveTaskToJson(downloader.WorkingFolder, item);

                    if (downloader.FragmentFileNames.Length > 0)
                    {
                        var length = await downloader.Merge();
                        if (length > 0)
                        {
                            item.DownloadedLength = length;
                        }
                        item.Status = DownloadStatus.Completed;
                    }
                    await SaveTaskToJson(downloader.WorkingFolder, item);
                }
                catch (Exception ex)
                {
                    item.Status = DownloadStatus.Error;
                    item.ErrorMessage = $"{ex.GetType().Name}: {Utility.TruncateString(ex.Message, 100, 300)}";
                    await SaveTaskToJson(downloader.WorkingFolder, item);
                }
                finally
                {
                    _downloaders.TryRemove(item.DateTime, out _);
                }
            }
        }).ConfigureAwait(false);
    }

    private async Task<IResult> HandleTaskDelete(string datetime)
    {
        try
        {
            if (_downloaders.TryGetValue(datetime, out var downloader))
            {
                await downloader.CancellationTokenSource.CancelAsync();
            }

            while (_downloaders.ContainsKey(datetime))
            {
                await Task.Delay(10);
            }

            var folderPath = Path.Combine(_builder.Environment.ContentRootPath, "wwwroot", $"hg-{datetime}");
            if (Directory.Exists(folderPath))
            {
                Directory.Delete(folderPath, true);
            }

            return Results.Ok();
        }
        catch (Exception ex)
        {
            return Results.BadRequest(ex.Message);
        }
    }

    private Token[] GetTokens()
    {
        var dataProvider = new PhysicalFileProvider(Path.Combine(_builder.Environment.ContentRootPath, ".hg"), ExclusionFilters.None);
        return Token.GetTokens(dataProvider.GetFileInfo("tokens.json"));
    }

    private async Task HandleTokensPost(Token[] tokens)
    {
        ContentDownloader.PwOptions.Tokens = tokens;
        var dataProvider = new PhysicalFileProvider(Path.Combine(_builder.Environment.ContentRootPath, ".hg"), ExclusionFilters.None);
        var tokenFileInfo = dataProvider.GetFileInfo("tokens.json");

        await Token.SaveTokensAsync(tokenFileInfo, tokens);
        await PwService.GetInstance().UpdateTokesAsync(tokens);
    }

    private async Task<JsonObject> GetSystemInfo(string q)
    {
        var parms = (string.IsNullOrWhiteSpace(q) ? "diskusage" : q)
            .Split(',').Select(x => x.ToLower()).Distinct().OrderBy(x => x).ToArray();
        var infos = new JsonObject();
        if (parms.Contains("diskusage"))
        {
            var driveInfo = DriveInfo.GetDrives().FirstOrDefault(x => _builder.Environment.ContentRootPath.StartsWith(x.Name, StringComparison.OrdinalIgnoreCase));
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
            infos.Add("playwrightVersion", await PwService.GetInstance().GetBrowserVersion());
        }

        if (parms.Contains("ytdl"))
        {
            infos.Add("ytdlVersion", await Utility.RunCmdFirstLine(Path.Combine(".hg", Utils.YtDlpBinaryName), "--version"));
        }

        if (parms.Contains("ffmpeg"))
        {
            infos.Add("ffmpegVersion", await Utility.RunCmdFirstLine(Utils.FfmpegBinaryName, "-version"));
        }

        return infos;
    }

    private async Task SaveTaskToJson(IFileProvider fileProvider, TaskItem item)
    {
        var jsonPath = fileProvider.GetFileInfo($".hg-{item.DateTime}.json").PhysicalPath;
        var content = JsonSerializer.Serialize(item);
        await File.WriteAllTextAsync(jsonPath, content).ConfigureAwait(false);
    }

    private class TaskItem
    {
        public string DateTime { get; set; }
        public Uri Url { get; set; }
        public Uri Referrer { get; set; }
        public string[] Files { get; set; }
        public long EstimatedLength { get; set; } // -1 means unknown length.
        public long DownloadedLength { get; set; }
        public DownloadStatus Status { get; set; }
        public string ErrorMessage { get; set; }
    }

    private enum DownloadStatus
    {
        Error = -1,
        Pending = 0,
        NotFound = 1,
        Downloading = 2,
        Merging = 3,
        Completed = 4,
    }
}
