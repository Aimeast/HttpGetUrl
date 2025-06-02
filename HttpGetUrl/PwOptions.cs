using HttpGetUrl.Models;

namespace HttpGetUrl;

public class PwOptions
{
    public string UserDataDir { get; set; }
    public string Proxy { get; set; }
    public Token[] Tokens { get; set; }
}
