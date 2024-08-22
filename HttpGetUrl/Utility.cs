using System.Text.Json;

namespace HttpGetUrl;

public static class Utility
{
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
        if (!parentDomain.StartsWith("."))
        {
            parentDomain = "." + parentDomain;
        }
        if (!subDomain.StartsWith("."))
        {
            subDomain = "." + subDomain;
        }
        return subDomain.EndsWith(parentDomain, StringComparison.OrdinalIgnoreCase);
    }

    public static string MakeValidFileName(string fileName, char replacement = '_')
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var chars = fileName.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (invalidChars.Contains(chars[i]))
                chars[i] = replacement;
        }
        return new string(chars);
    }
}
