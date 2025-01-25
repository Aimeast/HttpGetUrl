using System.Diagnostics;
using System.Text.Json;

namespace HttpGetUrl;

public static class Utility
{
    private static readonly Dictionary<string, string> _mimes = new(StringComparer.OrdinalIgnoreCase)
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
        //{"application/octet-stream", "bin,exe,com"}, // already fallback
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

    public static Dictionary<string, string> Mimes => _mimes;

    public static IEnumerable<JsonElement> SearchJson(this JsonElement jsonElement, string searchText)
    {
        if (jsonElement.ValueKind == JsonValueKind.Array)
            foreach (var element in jsonElement.EnumerateArray())
                foreach (var yield in SearchJson(element, searchText))
                    yield return yield;
        else if (jsonElement.ValueKind == JsonValueKind.Object)
            foreach (var element in jsonElement.EnumerateObject())
                if (element.Name == searchText)
                    yield return element.Value;
                else
                    foreach (var yield in SearchJson(element.Value, searchText))
                        yield return yield;
    }

    public static IEnumerable<JsonElement> SearchJson(this IEnumerable<JsonElement> elements, string searchText)
    {
        foreach (var element in elements)
            foreach (var yield in SearchJson(element, searchText))
                yield return yield;
    }

    public static bool IsSubdomain(string parentDomain, string subDomain)
    {
        if (!parentDomain.StartsWith('.'))
        {
            parentDomain = "." + parentDomain;
        }
        if (!subDomain.StartsWith('.'))
        {
            subDomain = "." + subDomain;
        }
        return subDomain.EndsWith(parentDomain, StringComparison.OrdinalIgnoreCase);
    }

    public static string MakeValidFileName(string fileName, char replacement = '_')
    {
        // Define invalid characters based on Windows, Linux, and macOS.
        // DO NOT call `Path.GetInvalidFileNameChars()`,
        // Because the array returned is different on different operating systems.
        char[] invalidChars = ['\\', '/', ':', '*', '?', '"', '<', '>', '|'];
        var chars = fileName.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (invalidChars.Contains(chars[i]))
                chars[i] = replacement;
        }
        return new string(chars);
    }

    public static string TruncateString(string input, int startLength, int endLength)
    {
        if (input.Length <= startLength + endLength)
        {
            return input;
        }

        string start = input.Substring(0, startLength);
        string end = input.Substring(input.Length - endLength, endLength);
        return $"{start}........{end}";
    }

    public static async ValueTask<string> RunCmdFirstLine(string path, string args, bool wait = false)
    {
        var processStartInfo = new ProcessStartInfo
        {
            FileName = path,
            Arguments = args,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = processStartInfo };
        process.Start();
        var output = await process.StandardOutput.ReadLineAsync();
        if (wait)
            process.WaitForExit();
        else
            process.Close();

        return output.Trim();
    }

    public static string FormatSize(long size)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double val = size;
        var i = 0;
        while (val >= 1000 && i < units.Length - 1)
        {
            val /= 1024;
            i++;
        }
        return $"{Math.Round(val, 1)}{units[i]}";
    }
}
