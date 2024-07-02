using Microsoft.Extensions.FileProviders;
using Newtonsoft.Json;
using System.Text.RegularExpressions;

namespace HttpGetUrl
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var mimes = new Dictionary<string, string>()
                {
                    {"audio/aac",".aac"},
                    {"application/x-abiword",".abw"},
                    {"image/apng",".apng"},
                    {"application/x-freearc",".arc"},
                    {"image/avif",".avif"},
                    {"video/x-msvideo",".avi"},
                    {"application/vnd.amazon.ebook",".azw"},
                    {"application/octet-stream",".bin"},
                    {"image/bmp",".bmp"},
                    {"application/x-bzip",".bz"},
                    {"application/x-bzip2",".bz2"},
                    {"application/x-cdf",".cda"},
                    {"application/x-csh",".csh"},
                    {"text/css",".css"},
                    {"text/csv",".csv"},
                    {"application/msword",".doc"},
                    {"application/vnd.openxmlformats-officedocument.wordprocessingml.document",".docx"},
                    {"application/vnd.ms-fontobject",".eot"},
                    {"application/epub+zip",".epub"},
                    {"application/gzip",".gz"},
                    {"image/gif",".gif"},
                    {"text/html",".htm"},
                    {"image/vnd.microsoft.icon",".ico"},
                    {"text/calendar",".ics"},
                    {"application/java-archive",".jar"},
                    {"image/jpeg",".jpeg"},
                    {"text/javascript",".js"},
                    {"application/json",".json"},
                    {"application/ld+json",".jsonld"},
                    {"audio/midi",".midi"},
                    {"audio/mpeg",".mp3"},
                    {"video/mp4",".mp4"},
                    {"video/mpeg",".mpeg"},
                    {"application/vnd.apple.installer+xml",".mpkg"},
                    {"application/vnd.oasis.opendocument.presentation",".odp"},
                    {"application/vnd.oasis.opendocument.spreadsheet",".ods"},
                    {"application/vnd.oasis.opendocument.text",".odt"},
                    {"audio/ogg",".oga"},
                    {"video/ogg",".ogv"},
                    {"application/ogg",".ogx"},
                    {"font/otf",".otf"},
                    {"image/png",".png"},
                    {"application/pdf",".pdf"},
                    {"application/x-httpd-php",".php"},
                    {"application/vnd.ms-powerpoint",".ppt"},
                    {"application/vnd.openxmlformats-officedocument.presentationml.presentation",".pptx"},
                    {"application/vnd.rar",".rar"},
                    {"application/rtf",".rtf"},
                    {"application/x-sh",".sh"},
                    {"image/svg+xml",".svg"},
                    {"application/x-tar",".tar"},
                    {"image/tiff",".tiff"},
                    {"video/mp2t",".ts"},
                    {"font/ttf",".ttf"},
                    {"text/plain",".txt"},
                    {"application/vnd.visio",".vsd"},
                    {"audio/wav",".wav"},
                    {"audio/webm",".weba"},
                    {"video/webm",".webm"},
                    {"image/webp",".webp"},
                    {"font/woff",".woff"},
                    {"font/woff2",".woff2"},
                    {"application/xhtml+xml",".xhtml"},
                    {"application/vnd.ms-excel",".xls"},
                    {"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",".xlsx"},
                    {"application/xml",".xml"},
                    {"application/vnd.mozilla.xul+xml",".xul"},
                    {"application/zip",".zip"},
                    {"video/3gpp",".3gp"},
                    {"video/3gpp2",".3g2"},
                    {"application/x-7z-compressed",".7z"},
                };

            var builder = WebApplication.CreateBuilder(args);

            var app = builder.Build();
            if (!builder.Environment.IsDevelopment())
            {
                app.UseHttpsRedirection();
                app.UseHsts();
            }
            app.UseFileServer(true);

            app.MapGet("/Api", () =>
            {
                var selector = builder.Environment.ContentRootFileProvider
                .GetDirectoryContents("wwwroot")
                .Where(x => x.IsDirectory && Regex.IsMatch(x.Name, @"^\d{8}-\d{6}$"))
                .Select(x =>
                {
                    using (var provider = new PhysicalFileProvider(x.PhysicalPath))
                    using (var reader = new StreamReader(provider.GetFileInfo(x.Name + ".json").CreateReadStream()))
                    {
                        var content = reader.ReadToEnd();
                        return JsonConvert.DeserializeObject<UrlDatum>(content);
                    }
                })
                .OrderByDescending(x => x.DateTime);
                return selector;
            });
            app.MapPost("/Api", (UrlDatum item) =>
            {
                if (!Uri.TryCreate(item.Url, UriKind.Absolute, out _))
                    return Results.BadRequest("Url is illegal.");

                item.DateTime = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                var folderPath = Path.Combine(builder.Environment.ContentRootPath, "wwwroot", item.DateTime);
                Directory.CreateDirectory(folderPath);

                var content = JsonConvert.SerializeObject(item);
                var jsonPath = Path.Combine(folderPath, item.DateTime + ".json");
                File.WriteAllTextAsync(jsonPath, content);
                item.Filename = "";

                _ = Task.Factory.StartNew(async () =>
                {
                    using (var client = new HttpClient())
                    {
                        if (!string.IsNullOrEmpty(item.Referrer))
                            client.DefaultRequestHeaders.Referrer = new Uri(item.Referrer);
                        if (!string.IsNullOrEmpty(item.UserAgent))
                            client.DefaultRequestHeaders.UserAgent.ParseAdd(item.UserAgent);

                        using (var response = await client.GetAsync(item.Url))
                        {
                            item.Filename = response.Content.Headers.ContentDisposition?.FileName?.Trim('"') ?? "";
                            if (string.IsNullOrEmpty(item.Filename))
                            {
                                item.Filename = Path.GetFileName(new Uri(item.Url).LocalPath);
                            }
                            if (string.IsNullOrEmpty(Path.GetExtension(item.Filename)))
                            {
                                var mediaType = response.Content.Headers.ContentType?.MediaType;
                                if (string.IsNullOrEmpty(mediaType) || !mimes.ContainsKey(mediaType))
                                    item.Filename += ".bin";
                                else
                                    item.Filename += mimes[mediaType];
                            }

                            item.Size = response.Content.Headers.ContentLength ?? 0;
                            await File.WriteAllTextAsync(jsonPath, JsonConvert.SerializeObject(item));

                            var filePath = Path.Combine(folderPath, item.Filename);
                            using (var fileStream = File.Create(filePath))
                            {
                                await response.Content.CopyToAsync(fileStream);
                            }
                            if (item.Size == 0)
                            {
                                item.Size = new FileInfo(filePath).Length;
                                await File.WriteAllTextAsync(jsonPath, JsonConvert.SerializeObject(item));
                            }
                        }
                    }
                }).ConfigureAwait(false);

                return Results.Ok();
            });
            app.MapDelete("/Api", (string datetime) =>
            {
                try
                {
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
    }

    public class UrlDatum
    {
        public string DateTime { get; set; }
        public string Url { get; set; }
        public string Referrer { get; set; }
        public string UserAgent { get; set; }
        public string Filename { get; set; }
        public long Size { get; set; }
    }
}
