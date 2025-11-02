using HttpGetUrl.Models;
using System.Buffers.Text;
using System.Security.Cryptography;

namespace HttpGetUrl;

public class UserSpaceMiddleware(RequestDelegate next)
{
    private const string CookieName = "user_space";

    public async Task InvokeAsync(HttpContext context, UserSpaceFile userSpace, StorageService storageService)
    {
        var expecting = context.Request.Method == "GET"
            && context.Request.Path.Equals("/info", StringComparison.OrdinalIgnoreCase)
            && context.Request.Query.ContainsKey("user-space");

        if (!expecting && context.Request.Cookies.TryGetValue(CookieName, out var existingUserSpace))
        {
            userSpace.Decode(existingUserSpace);
        }
        else if (expecting)
        {
            var expectUserSpace = context.Request.Query["user-space"].First();
            userSpace.Decode(expectUserSpace);
        }
        if (string.IsNullOrEmpty(userSpace.Space))
        {
            var bytes = new byte[UserSpaceFile.UserSpaceLength];
            RandomNumberGenerator.Fill(bytes);
            userSpace.Space = Base64Url.EncodeToString(bytes);
        }
        else
        {
            userSpace = storageService.GetUserSpace(userSpace.Space);
        }

        if (userSpace.Expires < DateTimeOffset.UtcNow.AddDays(30))
        {
            userSpace.Expires = DateTimeOffset.UtcNow.AddDays(31);
            SetCookie(context, userSpace);
            storageService.SaveUserSpace(userSpace);
        }

        await next(context);
    }

    private void SetCookie(HttpContext context, UserSpaceFile userSpace)
    {
        var cookieOptions = new CookieOptions
        {
            Expires = userSpace.Expires.AddDays(30),
            HttpOnly = false,
            Secure = context.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            IsEssential = true,
        };

        context.Response.Cookies.Append(CookieName, userSpace.Encode(), cookieOptions);
    }
}
