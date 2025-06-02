using HttpGetUrl.Models;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.FileProviders.Physical;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HttpGetUrl;

public class StorageService(IHostEnvironment hostEnvironment)
{
    private readonly IHostEnvironment _hostEnvironment = hostEnvironment;

    private Token[] _tokens = null; // for cache

    public string GetContentRootPath()
    {
        return _hostEnvironment.ContentRootPath;
    }

    public IEnumerable<string> GetAllUserSpace()
    {
        return _hostEnvironment
            .ContentRootFileProvider
            .GetDirectoryContents("wwwroot")
            .Where(x => x.IsDirectory && Regex.IsMatch(x.Name, @"^hg-[A-Za-z\d\-_]{8}$"))
            .Select(x => x.Name[3..])
            .OrderByDescending(x => x);
    }

    public UserSpaceFile GetUserSpace(string userSpace)
    {
        var jsonPath = Path.Combine(_hostEnvironment.ContentRootPath, "wwwroot", $"hg-{userSpace}", $".json");
        var content = "";
        using (StringLock.LockString($"storage-{userSpace}"))
            try
            {
                content = File.ReadAllText(jsonPath);
            }
            catch (Exception ex)
            {
                return null;
            }
        var item = JsonSerializer.Deserialize<UserSpaceFile>(content);
        return item;
    }

    public void DeleteUserSpace(string userSpace)
    {
        var folderPath = Path.Combine(_hostEnvironment.ContentRootPath, "wwwroot", $"hg-{userSpace}");
        Directory.Delete(folderPath, true);
    }

    public IEnumerable<string> GetAllTaskId(string userSpace)
    {
        return _hostEnvironment
            .ContentRootFileProvider
            .GetDirectoryContents("wwwroot/hg-" + userSpace)
            .Where(x => x.IsDirectory && Regex.IsMatch(x.Name, @"^\d{6}-\d{6}$"))
            .Select(x => x.Name)
            .OrderByDescending(x => x);
    }

    public void SaveUserSpace(UserSpaceFile userSpace)
    {
        var dir = Path.Combine(_hostEnvironment.ContentRootPath, "wwwroot", $"hg-{userSpace.Space}");
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        var jsonPath = Path.Combine(dir, $".json");
        var content = JsonSerializer.Serialize(userSpace);
        using (StringLock.LockString($"storage-{userSpace}"))
            File.WriteAllText(jsonPath, content);
    }

    public TaskFile[] GetTaskItems(string userSpace, string taskId)
    {
        var jsonPath = Path.Combine(_hostEnvironment.ContentRootPath, "wwwroot", $"hg-{userSpace}", taskId, $".{taskId}.json");
        var content = "";
        using (StringLock.LockString($"storage-{userSpace}.{taskId}"))
            try
            {
                content = File.ReadAllText(jsonPath);
            }
            catch (Exception ex)
            {
                return null;
            }
        var item = JsonSerializer.Deserialize<TaskFile[]>(content);
        return item;
    }

    public void SaveTasks(string userSpace, TaskFile[] tasks)
    {
        var taskId = tasks[0].TaskId;
        var jsonPath = Path.Combine(_hostEnvironment.ContentRootPath, "wwwroot", $"hg-{userSpace}", taskId, $".{taskId}.json");
        var content = JsonSerializer.Serialize(tasks);
        using (StringLock.LockString($"storage-{userSpace}.{taskId}"))
            File.WriteAllText(jsonPath, content);
    }

    public void DeleteTask(string userSpace, string taskId)
    {
        var folderPath = Path.Combine(_hostEnvironment.ContentRootPath, "wwwroot", $"hg-{userSpace}", taskId);
        Directory.Delete(folderPath, true);
    }

    public void PrepareDirectory(string userSpace, string taskId)
    {
        var folderPath = Path.Combine(_hostEnvironment.ContentRootPath, "wwwroot", $"hg-{userSpace}", taskId);
        Directory.CreateDirectory(folderPath);
    }

    public FileStream OpenFileStream(string userSpace, string taskId, string filename, long? position)
    {
        var filePath = GetFilePath(userSpace, taskId, filename);
        var stream = File.OpenWrite(filePath);
        stream.Position = position ?? 0;
        stream.SetLength(stream.Position);
        return stream;
    }

    public long? GetFileLength(string userSpace, string taskId, string filename)
    {
        var filePath = GetFilePath(userSpace, taskId, filename);
        var info = new FileInfo(filePath);
        if (info.Exists)
            return info.Length;
        else
            return null;
    }

    public string GetFilePath(string userSpace, string taskId, string filename)
    {
        var filePath = Path.GetFullPath(Path.Combine(_hostEnvironment.ContentRootPath, "wwwroot", $"hg-{userSpace}", taskId, filename));
        return filePath;
    }

    public Token[] GetTokens()
    {
        if (_tokens == null)
        {
            var dataProvider = new PhysicalFileProvider(Path.Combine(_hostEnvironment.ContentRootPath, ".hg"), ExclusionFilters.None);
            var tokenFileInfo = dataProvider.GetFileInfo("tokens.json");
            if (tokenFileInfo.Exists)
            {
                using var reader = new StreamReader(tokenFileInfo.CreateReadStream());
                var content = reader.ReadToEnd();
                _tokens = JsonSerializer.Deserialize<Token[]>(content);
            }
        }
        return _tokens ?? [];
    }

    public void SaveTokens(Token[] tokens)
    {
        _tokens = tokens;
        var dataProvider = new PhysicalFileProvider(Path.Combine(_hostEnvironment.ContentRootPath, ".hg"), ExclusionFilters.None);
        var jsonPath = dataProvider.GetFileInfo("tokens.json").PhysicalPath;
        var jsonContent = JsonSerializer.Serialize(tokens);
        File.WriteAllText(jsonPath, jsonContent);

        var txtPath = dataProvider.GetFileInfo("tokens.txt").PhysicalPath;
        var txtContent = ConventToNetscapeCookie(tokens);
        File.WriteAllText(txtPath, txtContent);
    }

    private string ConventToNetscapeCookie(Token[] tokens)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Netscape HTTP Cookie File");

        foreach (var token in tokens)
        {
            string domain = token.Domain.StartsWith('.') ? token.Domain : "." + token.Domain;
            string includeSubdomains = "TRUE";
            string path = token.Path;
            string secure = "FALSE";
            long expires = new DateTimeOffset(token.Expires).ToUnixTimeSeconds();
            string name = token.Name;
            string value = token.Value;

            sb.AppendLine($"{domain}\t{includeSubdomains}\t{path}\t{secure}\t{expires}\t{name}\t{value}");
        }

        return sb.ToString();
    }

    public string GetUserDataDir()
    {
        return Path.Combine(_hostEnvironment.ContentRootPath, ".hg");
    }
}
