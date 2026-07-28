// Copyright © 2026 Krzysztof Kasprowicz

namespace MailMcp.Infrastructure.Persistence;

/// <summary>Creates the local PostgreSQL schema directly from the EF Core model, for a developer's own database only.</summary>
/// <remarks>
/// <para>
/// This is temporary scaffolding with a single owner. Migrations are deliberately deferred until the schema that
/// specifications 07 through 12 grow has settled, which leaves a window in which nothing can create the tables a
/// developer needs to run the host at all. Specification 19 closes that window by generating the reviewed baseline
/// migration, and removes this port, its implementation, its registration, and the setting that turns it on.
/// </para>
/// <para>
/// The port exists so that <c>Host</c> can decide whether the bootstrap may run without taking on EF Core itself:
/// the environment gate is a host decision, and creating a schema is a persistence one. It publishes no way to ask
/// which statements were executed, because nothing may come to depend on a schema that migrations will own.
/// </para>
/// </remarks>
public interface IDevelopmentSchemaCreator
{
    /// <summary>Creates the tables, constraints, and indexes the model describes, if the database has none yet.</summary>
    /// <param name="cancellationToken">Cancels the creation attempt.</param>
    /// <returns>A task that completes once the database matches the model or already held a schema.</returns>
    /// <remarks>
    /// An existing schema is left untouched rather than reconciled: nothing here compares a database against the model,
    /// so a developer whose database predates a model change recreates it themselves. No data is dropped by this call.
    /// </remarks>
    Task CreateSchemaAsync(CancellationToken cancellationToken);
}
