using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Relaywright.Web.Data;
using Relaywright.Web.Data.Entities;

namespace Relaywright.Web.Tests.Support;

internal sealed class SqliteTestStore : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    private SqliteTestStore(SqliteConnection connection, TestDbContextFactory dbContextFactory)
    {
        _connection = connection;
        DbContextFactory = dbContextFactory;
    }

    public TestDbContextFactory DbContextFactory { get; }

    public static async Task<SqliteTestStore> CreateAsync(bool seedRelayConfiguration = false)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        var factory = new TestDbContextFactory(options);
        await using var dbContext = factory.CreateDbContext();
        await dbContext.Database.EnsureCreatedAsync();

        if (seedRelayConfiguration)
        {
            dbContext.RelayConfigurations.Add(TestData.RelayConfiguration());
            await dbContext.SaveChangesAsync();
        }

        return new SqliteTestStore(connection, factory);
    }

    public ApplicationDbContext CreateDbContext()
    {
        return DbContextFactory.CreateDbContext();
    }

    public async Task<QueuedMessage?> FindMessageAsync(Guid id)
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.QueuedMessages
            .Include(x => x.Recipients)
            .Include(x => x.DeliveryAttempts)
            .SingleOrDefaultAsync(x => x.Id == id);
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
    }
}
