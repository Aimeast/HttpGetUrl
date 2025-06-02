using HttpGetUrl.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.ResponseCompression;

namespace HttpGetUrl;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services
            .AddSingleton<HgetApp>()
            .AddSingleton<DownloaderFactory>()
            .AddSingleton<ProxyService>()
            .AddSingleton<StorageService>()
            .AddSingleton<TaskStorageCache>()
            .AddSingleton<TaskService>()
            .AddSingleton<PwService>()
            .AddScoped<UserSpaceFile>()
            .AddHostedService<CleanUserSpaceHostedService>()
            .AddHostedService<PwServiceHostedService>();
        if (!builder.Environment.IsDevelopment())
        {
            builder.Services
                .AddResponseCompression(options =>
                {
                    options.Providers.Add<BrotliCompressionProvider>();
                    options.Providers.Add<GzipCompressionProvider>();
                    options.EnableForHttps = true;
                });
        }

        var app = builder.Build();

        app.UseDefaultFiles();
        app.UseStaticFiles(new StaticFileOptions
        {
            ServeUnknownFileTypes = true,
            DefaultContentType = "application/octet-stream",
        });
        if (!builder.Environment.IsDevelopment())
        {
            app.UseResponseCompression();
            app.UseHttpsRedirection()
                .UseHsts();
        }
        app.UseMiddleware<UserSpaceMiddleware>();

        app.MapGet("/task",
            ([FromServices] HgetApp hget,
            [FromServices] UserSpaceFile userSpace) => hget.GetTaskItems(userSpace.Space));
        app.MapPost("/task",
            ([FromServices] HgetApp hget,
            [FromServices] UserSpaceFile userSpace,
            TaskFile task) => hget.CreateTask(userSpace.Space, task));
        app.MapDelete("/task",
            ([FromServices] HgetApp hget,
            [FromServices] UserSpaceFile userSpace,
            string taskId) => hget.DeleteTask(userSpace.Space, taskId));
        app.MapPatch("/task",
            ([FromServices] HgetApp hget,
            [FromServices] UserSpaceFile userSpace,
            string taskId) => hget.ResumeTask(userSpace.Space, taskId));
        app.MapGet("/tokens",
            ([FromServices] HgetApp hget) => hget.GetTokens());
        app.MapPost("/tokens", async
            ([FromServices] HgetApp hget, Token[] tokens) => await hget.UpdateTokensAsync(tokens));
        app.MapGet("/info", async
            ([FromServices] HgetApp hget, HttpContext context) => await hget.GetSystemInfoAsync(context));
        app.MapGet("/upytdlp", async
            ([FromServices] HgetApp hget) => await hget.UpgradeYtdlp());

        app.Run();
    }
}
