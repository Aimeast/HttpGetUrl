namespace HttpGetUrl.Downloaders;

public struct MatchResult
{
    public MatchStatus MatchStatus { get; set; }
    public List<(string Url, string FileName)> Values { get; set; }
}
