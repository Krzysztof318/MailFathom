// Copyright © 2026 Krzysztof Kasprowicz

using System.Data.Common;
using MailMcp.Application.Persistence;
using MailMcp.CodeCoverage;
using Microsoft.EntityFrameworkCore;

namespace MailMcp.Infrastructure.Persistence;

/// <summary>Reads the migration history through EF Core and compares it against the migrations this build defines.</summary>
/// <remarks>
/// The comparison is between the assembly's compiled migration set and the rows in the history table, so it answers
/// what this build expects rather than what the model currently describes. A model change with no migration behind it
/// is therefore invisible here by design; catching that is the reviewer's job when the migration is generated.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class EfCoreDatabaseSchemaInspector(MailMcpDbContext dbContext) : IDatabaseSchemaInspector
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ReadPendingMigrationIdentifiersAsync(CancellationToken cancellationToken)
    {
        try
        {
            var pendingMigrations = await dbContext.Database.GetPendingMigrationsAsync(cancellationToken);

            return [.. pendingMigrations];
        }
        catch (Exception exception) when (exception is DbException or InvalidOperationException)
        {
            // Both classes mean the same thing to a caller: nothing was learned about the schema. A connection that was
            // refused arrives as the first, and a history table this user cannot read as the second.
            throw new DatabaseSchemaStateUnreadableException(
                "The PostgreSQL migration history could not be read, so the database schema is of unknown shape. Check that the database is reachable and that the configured user may read the migration history table.",
                exception);
        }
    }
}
