using Microsoft.Extensions.FileProviders;
using System.Net;

namespace HttpGetUrl;

public class HttpDownloader(Uri uri, Uri referrer, IFileProvider workingFolder, CancellationTokenSource cancellationTokenSource)
    : ContentDownloader(uri, referrer, workingFolder, cancellationTokenSource)
{
    private readonly Dictionary<string, string> mimes = new(StringComparer.OrdinalIgnoreCase)
    {
        // Following is from https://chromium.googlesource.com/chromium/src/net/+/refs/heads/main/base/mime_util.cc
        // git sha-1 9960e93b4bf52af6d1fa25ee1fa1bf4566eb7155
        // kPrimaryMappings
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

        // kSecondaryMappings
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
    private HttpClient httpClient = null;
    private HttpResponseMessage httpResponseMessage = null;

    public override async Task Analysis()
    {
        if (httpClient != null || httpResponseMessage != null)
            throw new InvalidOperationException("Analysis has been called.");

        var handler = new HttpClientHandler();
        if (PwOptions.Proxy != null)
            handler.Proxy = new WebProxy(PwOptions.Proxy);
        httpClient = new HttpClient(handler);
        if (referrer != null)
            httpClient.DefaultRequestHeaders.Referrer = referrer;

        httpResponseMessage = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
        httpResponseMessage.EnsureSuccessStatusCode();

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
                if (mediaType != null && mimes.TryGetValue(mediaType, out ext))
                    ext = "." + ext.Split(',')[0];
                else
                    ext = ".bin";
                if (!filename.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                    filename += ext;
            }
        }
        if (string.IsNullOrEmpty(FinalFileName))
            FinalFileName = filename;
        EstimatedContentLength = httpResponseMessage.Content.Headers.ContentLength ?? -1;
    }

    public override async Task<long> Download()
    {
        var filePath = WorkingFolder.GetFileInfo(FinalFileName).PhysicalPath;
        using (var fileStream = File.Create(filePath))
        {
            await httpResponseMessage.Content.CopyToAsync(fileStream, CancellationTokenSource.Token);
            return fileStream.Length;
        }
    }

    public override Task Merge()
    {
        throw new InvalidOperationException($"Merge not supported by {nameof(HttpDownloader)}.");
    }

    public override void Dispose()
    {
        httpResponseMessage?.Dispose();
        httpClient?.Dispose();

        httpResponseMessage = null;
        httpClient = null;
    }
}
