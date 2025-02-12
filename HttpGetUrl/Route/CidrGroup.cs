using System.Net;

namespace HttpGetUrl.Route;

public class CidrGroup(Cidr[] cidrs)
{
    private readonly Cidr[] cidrs = cidrs;

    public bool Match(IPAddress ip)
    {
        var index = Array.BinarySearch(cidrs, ip);
        return index >= 0 || ~index > 0 && cidrs[~index - 1].ExistsIntersection(ip);
    }
}
