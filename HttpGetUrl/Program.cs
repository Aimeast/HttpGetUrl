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
            .AddSingleton<StorageService>()
            .AddSingleton<TaskStorageCache>()
            .AddSingleton<TaskService>()
            .AddSingleton<PwService>()
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
        app.UseFileServer(new FileServerOptions
        {
            StaticFileOptions =
            {
                ServeUnknownFileTypes = true,
                DefaultContentType = "application/octet-stream"
            }
        });
        if (!builder.Environment.IsDevelopment())
        {
            app.UseResponseCompression();
            app.UseHttpsRedirection()
                .UseHsts();
        }

        app.MapGet("/task", ([FromServices] HgetApp hget) => hget.GetTaskItems());
        app.MapPost("/task", (TaskFile task, [FromServices] HgetApp hget) => hget.CreateTask(task));
        app.MapDelete("/task", (string taskId, [FromServices] HgetApp hget) => hget.DeleteTask(taskId));
        app.MapGet("/tokens", ([FromServices] HgetApp hget) => hget.GetTokens());
        app.MapPost("/tokens", async (Token[] tokens, [FromServices] HgetApp hget) => await hget.UpdateTokensAsync(tokens));
        app.MapGet("/info", async (string q, [FromServices] HgetApp hget) => await hget.GetSystemInfoAsync(q));

        app.Run();
    }
}
