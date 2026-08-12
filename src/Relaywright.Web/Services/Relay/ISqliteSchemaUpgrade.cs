using Relaywright.Web.Data;

namespace Relaywright.Web.Services.Relay;

public interface ISqliteSchemaUpgrade
{
    int Version { get; }

    Task ApplyAsync(ApplicationDbContext dbContext, CancellationToken cancellationToken);
}
