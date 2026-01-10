namespace HttpGetUrl.Models;

public class Token
{
    public string Name { get; set; }
    public string Value { get; set; }
    public string Domain { get; set; }
    public string Path { get; set; }
    public bool HttpOnly { get; set; }
    public bool Secure { get; set; }
    public DateTimeOffset Expires { get; set; }
}
