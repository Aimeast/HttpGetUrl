using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace HttpGetUrl;

public static class Utility
{
    private static readonly Lazy<Dictionary<string, string>> _mimes = new(() =>
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var stream = typeof(Utility).GetTypeInfo().Assembly.GetManifestResourceStream("HttpGetUrl.mines.txt");
        using var reader = new StreamReader(stream);
        while (!reader.EndOfStream)
        {
            var line = reader.ReadLine().Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
                continue;
            var arr = line.Split('\t');
            var mime = arr[0];
            var ext = arr[1];
            dict.Add(mime, ext);
        }
        return dict;
    });

    public static Dictionary<string, string> Mimes => _mimes.Value;

    public static IEnumerable<JsonElement> SearchJson(this JsonElement jsonElement, string searchText)
    {
        if (jsonElement.ValueKind == JsonValueKind.Array)
            foreach (var element in jsonElement.EnumerateArray())
                foreach (var yield in SearchJson(element, searchText))
                    yield return yield;
        else if (jsonElement.ValueKind == JsonValueKind.Object)
            foreach (var element in jsonElement.EnumerateObject())
                if (element.Name == searchText)
                    yield return element.Value;
                else
                    foreach (var yield in SearchJson(element.Value, searchText))
                        yield return yield;
    }

    public static IEnumerable<JsonElement> SearchJson(this IEnumerable<JsonElement> elements, string searchText)
    {
        foreach (var element in elements)
            foreach (var yield in SearchJson(element, searchText))
                yield return yield;
    }

    public static bool IsSubdomain(string parentDomain, string subDomain)
    {
        if (!parentDomain.StartsWith('.'))
        {
            parentDomain = "." + parentDomain;
        }
        if (!subDomain.StartsWith('.'))
        {
            subDomain = "." + subDomain;
        }
        return subDomain.EndsWith(parentDomain, StringComparison.OrdinalIgnoreCase);
    }

    public static string MakeValidFileName(string fileName, char replacement = '_')
    {
        // Define invalid characters based on Windows, Linux, and macOS.
        // DO NOT call `Path.GetInvalidFileNameChars()`,
        // Because the array returned is different on different operating systems.
        char[] invalidChars = ['\\', '/', ':', '*', '?', '"', '<', '>', '|'];
        var chars = fileName.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (invalidChars.Contains(chars[i]))
                chars[i] = replacement;
        }
        return new string(chars);
    }

    public static string TruncateString(string input, int startLength, int endLength)
    {
        if (input.Length <= startLength + endLength)
        {
            return input;
        }

        string start = input[..startLength];
        string end = input.Substring(input.Length - endLength, endLength);
        return $"{start}........{end}";
    }

    public static string TruncateStringInUtf8(string input, int startBytes, int endBytes)
    {
        var count = Encoding.UTF8.GetByteCount(input);
        if (count <= startBytes + endBytes + 3)
            return input;
        var start = MaxCharsInUtf8(input, startBytes);
        var end = MaxCharsInUtf8(input.Reverse(), endBytes);
        return $"{input[..start]}...{input.Substring(input.Length - end, end)}";
    }

    private static int MaxCharsInUtf8(IEnumerable<char> input, int bytesNum)
    {
        var max = 0;
        foreach (var ch in input)
        {
            var count = Encoding.UTF8.GetByteCount([ch]);
            bytesNum -= count;
            if (bytesNum < 0)
                return max;
            max++;
        }
        return max;
    }

    public static async ValueTask<string> RunCmdFirstLine(string path, string args, bool wait = false)
    {
        var processStartInfo = new ProcessStartInfo
        {
            FileName = path,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = processStartInfo };
        process.Start();
        var std = await process.StandardOutput.ReadLineAsync();
        var err = await process.StandardError.ReadLineAsync();
        if (wait)
            await process.WaitForExitAsync();
        else
            process.Close();

        return (std ?? err)?.Trim() ?? process.ExitCode.ToString();
    }

    public static string FormatSize(long size)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double val = size;
        var i = 0;
        while (val >= 1000 && i < units.Length - 1)
        {
            val /= 1024;
            i++;
        }
        return $"{Math.Round(val, 1)}{units[i]}";
    }
}
