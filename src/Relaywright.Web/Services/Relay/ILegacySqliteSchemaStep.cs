using Relaywright.Web.Data;

namespace Relaywright.Web.Services.Relay;

public interface ILegacySqliteSchemaStep
{
    int Order { get; }

    string Name { get; }

    Task ApplyAsync(ApplicationDbContext dbContext, CancellationToken cancellationToken);
}
