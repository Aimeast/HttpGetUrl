namespace HttpGetUrl;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var app = new HgetAppBuilder(builder)
            .ConfigureApplication()
            .RegisterDownloaderService()
            .RegisterEndpoints()
            .Build();

        app.Run();
        PwService.Close();
    }
}
