using Microsoft.Extensions.FileProviders;
using System.Reflection;

namespace HttpGetUrl;

public class DownloaderService
{
    private readonly List<DownloaderInfo> downloaders = [];

    public DownloaderService Register<T>() where T : ContentDownloader
    {
        var t = typeof(T);
        var attr = t.GetCustomAttribute<DownloaderAttribute>();
        ArgumentNullException.ThrowIfNull(attr, nameof(attr));
        downloaders.Add(new DownloaderInfo { Identity = attr.Identity, SupportedDomains = attr.SupportedDomains, Type = t });
        return this;
    }

    public DownloaderService RegisterAll()
    {
        var types = Assembly.GetCallingAssembly().GetTypes().Where(x => x.GetCustomAttribute<DownloaderAttribute>() != null);
        foreach (var t in types)
        {
            var attr = t.GetCustomAttribute<DownloaderAttribute>();
            downloaders.Add(new DownloaderInfo { Identity = attr.Identity, SupportedDomains = attr.SupportedDomains, Type = t });
        }
        return this;
    }

    public ContentDownloader CreateDownloader(Uri uri, Uri referrer, IFileProvider workingFolder)
    {
        var cancellationTokenSource = new CancellationTokenSource();
        var info = downloaders.FirstOrDefault(x => x.SupportedDomains.Any(y => Utility.IsSubdomain(y, uri.Host)));
        if (info != null)
        {
            return (ContentDownloader)Activator.CreateInstance(info.Type, [uri, referrer, workingFolder, cancellationTokenSource]);
        }

        return new HttpDownloader(uri, referrer, workingFolder, cancellationTokenSource);
    }

    private class DownloaderInfo
    {
        public string Identity { get; set; }
        public string[] SupportedDomains { get; set; }
        public Type Type { get; set; }
    }
}
