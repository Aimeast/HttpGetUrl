using Microsoft.Playwright;
using System.Net;
using Cookie = Microsoft.Playwright.Cookie;

namespace HttpGetUrl;

public class PwService(IConfiguration configuration, StorageService storageService, ProxyService proxyService)
{
    private static readonly SemaphoreSlim _semaphoreSlim = new(1, 1);
    private readonly string _userDataDir = storageService.GetUserDataDir();
    private readonly HttpClientHandler _httpClientHandler = new()
    {
        Proxy = new WebProxy(configuration.GetValue<string>("Hget:Proxy")),
        AutomaticDecompression = DecompressionMethods.All
    };

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
                    _playwright = await Playwright.CreateAsync();
                    _browserContext = await _playwright.Chromium.LaunchPersistentContextAsync(Path.Combine(_userDataDir, "Chromium"), contextOptions);
                    await _browserContext.RouteAsync(x => proxyService.TestUseProxy(new Uri(x).Host), RouteHandler);

                    _ = Task.Run(async () =>
                    {
                        var idle = Math.Max(configuration.GetValue<int>("Hget:PwServiceAlive", 10), 3);
                        while (DateTime.Now - _lastAccess < TimeSpan.FromMinutes(idle))
                        {
                            Thread.Sleep(60_000);
                        }
                        await InternalClose();
                    });
                }
            }
            finally
            {
                _semaphoreSlim.Release();
            }
        }
    }

    private async Task RouteHandler(IRoute route)
    {
        var request = route.Request;
        var url = request.Url;

        var client = new HttpClient(_httpClientHandler);
        using var forwardRequest = new HttpRequestMessage(new HttpMethod(request.Method), url);

        foreach (var header in request.Headers)
        {
            forwardRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.PostDataBuffer != null && (request.Method == "POST" || request.Method == "PUT" || request.Method == "PATCH"))
        {
            forwardRequest.Content = new ByteArrayContent(request.PostDataBuffer);
        }

        try
        {
            using var response = await client.SendAsync(forwardRequest);
            var bodyBytes = await response.Content.ReadAsByteArrayAsync();

            await route.FulfillAsync(new()
            {
                Status = (int)response.StatusCode,
                ContentType = response.Content.Headers.ContentType?.ToString(),
                Headers = response.Headers.ToDictionary(h => h.Key, h => h.Value.First()),
                BodyBytes = bodyBytes,
            });
        }
        catch (Exception ex)
        {
            await route.AbortAsync();
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
