using Microsoft.Extensions.FileProviders;
using System.Text.Json;

namespace HttpGetUrl;

public class Token
{
    public string Name { get; set; }
    public string Value { get; set; }
    public string Domain { get; set; }
    public string Path { get; set; }
    public DateTime Expires { get; set; }

    public static async Task SaveTokensAsync(IFileInfo tokenFileInfo, Token[] tokens)
    {
        var jsonPath = tokenFileInfo.PhysicalPath;
        var content = JsonSerializer.Serialize(tokens);
        await File.WriteAllTextAsync(jsonPath, content);
    }

    public static Token[] GetTokens(IFileInfo tokenFileInfo)
    {
        if (tokenFileInfo.Exists)
        {
            using var reader = new StreamReader(tokenFileInfo.CreateReadStream());
            var content = reader.ReadToEnd();
            var tokens = JsonSerializer.Deserialize<Token[]>(content);
            return tokens;
        }
        return [];
    }
}
