namespace HttpGetUrl;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
sealed class DownloaderAttribute(string identity, string[] supportedDomains) : Attribute
{
    public string Identity { get; } = identity;
    public string[] SupportedDomains { get; } = supportedDomains;
}
