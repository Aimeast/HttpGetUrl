using HttpGetUrl.Downloaders;
using System.Reflection;

namespace HttpGetUrl;

public class DownloaderFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly List<DownloaderInfo> _downloaders = [];

    public DownloaderFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;

        var types = AppDomain.CurrentDomain.GetAssemblies()
                            .SelectMany(a => a.GetTypes())
                            .Where(x => x.GetCustomAttribute<DownloaderAttribute>() != null);
        foreach (var t in types)
        {
            var attr = t.GetCustomAttribute<DownloaderAttribute>();
            _downloaders.Add(new DownloaderInfo { Identity = attr.Identity, SupportedDomains = attr.SupportedDomains, ServiceType = t });
        }
    }

    public ContentDownloader CreateDownloader(TaskFile task, CancellationTokenSource cancellationTokenSource = null)
    {
        cancellationTokenSource ??= new CancellationTokenSource();
        var info = _downloaders.FirstOrDefault(x => task.DownloaderType == null
                        ? x.SupportedDomains.Any(y => Utility.IsSubdomain(y, task.Url.Host))
                        : x.ServiceType.FullName == task.DownloaderType);
        if (info != null)
        {
            try
            {
                task.IsHide = true;
                task.IsVirtual = true;
                return (ContentDownloader)ActivatorUtilities.CreateInstance(_serviceProvider, info.ServiceType,
                    task, cancellationTokenSource);
            }
            catch (Exception ex)
            {
                task.Status = TaskStatus.Error;
                task.ErrorMessage = ex.ToString();
            }
        }

        return ActivatorUtilities.CreateInstance<YtdlpDownloader>(_serviceProvider, task, cancellationTokenSource);
    }

    public HttpDownloader CreateHttpDownloader(TaskFile task, CancellationTokenSource cancellationTokenSource = null)
    {
        cancellationTokenSource ??= new CancellationTokenSource();
        return ActivatorUtilities.CreateInstance<HttpDownloader>(_serviceProvider, task, cancellationTokenSource);
    }

    private class DownloaderInfo
    {
        public string Identity { get; set; }
        public string[] SupportedDomains { get; set; }
        public Type ServiceType { get; set; }
    }
}
