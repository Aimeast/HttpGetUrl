using Microsoft.Playwright;

namespace HttpGetUrl;

public class PwService(IConfiguration configuration, StorageService storageService)
{
    private static readonly SemaphoreSlim _semaphoreSlim = new(1, 1);
    private readonly string _proxy = configuration.GetValue<string>("Hget:Proxy");
    private readonly string _userDataDir = storageService.GetUserDataDir();

    private IPlaywright _playwright;
    private IBrowserContext _browserContext;
    private DateTime _lastAccess = DateTime.MinValue;

    public async Task InitializeAsync()
    {
        await Task.CompletedTask;
    }

    public async Task CloseAsync()
    {
        await InternalClose();
    }

    private async Task InternalInit()
    {
        _lastAccess = DateTime.Now;
        if (_browserContext == null)
        {
            _semaphoreSlim.Wait();
            try
            {
                if (_browserContext == null)
                {
                    var contextOptions = new BrowserTypeLaunchPersistentContextOptions { Headless = true };
                    if (!string.IsNullOrEmpty(_proxy))
                        contextOptions.Proxy = new Proxy { Server = _proxy };
                    _playwright = await Playwright.CreateAsync();
                    _browserContext = await _playwright.Firefox.LaunchPersistentContextAsync(Path.Combine(_userDataDir, "firefox"), contextOptions);

                    _ = Task.Run(() =>
                    {
                        var idle = Math.Max(configuration.GetValue<int>("Hget:PwServiceAlive", 10), 3);
                        while (DateTime.Now - _lastAccess < TimeSpan.FromMinutes(idle))
                        {
                            Thread.Sleep(60_000);
                        }
                        _ = InternalClose();
                    });
                }
            }
            finally
            {
                _semaphoreSlim.Release();
            }
        }
    }

    private async Task InternalClose()
    {
        if (_browserContext != null)
        {
            _semaphoreSlim.Wait();
            try
            {
                if (_browserContext != null)
                {
                    await _browserContext.CloseAsync();
                    _playwright.Dispose();

                    _playwright = null;
                    _browserContext = null;
                }
            }
            finally
            {
                _semaphoreSlim.Release();
            }
        }
    }

    public async Task<IPage> NewPageAsync()
    {
        await InternalInit();
        return await _browserContext.NewPageAsync();
    }

    public async Task AddCookieAsync(Cookie cookie)
    {
        await InternalInit();
        await _browserContext.AddCookiesAsync([cookie]);
    }

    public async Task UpdateTokesAsync(Token[] tokens)
    {
        await InternalInit();
        var cookies = tokens.Select(x => new Cookie
        {
            Name = x.Name,
            Value = x.Value,
            Domain = x.Domain,
            Path = x.Path,
            Expires = new DateTimeOffset(x.Expires.ToUniversalTime()).ToUnixTimeSeconds(),
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteAttribute.None,
        });
        await _browserContext.AddCookiesAsync(cookies);
    }

    public async ValueTask<string> GetPlaywrightVersionAsync()
    {
        await InternalInit();
        var browser = await _playwright.Firefox.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var browserVersion = $"{typeof(Playwright).Assembly.GetName().Version} @ {browser.BrowserType.Name} {browser.Version}";
        await browser.CloseAsync();
        return browserVersion;
    }
}
