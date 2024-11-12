using Microsoft.Playwright;

namespace HttpGetUrl;

public class PwService(IConfiguration configuration, StorageService storageService)
{
    private readonly string _proxy = configuration.GetValue<string>("Hget:Proxy");
    private readonly Token[] _tokens = storageService.GetTokens();
    private readonly string _userDataDir = storageService.GetUserDataDir();

    private IPlaywright _playwright;
    private IBrowserContext _browserContext;

    public async Task InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        var contextOptions = new BrowserTypeLaunchPersistentContextOptions { Headless = true };
        if (!string.IsNullOrEmpty(_proxy))
            contextOptions.Proxy = new Proxy { Server = _proxy };
        _browserContext = await _playwright.Chromium
            .LaunchPersistentContextAsync(_userDataDir, contextOptions);
        await UpdateTokesAsync(_tokens);
    }

    public async Task CloseAsync()
    {
        await _browserContext.CloseAsync();
        _playwright.Dispose();
    }

    public Task<IPage> NewPageAsync()
    {
        return _browserContext.NewPageAsync();
    }

    public Task AddCookieAsync(Cookie cookie)
    {
        return _browserContext.AddCookiesAsync([cookie]);
    }

    public async Task UpdateTokesAsync(Token[] tokens)
    {
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

    public async Task<string> GetBrowserVersionAsync()
    {
        var browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var browserVersion = browser.Version;
        await browser.CloseAsync();
        return browserVersion;
    }
}
