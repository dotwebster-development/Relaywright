namespace Relaywright.Web.Data.Entities;

public sealed class ConfigurationSnapshot
{
    public Guid Id { get; set; }

    public string Area { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string PayloadJson { get; set; } = string.Empty;

    public string? CreatedBy { get; set; }

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

    public bool IsRollback { get; set; }
}
