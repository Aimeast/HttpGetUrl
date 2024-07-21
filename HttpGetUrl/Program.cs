using Microsoft.Extensions.FileProviders;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HttpGetUrl
{
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

            app.MapGet("/Api", () =>
            {
                var selector = builder.Environment.ContentRootFileProvider
                .GetDirectoryContents("wwwroot")
                .Where(x => x.IsDirectory && Regex.IsMatch(x.Name, @"^\d{8}-\d{6}$"))
                .Select(x =>
                {
                    var item = default(TaskItem);
                    using var provider = new PhysicalFileProvider(x.PhysicalPath);

                    try
                    {
                        using var reader = new StreamReader(provider.GetFileInfo(x.Name + ".json").CreateReadStream());
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
                        else if (downloader.FragmentFileNames.Length == 0)
                        {   // Single file download
                            var info = provider.GetFileInfo(item.Filename);
                            if (info.Exists)
                                item.DownloadedLength = info.Length;
                        }
                        else
                        {   // Fragmented download
                            item.DownloadedLength = 0;
                            foreach (var fragment in downloader.FragmentFileNames)
                            {
                                var info = provider.GetFileInfo(fragment);
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
            app.MapPost("/Api", async (TaskItem item) =>
            {
                if (!supportedProtocol.Contains(item.Url.Scheme))
                    return Results.BadRequest($"Only supported {string.Join('/', supportedProtocol)}.");

                item.DateTime = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                item.Filename = "";
                item.EstimatedLength = -1;
                var folderPath = Path.Combine(builder.Environment.ContentRootPath, "wwwroot", item.DateTime);
                Directory.CreateDirectory(folderPath);

                var downloader = ContentDownloader.Create(item.Url, item.Referrer, new PhysicalFileProvider(folderPath));
                downloaders.TryAdd(item.DateTime, downloader);

                await SaveTaskToJson(downloader.WorkingFolder, item);

                _ = Task.Factory.StartNew(async () =>
                {
                    using (downloader)
                        try
                        {
                            await downloader.Analysis();
                            item.Filename = downloader.FinalFileName;
                            item.EstimatedLength = downloader.EstimatedContentLength;
                            item.Status = DownloadStatus.Downloading;
                            await SaveTaskToJson(downloader.WorkingFolder, item);

                            item.DownloadedLength = await downloader.Download();
                            item.Status = downloader.FragmentFileNames.Length == 0 ? DownloadStatus.Completed : DownloadStatus.Merging;
                            await SaveTaskToJson(downloader.WorkingFolder, item);

                            if (downloader.FragmentFileNames.Length > 0)
                            {
                                await downloader.Merge();
                                item.Status = DownloadStatus.Completed;
                                await SaveTaskToJson(downloader.WorkingFolder, item);
                            }
                        }
                        catch
                        {
                            item.Status = DownloadStatus.Error;
                            await SaveTaskToJson(downloader.WorkingFolder, item);
                        }
                        finally
                        {
                            downloaders.TryRemove(item.DateTime, out _);
                        }
                }).ConfigureAwait(false);

                return Results.Ok();
            });
            app.MapDelete("/Api", async (string datetime) =>
            {
                try
                {
                    if (downloaders.TryGetValue(datetime, out var downloader))
                        await downloader.CancellationTokenSource.CancelAsync();
                    while (downloaders.ContainsKey(datetime))
                        await Task.Delay(10);

                    var folderPath = Path.Combine(builder.Environment.ContentRootPath, "wwwroot", datetime);
                    if (Directory.Exists(folderPath))
                        Directory.Delete(folderPath, true);

                    return Results.Ok();
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(ex.Message);
                }
            });

            app.Run();
        }

        private static async Task SaveTaskToJson(IFileProvider fileProvider, TaskItem item)
        {
            var jsonPath = fileProvider.GetFileInfo(item.DateTime + ".json").PhysicalPath;
            var content = JsonSerializer.Serialize(item);
            await File.WriteAllTextAsync(jsonPath, content);
        }

        private class TaskItem
        {
            public string DateTime { get; set; }
            public Uri Url { get; set; }
            public Uri Referrer { get; set; }
            public string Filename { get; set; }
            public long EstimatedLength { get; set; } // -1 means unknow length.
            public long DownloadedLength { get; set; }
            public DownloadStatus Status { get; set; }
        }

        private enum DownloadStatus
        {
            Error = -1,
            Pending = 0,
            Downloading = 1,
            Merging = 2,
            Completed = 3,
        }
    }
}
