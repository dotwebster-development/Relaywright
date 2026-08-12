using System.Text.Json;
using Relaywright.Web.Infrastructure;

namespace Relaywright.Web.Services.Security;

public sealed class AdminHttpsCertificateConfigurationStore(AppPaths paths)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public AdminHttpsCertificateConfiguration? Load()
    {
        if (!File.Exists(paths.AdminHttpsCertificateConfigurationPath))
        {
            return null;
        }

        var json = File.ReadAllText(paths.AdminHttpsCertificateConfigurationPath);
        return JsonSerializer.Deserialize<AdminHttpsCertificateConfiguration>(json, JsonOptions)
            ?? throw new InvalidOperationException("Admin HTTPS certificate configuration could not be read.");
    }

    public async Task<AdminHttpsCertificateConfiguration?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(paths.AdminHttpsCertificateConfigurationPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(paths.AdminHttpsCertificateConfigurationPath);
        return await JsonSerializer.DeserializeAsync<AdminHttpsCertificateConfiguration>(stream, JsonOptions, cancellationToken);
    }

    public async Task SaveAsync(
        AdminHttpsCertificateConfiguration configuration,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(paths.DataDirectory);
        var tempPath = Path.Combine(paths.DataDirectory, $"{Guid.NewGuid():N}.json");
        try
        {
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, configuration, JsonOptions, cancellationToken);
            }

            File.Move(tempPath, paths.AdminHttpsCertificateConfigurationPath, overwrite: true);
        }
        finally
        {
            DeleteIfExists(tempPath);
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
