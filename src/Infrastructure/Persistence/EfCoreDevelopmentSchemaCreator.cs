// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.CodeCoverage;

namespace MailMcp.Infrastructure.Persistence;

/// <summary>Creates the schema from the EF Core model, which is what a developer's database gets until migrations exist.</summary>
/// <remarks>
/// <c>EnsureCreated</c> is not a migration and must never become one: it writes the model's current shape into an empty
/// database and does nothing at all to a database that already has tables. That is exactly why it is safe here and
/// unsafe anywhere else, and why specification 19 replaces it rather than promoting it.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class EfCoreDevelopmentSchemaCreator(MailMcpDbContext dbContext) : IDevelopmentSchemaCreator
{
    /// <inheritdoc />
    public Task CreateSchemaAsync(CancellationToken cancellationToken) =>
        dbContext.Database.EnsureCreatedAsync(cancellationToken);
}
