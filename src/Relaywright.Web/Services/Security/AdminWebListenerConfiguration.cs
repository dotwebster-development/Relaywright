namespace Relaywright.Web.Services.Security;

public sealed class AdminWebListenerConfiguration
{
    public const int DefaultHttpsPort = 5443;

    public const int DefaultHttpPort = 5080;

    public int HttpsPort { get; set; } = DefaultHttpsPort;

    public bool EnableHttp { get; set; } = true;

    public int HttpPort { get; set; } = DefaultHttpPort;

    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;

    public string[] GetUrls()
    {
        var urls = new List<string> { $"https://*:{HttpsPort}" };
        if (EnableHttp)
        {
            urls.Add($"http://*:{HttpPort}");
        }

        return urls.ToArray();
    }
}
