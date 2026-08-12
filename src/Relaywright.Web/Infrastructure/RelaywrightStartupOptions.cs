using Microsoft.Extensions.Options;
using Relaywright.Web.Options;

namespace Relaywright.Web.Infrastructure;

public sealed class RelaywrightStartupOptions
{
    private RelaywrightStartupOptions(
        StorageOptions storage,
        BootstrapAdminOptions bootstrapAdmin,
        DatabaseOptions database,
        UpdateCheckOptions updateCheck,
        QueueProcessingOptions queueProcessing)
    {
        Storage = storage;
        BootstrapAdmin = bootstrapAdmin;
        Database = database;
        UpdateCheck = updateCheck;
        QueueProcessing = queueProcessing;
    }

    public StorageOptions Storage { get; }

    public BootstrapAdminOptions BootstrapAdmin { get; }

    public DatabaseOptions Database { get; }

    public UpdateCheckOptions UpdateCheck { get; }

    public QueueProcessingOptions QueueProcessing { get; }

    public static RelaywrightStartupOptions Load(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new RelaywrightStartupOptions(
            configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>() ?? new StorageOptions(),
            configuration.GetSection(BootstrapAdminOptions.SectionName).Get<BootstrapAdminOptions>() ?? new BootstrapAdminOptions(),
            configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions(),
            configuration.GetSection(UpdateCheckOptions.SectionName).Get<UpdateCheckOptions>() ?? new UpdateCheckOptions(),
            configuration.GetSection(QueueProcessingOptions.SectionName).Get<QueueProcessingOptions>() ?? new QueueProcessingOptions());
        var failures = options.Validate().ToArray();
        if (failures.Length > 0)
        {
            throw new OptionsValidationException(nameof(RelaywrightStartupOptions), typeof(RelaywrightStartupOptions), failures);
        }

        return options;
    }

    private IEnumerable<string> Validate()
    {
        if (string.IsNullOrWhiteSpace(Storage.DataDirectory))
        {
            yield return "Storage:DataDirectory is required.";
        }

        foreach (var (name, value) in GetStorageLeafNames())
        {
            if (!IsSafeLeafName(value))
            {
                yield return $"Storage:{name} must be a non-empty file or directory name without path separators.";
            }
        }

        DatabaseProvider provider;
        string? databaseProviderFailure = null;
        try
        {
            provider = DatabaseConfiguration.ParseProvider(Database.Provider);
        }
        catch (InvalidOperationException exception)
        {
            databaseProviderFailure = exception.Message;
            provider = DatabaseProvider.Sqlite;
        }

        if (databaseProviderFailure is not null)
        {
            yield return databaseProviderFailure;
        }

        if (provider != DatabaseProvider.Sqlite && string.IsNullOrWhiteSpace(Database.ConnectionString))
        {
            yield return $"{Database.Provider} database provider requires Database:ConnectionString.";
        }

        if (string.IsNullOrWhiteSpace(BootstrapAdmin.UserName))
        {
            yield return "BootstrapAdmin:UserName is required.";
        }

        if (string.IsNullOrWhiteSpace(BootstrapAdmin.Email) || !BootstrapAdmin.Email.Contains('@', StringComparison.Ordinal))
        {
            yield return "BootstrapAdmin:Email must be a valid email address.";
        }

        if (!string.IsNullOrEmpty(BootstrapAdmin.Password) && BootstrapAdmin.Password.Length < 12)
        {
            yield return "BootstrapAdmin:Password must contain at least 12 characters when configured.";
        }

        if (string.IsNullOrWhiteSpace(UpdateCheck.Repository)
            || UpdateCheck.Repository.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length != 2)
        {
            yield return "UpdateCheck:Repository must use the owner/repository format.";
        }

        if (UpdateCheck.IntervalHours is < UpdateCheckOptions.MinimumIntervalHours or > UpdateCheckOptions.MaximumIntervalHours)
        {
            yield return $"UpdateCheck:IntervalHours must be between {UpdateCheckOptions.MinimumIntervalHours} and {UpdateCheckOptions.MaximumIntervalHours}.";
        }

        if (UpdateCheck.TimeoutSeconds is < UpdateCheckOptions.MinimumTimeoutSeconds or > UpdateCheckOptions.MaximumTimeoutSeconds)
        {
            yield return $"UpdateCheck:TimeoutSeconds must be between {UpdateCheckOptions.MinimumTimeoutSeconds} and {UpdateCheckOptions.MaximumTimeoutSeconds}.";
        }

        if (UpdateCheck.StartupDelaySeconds is < UpdateCheckOptions.MinimumStartupDelaySeconds or > UpdateCheckOptions.MaximumStartupDelaySeconds)
        {
            yield return $"UpdateCheck:StartupDelaySeconds must be between {UpdateCheckOptions.MinimumStartupDelaySeconds} and {UpdateCheckOptions.MaximumStartupDelaySeconds}.";
        }

        if (QueueProcessing.StaleClaimMinutes is < QueueProcessingOptions.MinimumStaleClaimMinutes or > QueueProcessingOptions.MaximumStaleClaimMinutes)
        {
            yield return $"QueueProcessing:StaleClaimMinutes must be between {QueueProcessingOptions.MinimumStaleClaimMinutes} and {QueueProcessingOptions.MaximumStaleClaimMinutes}.";
        }
    }

    private IEnumerable<(string Name, string Value)> GetStorageLeafNames()
    {
        yield return (nameof(Storage.DatabaseFileName), Storage.DatabaseFileName);
        yield return (nameof(Storage.SpoolDirectoryName), Storage.SpoolDirectoryName);
        yield return (nameof(Storage.KeyDirectoryName), Storage.KeyDirectoryName);
        yield return (nameof(Storage.BackupDirectoryName), Storage.BackupDirectoryName);
        yield return (nameof(Storage.RestorePendingDirectoryName), Storage.RestorePendingDirectoryName);
        yield return (nameof(Storage.CertificateDirectoryName), Storage.CertificateDirectoryName);
        yield return (nameof(Storage.AdminHttpsCertificateFileName), Storage.AdminHttpsCertificateFileName);
        yield return (nameof(Storage.AdminWebListenerFileName), Storage.AdminWebListenerFileName);
    }

    private static bool IsSafeLeafName(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value is not "." and not ".."
            && !Path.IsPathRooted(value)
            && value.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) < 0;
    }
}

public static class RelaywrightOptionsServiceCollectionExtensions
{
    public static RelaywrightStartupOptions AddRelaywrightOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = RelaywrightStartupOptions.Load(configuration);
        services.AddSingleton<IOptions<StorageOptions>>(
            Microsoft.Extensions.Options.Options.Create(options.Storage));
        services.AddSingleton<IOptions<BootstrapAdminOptions>>(
            Microsoft.Extensions.Options.Options.Create(options.BootstrapAdmin));
        services.AddSingleton<IOptions<DatabaseOptions>>(
            Microsoft.Extensions.Options.Options.Create(options.Database));
        services.AddSingleton<IOptions<UpdateCheckOptions>>(
            Microsoft.Extensions.Options.Options.Create(options.UpdateCheck));
        services.AddSingleton<IOptions<QueueProcessingOptions>>(
            Microsoft.Extensions.Options.Options.Create(options.QueueProcessing));
        return options;
    }
}
