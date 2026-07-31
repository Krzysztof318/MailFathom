// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System.Data.Common;
using System.Text.RegularExpressions;
using MailFathom.Application.Persistence;
using MailFathom.CodeCoverage;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence;

/// <summary>Reads the migration history and the schema facts a build cannot infer from its own model.</summary>
/// <remarks>
/// The migration comparison is between the assembly's compiled migration set and the rows in the history table, so it
/// answers what this build expects rather than what the model currently describes. A model change with no migration
/// behind it is therefore invisible there by design; catching that is the reviewer's job when the migration is
/// generated. The text search configuration is read from the catalogue instead, because the identifiers are identical
/// whichever configuration a migration was generated from.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed partial class EfCoreDatabaseSchemaInspector(MailFathomDbContext dbContext) : IDatabaseSchemaInspector
{
    /// <summary>The table and column the generated search vector lives in, as the model maps them.</summary>
    private const string SearchDocumentTableName = "email_search_documents";

    private const string SearchVectorColumnName = "SearchVector";

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

    /// <inheritdoc />
    public async Task<string> ReadSearchVectorTextSearchConfigurationAsync(CancellationToken cancellationToken)
    {
        string?[] generationExpressions;

        try
        {
            // information_schema reports the expression PostgreSQL stored for the generated column, which is the
            // configuration the lexemes in that column were actually built with. Both identifiers are compile-time
            // constants of this assembly rather than anything a caller supplies.
            generationExpressions = await dbContext.Database
                .SqlQueryRaw<string?>(
                    """
                    SELECT generation_expression AS "Value"
                    FROM information_schema.columns
                    WHERE table_schema = current_schema()
                      AND table_name = {0}
                      AND column_name = {1}
                    """,
                    SearchDocumentTableName,
                    SearchVectorColumnName)
                .ToArrayAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is DbException or InvalidOperationException)
        {
            throw new DatabaseSchemaStateUnreadableException(
                "The PostgreSQL column catalogue could not be read, so the text search configuration the lexical index was built with is unknown. Check that the database is reachable and that the configured user may read the schema catalogue.",
                exception);
        }

        // An empty result means the column is absent, and a null expression means it exists without being generated.
        // Either way this database is not one the migration produced, which the caller cannot tell from a name it
        // simply did not learn.
        var generationExpression = generationExpressions.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(generationExpression))
        {
            throw new DatabaseSchemaStateUnreadableException(
                "The lexical email index carries no stored search vector expression: its generated column is absent, or the column exists without one. Every migration this build defines is applied, so the database is not the one those migrations produce. Recreate it from them rather than starting against it.");
        }

        return ReadRegisteredConfigurationName(generationExpression)
            ?? throw new DatabaseSchemaStateUnreadableException(
                "The lexical email index's stored search vector expression names no registered text search configuration, so the configuration its lexemes were built with cannot be identified. The expression was written by something other than this build's migration; recreate the database from that migration rather than starting against it.");
    }

    /// <summary>Extracts the configuration name from a stored <c>to_tsvector</c> expression.</summary>
    /// <param name="generationExpression">The expression PostgreSQL reports for the generated column.</param>
    /// <returns>The configuration name, or <see langword="null" /> when the expression names none.</returns>
    /// <remarks>
    /// PostgreSQL normalizes the first argument to a <c>regconfig</c> literal when it stores the expression, so the
    /// name is matched from that literal rather than from whatever the migration wrote. An expression that carries no
    /// such literal is reported as unknown rather than guessed at.
    /// </remarks>
    private static string? ReadRegisteredConfigurationName(string generationExpression)
    {
        var match = RegisteredConfigurationPattern().Match(generationExpression);

        return match.Success ? match.Groups[1].Value : null;
    }

    [GeneratedRegex("'([^']+)'::regconfig", RegexOptions.CultureInvariant)]
    private static partial Regex RegisteredConfigurationPattern();
}
