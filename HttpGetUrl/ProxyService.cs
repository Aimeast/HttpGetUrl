using HttpGetUrl.Route;
using System.Net;

namespace HttpGetUrl;

public class ProxyService
{
    private readonly CidrGroup _bypassGroup;

    public ProxyService(IConfiguration configuration, StorageService storageService)
    {
        Proxy = configuration.GetValue<string>("Hget:Proxy");
        var bypassList = configuration.GetSection("Hget:ByPassList").Get<string[]>();
        if (Proxy != null && bypassList?.Length > 0)
        {
            var builder = new CidrGroupBuilder();
            foreach (var file in bypassList)
            {
                var path = Path.Combine(storageService.GetUserDataDir(), file);
                var cidrs = File.ReadAllLines(path)
                    .Where(x => !string.IsNullOrWhiteSpace(x) && !x.StartsWith('#'))
                    .Select(Cidr.Parse);
                builder.AddCidrs(cidrs);
            }
            _bypassGroup = builder.Build();
        }
    }

    public string Proxy { get; private set; }

    public bool TestUseProxy(string domain)
    {
        if (Proxy == null)
            return false;
        if (_bypassGroup == null)
            return true;

        foreach (var ip in Dns.GetHostAddresses(domain))
        {
            if (_bypassGroup.Match(ip))
                return false;
        }
        return true;
    }
}
