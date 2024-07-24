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
}
