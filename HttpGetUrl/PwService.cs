using Microsoft.Playwright;

namespace HttpGetUrl;

public class PwService(IConfiguration configuration, StorageService storageService)
{
    private readonly string _proxy = configuration.GetValue<string>("Hget:Proxy");
    private readonly string _userDataDir = storageService.GetUserDataDir();

    private IPlaywright _playwright;
    private IBrowserContext _browserContext;
    private Lazy<Task<IBrowserContext>> _browserContextLazy;

    public async Task InitializeAsync()
    {
        _browserContextLazy = new(async () =>
        {
            var contextOptions = new BrowserTypeLaunchPersistentContextOptions { Headless = true };
            if (!string.IsNullOrEmpty(_proxy))
                contextOptions.Proxy = new Proxy { Server = _proxy };
            _playwright = await Playwright.CreateAsync();
            return await _playwright.Firefox.LaunchPersistentContextAsync(Path.Combine(_userDataDir, "firefox"), contextOptions);
        });

        await Task.CompletedTask;
    }

    private async Task InternalInit()
    {
        _browserContext ??= await _browserContextLazy.Value;
    }

    public async Task CloseAsync()
    {
        if (_browserContext != null)
        {
            await _browserContext.CloseAsync();
            _playwright.Dispose();
        }
    }

    public async Task<IPage> NewPageAsync()
    {
        await InternalInit();
        return await _browserContext.NewPageAsync();
    }

    public async Task AddCookieAsync(Cookie cookie)
    {
        if (_browserContext == null)
            return;

        await _browserContext.AddCookiesAsync([cookie]);
    }

    public async Task UpdateTokesAsync(Token[] tokens)
    {
        if (_browserContext == null)
            return;

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

    public async ValueTask<string> GetBrowserVersionAsync()
    {
        await InternalInit();
        var browser = await _playwright.Firefox.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var browserVersion = $"{browser.BrowserType.Name} {browser.Version}";
        await browser.CloseAsync();
        return browserVersion;
    }
}
