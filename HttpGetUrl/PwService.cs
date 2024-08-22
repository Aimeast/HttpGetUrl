using Microsoft.Playwright;

namespace HttpGetUrl;

public class PwService
{
    private static PwService instance;

    private IPlaywright playwright;
    private IBrowserContext browserContext;

    private PwService() { }

    public static void InitService(PwOptions pwOptions)
    {
        var f = false;
        lock (typeof(PwService))
        {
            if (instance == null)
            {
                f = true;
                instance = new PwService();
            }
        }
        if (f)
            _ = Task.Factory.StartNew(async () =>
            {
                instance.playwright = await Playwright.CreateAsync();
                var contextOptions = new BrowserTypeLaunchPersistentContextOptions { Headless = true };
                if (!string.IsNullOrEmpty(pwOptions.Proxy))
                    contextOptions.Proxy = new Proxy { Server = pwOptions.Proxy };
                instance.browserContext = await instance.playwright.Chromium
                    .LaunchPersistentContextAsync(pwOptions.UserDataDir, contextOptions)
                    .ConfigureAwait(false);
                await instance.UpdateTokesAsync(ContentDownloader.PwOptions.Tokens);
            });
    }

    public static PwService GetInstance()
    {
        ArgumentNullException.ThrowIfNull(instance, nameof(instance));
        return instance;
    }

    public static void Close()
    {
        instance?.playwright.Dispose();
        instance?.browserContext.CloseAsync().Wait();
        instance = null;
    }

    public Task<IPage> NewPageAsync()
    {
        return browserContext.NewPageAsync();
    }

    public Task AddCookieAsync(Cookie cookie)
    {
        return browserContext.AddCookiesAsync([cookie]);
    }

    public async Task UpdateTokesAsync(Token[] tokens)
    {
        var cookies = new List<Cookie>();
        var attrs = Assembly.GetCallingAssembly().GetTypes()
            .Select(x => x.GetCustomAttribute<DownloaderAttribute>())
            .Where(x => x != null);
        foreach (var attr in attrs)
        {
            var token = tokens.FirstOrDefault(x => x.Identity == attr.Identity);
            if (token != null)
            {
                foreach (var domain in attr.SupportedDomains)
                {
                    cookies.Add(new Cookie
                    {
                        Name = token.Key,
                        Value = token.Value,
                        Domain = domain,
                        Expires = DateTimeOffset.UtcNow.AddYears(1).ToUnixTimeSeconds(),
                        Path = "/",
                        HttpOnly = true,
                        Secure = true,
                        SameSite = SameSiteAttribute.None,
                    });
                }
            }
        }
        await browserContext.AddCookiesAsync(cookies);
    }
}
