using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WorriorVex.Infrastructure.Search;

namespace WorriorVex.Infrastructure.Persistence;

/// <summary>Brings the local database up to the current schema at startup.</summary>
public sealed class DatabaseInitializer(
    IDbContextFactory<WorriorVexDbContext> contextFactory,
    ILogger<DatabaseInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count > 0)
        {
            logger.LogInformation("Applying {Count} database migration(s): {Migrations}", pending.Count, string.Join(", ", pending));
        }

        await context.Database.MigrateAsync(cancellationToken);

        var indexed = await SearchIndex.CompleteAsync(context, cancellationToken);
        if (indexed > 0)
        {
            logger.LogInformation("Added {Count} note(s) to the search index", indexed);
        }

        logger.LogInformation("Database ready at {DataSource}", context.Database.GetDbConnection().DataSource);
    }
}
