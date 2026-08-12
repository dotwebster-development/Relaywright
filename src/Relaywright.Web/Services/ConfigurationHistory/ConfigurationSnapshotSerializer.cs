using System.Text.Json;

namespace Relaywright.Web.Services.ConfigurationHistory;

public sealed class ConfigurationSnapshotSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public string Serialize<T>(T value)
    {
        return JsonSerializer.Serialize(value, JsonOptions);
    }

    public T Deserialize<T>(string payloadJson)
    {
        return JsonSerializer.Deserialize<T>(payloadJson, JsonOptions)
            ?? throw new InvalidOperationException("Configuration snapshot payload could not be read.");
    }
}
