using Microsoft.EntityFrameworkCore;
using Relaywright.Web.Data;

namespace Relaywright.Web.Tests.Support;

internal sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
    : IDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext()
    {
        return new ApplicationDbContext(options);
    }
}
