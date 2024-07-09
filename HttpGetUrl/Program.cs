using Microsoft.Extensions.FileProviders;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace HttpGetUrl
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var mimes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // Following is from https://chromium.googlesource.com/chromium/src/net/+/refs/heads/main/base/mime_util.cc
                // git sha-1 9960e93b4bf52af6d1fa25ee1fa1bf4566eb7155
                {"video/webm", "webm"},
                {"audio/mpeg", "mp3"},
                {"application/wasm", "wasm"},
                {"application/x-chrome-extension", "crx"},
                {"application/xhtml+xml", "xhtml,xht,xhtm"},
                {"audio/flac", "flac"},
                {"audio/mp3", "mp3"},
                {"audio/ogg", "ogg,oga,opus"},
                {"audio/wav", "wav"},
                {"audio/webm", "webm"},
                {"audio/x-m4a", "m4a"},
                {"image/avif", "avif"},
                {"image/gif", "gif"},
                {"image/jpeg", "jpeg,jpg"},
                {"image/png", "png"},
                {"image/apng", "png,apng"},
                {"image/svg+xml", "svg,svgz"},
                {"image/webp", "webp"},
                {"multipart/related", "mht,mhtml"},
                {"text/css", "css"},
                {"text/html", "html,htm,shtml,shtm"},
                {"text/javascript", "js,mjs"},
                {"text/xml", "xml"},
                {"video/mp4", "mp4,m4v"},
                {"video/ogg", "ogv,ogm"},
                {"text/csv", "csv"},

                {"image/x-icon", "ico"},
                {"application/epub+zip", "epub"},
                {"application/font-woff", "woff"},
                {"application/gzip", "gz,tgz"},
                {"application/javascript", "js"},
                {"application/json", "json"},
                {"application/msword", "doc,dot"},
                //{"application/octet-stream", "bin,exe,com"}, // // already fallback
                {"application/pdf", "pdf"},
                {"application/pkcs7-mime", "p7m,p7c,p7z"},
                {"application/pkcs7-signature", "p7s"},
                {"application/postscript", "ps,eps,ai"},
                {"application/rdf+xml", "rdf"},
                {"application/rss+xml", "rss"},
                {"application/rtf", "rtf"},
                {"application/vnd.android.package-archive", "apk"},
                {"application/vnd.mozilla.xul+xml", "xul"},
                {"application/vnd.ms-excel", "xls"},
                {"application/vnd.ms-powerpoint", "ppt"},
                {"application/vnd.openxmlformats-officedocument.presentationml.presentation", "pptx"},
                {"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "xlsx"},
                {"application/vnd.openxmlformats-officedocument.wordprocessingml.document", "docx"},
                {"application/x-gzip", "gz,tgz"},
                {"application/x-mpegurl", "m3u8"},
                {"application/x-shockwave-flash", "swf,swl"},
                {"application/x-tar", "tar"},
                {"application/x-x509-ca-cert", "cer,crt"},
                {"application/zip", "zip"},
                //{"audio/webm", "weba"}, //repeated
                {"image/bmp", "bmp"},
                //{"image/jpeg", "jfif,pjpeg,pjp"}, //repeated
                {"image/tiff", "tiff,tif"},
                {"image/vnd.microsoft.icon", "ico"},
                {"image/x-png", "png"},
                {"image/x-xbitmap", "xbm"},
                {"message/rfc822", "eml"},
                {"text/calendar", "ics"},
                //{"text/html", "ehtml"}, //repeated
                {"text/plain", "txt,text"},
                {"text/x-sh", "sh"},
                //{"text/xml", "xsl,xbl,xslt"}, //repeated
                {"video/mpeg", "mpeg,mpg"},

                // Following is from https://github.com/samuelneff/MimeTypeMap/blob/master/MimeTypeMap.cs and filtered out duplicates
                // git sha-1 264181894cb43ce468f2413478eb25c1ed381235
                {"application/fsharp-script", "fsx"},
                {"application/msaccess", "adp"},
                {"application/octet-stream", "bin"},
                {"application/onenote", "one"},
                {"application/step", "step"},
                {"application/vnd.apple.keynote", "key"},
                {"application/vnd.apple.numbers", "numbers"},
                {"application/vnd.apple.pages", "pages"},
                {"application/vnd.ms-works", "wks"},
                {"application/vnd.visio", "vsd"},
                {"application/x-director", "dir"},
                {"application/x-msdos-program", "exe"},
                {"application/x-zip-compressed", "zip"},
                {"application/x-iwork-keynote-sffkey", "key"},
                {"application/x-iwork-numbers-sffnumbers", "numbers"},
                {"application/x-iwork-pages-sffpages", "pages"},
                {"application/xml", "xml"},
                {"audio/aac", "acc"},
                {"audio/aiff", "aiff"},
                {"audio/basic", "snd"},
                {"audio/mid", "midi"},
                {"audio/mp4", "m4a"},
                {"audio/ogg;codecs=opus", "opus"},
                {"audio/x-mpegurl", "m3u"},
                {"audio/x-pn-realaudio", "ra"},
                {"audio/x-smd", "smd"},
                {"image/heic", "heic"},
                {"image/heif", "heif"},
                {"image/pict", "pic"},
                {"image/x-macpaint", "mac"},
                {"image/x-quicktime", "qti"},
                {"text/scriptlet", "wsc"},
                {"video/3gpp", "3gp"},
                {"video/3gpp2", "3gp2"},
                {"video/quicktime", "mov"},
                {"video/vnd.dlna.mpeg-tts", "m2t"},
                {"video/x-dv", "dv"},
                {"video/x-la-asf", "lsf"},
                {"video/x-ms-asf", "asf"},
                {"x-world/x-vrml", "xof"},
            };
            var cancellations = new ConcurrentDictionary<string, CancellationTokenSource>();

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
                    try
                    {
                        using (var provider = new PhysicalFileProvider(x.PhysicalPath))
                        using (var reader = new StreamReader(provider.GetFileInfo(x.Name + ".json").CreateReadStream()))
                        {
                            var content = reader.ReadToEnd();
                            var item = JsonConvert.DeserializeObject<UrlDatum>(content);
                            if (item.DownloadedSize == 0)
                            {
                                var info = provider.GetFileInfo(item.Filename);
                                if (info.Exists)
                                    item.DownloadedSize = info.Length;
                            }
                            return item;
                        }
                    }
                    catch
                    {
                        return null;
                    }
                })
                .Where(x => x != null)
                .OrderByDescending(x => x.DateTime);
                return selector;
            });
            app.MapPost("/Api", (UrlDatum item) =>
            {
                if (!Uri.TryCreate(item.Url, UriKind.Absolute, out _))
                    return Results.BadRequest("Url is illegal.");

                item.DateTime = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                item.Filename = "";
                item.Size = -1;
                var folderPath = Path.Combine(builder.Environment.ContentRootPath, "wwwroot", item.DateTime);
                Directory.CreateDirectory(folderPath);

                var cancellationTokenSource = new CancellationTokenSource();
                cancellations.TryAdd(item.DateTime, cancellationTokenSource);

                var content = JsonConvert.SerializeObject(item);
                var jsonPath = Path.Combine(folderPath, item.DateTime + ".json");
                File.WriteAllTextAsync(jsonPath, content);

                _ = Task.Factory.StartNew(async () =>
                {
                    try
                    {
                        using (var client = new HttpClient())
                        {
                            if (!string.IsNullOrEmpty(item.Referrer))
                                client.DefaultRequestHeaders.Referrer = new Uri(item.Referrer);
                            if (!string.IsNullOrEmpty(item.UserAgent))
                                client.DefaultRequestHeaders.UserAgent.ParseAdd(item.UserAgent);

                            using (var response = await client.GetAsync(item.Url, HttpCompletionOption.ResponseHeadersRead))
                            {
                                response.EnsureSuccessStatusCode();

                                item.Filename = response.Content.Headers.ContentDisposition?.FileName?.Trim('"') ?? "";
                                if (string.IsNullOrEmpty(item.Filename))
                                {
                                    item.Filename = Path.GetFileName(new Uri(item.Url).LocalPath).Trim();
                                    if (string.IsNullOrEmpty(item.Filename))
                                    {
                                        item.Filename = "default";
                                    }
                                    var ext = Path.GetExtension(item.Filename);
                                    var mediaType = response.Content.Headers.ContentType?.MediaType;
                                    if (mediaType != "application/octet-stream" || string.IsNullOrEmpty(ext))
                                    {
                                        if (mediaType != null && mimes.TryGetValue(mediaType, out ext))
                                            ext = "." + ext.Split(',')[0];
                                        else
                                            ext = ".bin";
                                        if (!item.Filename.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                                            item.Filename += ext;
                                    }
                                }

                                item.Size = response.Content.Headers.ContentLength ?? -1;
                                await File.WriteAllTextAsync(jsonPath, JsonConvert.SerializeObject(item));

                                var filePath = Path.Combine(folderPath, item.Filename);
                                using (var fileStream = File.Create(filePath))
                                {
                                    await response.Content.CopyToAsync(fileStream, cancellationTokenSource.Token);
                                }

                                item.Size = item.DownloadedSize = new FileInfo(filePath).Length;
                                await File.WriteAllTextAsync(jsonPath, JsonConvert.SerializeObject(item));
                            }
                        }
                    }
                    catch
                    {
                        item.DownloadedSize = -1;
                        await File.WriteAllTextAsync(jsonPath, JsonConvert.SerializeObject(item));
                    }
                    finally
                    {
                        cancellations.TryRemove(item.DateTime, out _);
                    }
                }).ConfigureAwait(false);

                return Results.Ok();
            });
            app.MapDelete("/Api", async (string datetime) =>
            {
                try
                {
                    if (cancellations.TryGetValue(datetime, out var cancellationTokenSource))
                        await cancellationTokenSource.CancelAsync();
                    while (cancellations.ContainsKey(datetime))
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
    }

    public class UrlDatum
    {
        public string DateTime { get; set; }
        public string Url { get; set; }
        public string Referrer { get; set; }
        public string UserAgent { get; set; }
        public string Filename { get; set; }
        public long Size { get; set; } // -1 means unknow size.
        public long DownloadedSize { get; set; } // -1 means with error.
    }
}
