using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.FileProviders.Physical;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HttpGetUrl;

public class Program
{
    public static void Main(string[] args)
    {
        var supportedProtocol = new[] { Uri.UriSchemeHttp, Uri.UriSchemeHttps };
        var downloaders = new ConcurrentDictionary<string, ContentDownloader>();
        var builder = WebApplication.CreateBuilder(args);

        var app = builder.Build();
        if (!builder.Environment.IsDevelopment())
        {
            app.UseHttpsRedirection();
            app.UseHsts();
        }
        var options = new FileServerOptions();
        options.StaticFileOptions.ServeUnknownFileTypes = true;
        options.StaticFileOptions.DefaultContentType = "application/octet-stream";
        app.UseFileServer(options);

        var dataProvider = new PhysicalFileProvider(Path.Combine(builder.Environment.ContentRootPath, ".hg"), ExclusionFilters.None);
        var tokenFileInfo = dataProvider.GetFileInfo("tokens.json");
        if (!tokenFileInfo.Exists)
        {
            File.WriteAllText(tokenFileInfo.PhysicalPath, "[]");
            tokenFileInfo = dataProvider.GetFileInfo("tokens.json");
        }
        var tokens = Token.GetTokens(tokenFileInfo);
        ContentDownloader.InitService(new PwOptions
        {
            UserDataDir = dataProvider.Root,
            Proxy = builder.Configuration["HttpGetUrl:Proxy"],
            Tokens = tokens,
        });
        var downloaderService = new DownloaderService().RegisterAll();

        #region app.Map to Task
        app.MapGet("/task", () =>
        {
            var selector = builder.Environment.ContentRootFileProvider
            .GetDirectoryContents("wwwroot")
            .Where(x => x.IsDirectory && Regex.IsMatch(x.Name, @"^hg-\d{8}-\d{6}$"))
            .Select(x =>
            {
                var item = default(TaskItem);
                using var provider = new PhysicalFileProvider(x.PhysicalPath, ExclusionFilters.None);

                try
                {
                    using var reader = new StreamReader(provider.GetFileInfo($".{x.Name}.json").CreateReadStream());
                    item = JsonSerializer.Deserialize<TaskItem>(reader.ReadToEnd());
                    ArgumentNullException.ThrowIfNull(item);
                }
                catch
                {
                    return null;
                }

                if (item.Status == DownloadStatus.Downloading || item.Status == DownloadStatus.Merging)
                {
                    if (!downloaders.TryGetValue(item.DateTime, out var downloader))
                    {
                        item.Status = DownloadStatus.Error;
                    }
                    else
                    {
                        item.DownloadedLength = 0;
                        foreach (var filename in downloader.FinalFileNames.Concat(downloader.FragmentFileNames))
                        {
                            var info = provider.GetFileInfo(filename);
                            if (info.Exists)
                                item.DownloadedLength += info.Length;
                        }
                    }
                }
                return item;
            })
            .Where(x => x != null)
            .OrderByDescending(x => x.DateTime);
            return selector;
        });
        app.MapPost("/task", async (TaskItem item) =>
        {
            if (!supportedProtocol.Contains(item.Url.Scheme))
                return Results.BadRequest($"Only supported {string.Join('/', supportedProtocol)}.");

            item.DateTime = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            item.Files = [];
            item.EstimatedLength = -1;
            var folderPath = Path.Combine(builder.Environment.ContentRootPath, "wwwroot", $"hg-{item.DateTime}");
            Directory.CreateDirectory(folderPath);

            var downloader = downloaderService.CreateDownloader(item.Url, item.Referrer, new PhysicalFileProvider(folderPath, ExclusionFilters.None));
            downloaders.TryAdd(item.DateTime, downloader);

            await SaveTaskToJson(downloader.WorkingFolder, item);

            _ = Task.Factory.StartNew(async () =>
            {
                using (downloader)
                    try
                    {
                        item.Status = DownloadStatus.Downloading;
                        await SaveTaskToJson(downloader.WorkingFolder, item);

                        await downloader.Analysis();
                        if (downloader.FinalFileNames.Length == 0)
                        {
                            item.Status = DownloadStatus.NotFound;
                            await SaveTaskToJson(downloader.WorkingFolder, item);
                            return;
                        }
                        else
                        {
                            item.Files = downloader.FinalFileNames;
                            item.EstimatedLength = downloader.EstimatedContentLength;
                            await SaveTaskToJson(downloader.WorkingFolder, item);
                        }

                        item.DownloadedLength = await downloader.Download();
                        item.Status = downloader.FragmentFileNames.Length == 0 ? DownloadStatus.Completed : DownloadStatus.Merging;
                        await SaveTaskToJson(downloader.WorkingFolder, item);

                        if (downloader.FragmentFileNames.Length > 0)
                        {
                            var length = await downloader.Merge();
                            if (length > 0)
                                item.DownloadedLength = length;
                            item.Status = DownloadStatus.Completed;
                            await SaveTaskToJson(downloader.WorkingFolder, item);
                        }
                    }
                    catch (Exception ex)
                    {
                        item.Status = DownloadStatus.Error;
                        // ffmpeg message may very large. So, truncate
                        item.ErrorMessage = $"{ex.GetType().Name}: {Utility.TruncateString(ex.Message, 100, 300)}";
                        await SaveTaskToJson(downloader.WorkingFolder, item);
                    }
                    finally
                    {
                        downloaders.TryRemove(item.DateTime, out _);
                    }
            }).ConfigureAwait(false);

            return Results.Ok();
        });
        app.MapDelete("/task", async (string datetime) =>
        {
            try
            {
                if (downloaders.TryGetValue(datetime, out var downloader))
                    await downloader.CancellationTokenSource.CancelAsync();
                while (downloaders.ContainsKey(datetime))
                    await Task.Delay(10);

                var folderPath = Path.Combine(builder.Environment.ContentRootPath, "wwwroot", $"hg-{datetime}");
                if (Directory.Exists(folderPath))
                    Directory.Delete(folderPath, true);

                return Results.Ok();
            }
            catch (Exception ex)
            {
                return Results.BadRequest(ex.Message);
            }
        });
        #endregion
        #region app.Map to Tokens
        app.MapGet("/tokens", () =>
        {
            var tokens = Token.GetTokens(tokenFileInfo);
            return tokens;
        });
        app.MapPost("/tokens", async (Token[] tokens) =>
        {
            ContentDownloader.PwOptions.Tokens = tokens;
            await Token.SaveTokensAsync(tokenFileInfo, tokens);
            await PwService.GetInstance().UpdateTokesAsync(tokens);
            return Results.Ok();
        });
        #endregion
        #region app.Map to Info
        app.MapGet("/info", (string[] parms) =>
        {
            var driveInfo = DriveInfo.GetDrives().FirstOrDefault(x => builder.Environment.ContentRootPath.StartsWith(x.Name, StringComparison.OrdinalIgnoreCase));
            return new
            {
                FreeSpace = driveInfo.AvailableFreeSpace,
                TotalSize = driveInfo.TotalSize,
            };
        });
        #endregion

        app.Run();

        PwService.Close();
    }

    private static async Task SaveTaskToJson(IFileProvider fileProvider, TaskItem item)
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
        public long EstimatedLength { get; set; } // -1 means unknow length.
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
