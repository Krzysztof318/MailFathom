// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

namespace MailFathom.Application.Persistence;

/// <summary>Reports which migrations the running build expects that the database does not carry yet.</summary>
/// <remarks>
/// <para>
/// The port reads and never writes. Applying a migration is a deliberate, reviewable deployment step run through the
/// orchestration's migration resource, so nothing behind this interface may create, alter, or drop a schema object. A
/// port that could do both would put the decision in the adapter rather than at the boundary that owns it.
/// </para>
/// <para>
/// It exists so the composition root can refuse to start against a schema the build does not recognize without taking
/// on EF Core itself: whether a pending migration is fatal is a host decision, and reading the migration history is a
/// persistence one.
/// </para>
/// </remarks>
public interface IDatabaseSchemaInspector
{
    /// <summary>Reads the identifiers of the migrations the build defines and the database has not applied.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The pending migration identifiers, in the order they would be applied, or an empty sequence when the schema is current.</returns>
    /// <exception cref="DatabaseSchemaStateUnreadableException">
    /// Thrown when the migration history cannot be read at all, which leaves the schema of unknown shape rather than
    /// known to be current or known to be behind.
    /// </exception>
    /// <remarks>
    /// An unreachable database, a missing history table, and an insufficiently privileged user are all unreadable
    /// rather than current. Reporting an empty sequence for any of them would let a host start against a database it
    /// never actually inspected.
    /// </remarks>
    Task<IReadOnlyList<string>> ReadPendingMigrationIdentifiersAsync(CancellationToken cancellationToken);

    /// <summary>Reads the text search configuration the database compiled into the search vector's generated column.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The configuration name the schema actually uses.</returns>
    /// <exception cref="DatabaseSchemaStateUnreadableException">
    /// Thrown when the catalogue cannot be read at all, and when it is read but identifies no configuration: the column
    /// is absent, it carries no stored expression, or that expression names no registered configuration.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The name is part of the schema rather than of a query: it decides how every indexed word is stemmed and which
    /// words are dropped, and it is frozen into a stored generated column when the table is created. A migration is
    /// therefore generated for one configuration and cannot be reused for another, and a running deployment that is
    /// configured for a second one would query with stemming the stored lexemes were never built with — which shows up
    /// as missing search results, not as an error.
    /// </para>
    /// <para>
    /// Comparing migration identifiers cannot catch that, because the identifiers are the same either way. This reads
    /// what PostgreSQL actually holds, so the answer does not depend on which configuration the migration was generated
    /// from or on whether anyone remembered to regenerate it.
    /// </para>
    /// <para>
    /// A caller reaches this only once every migration is applied, and the migration that creates the search document
    /// table creates the generated column with it. A database that then reports no column, no expression, or an
    /// expression naming no configuration is not one of ours, so it is unreadable rather than a name absent for a
    /// benign reason. Returning nothing for it would hand the caller a state it could only treat as agreement.
    /// </para>
    /// </remarks>
    Task<string> ReadSearchVectorTextSearchConfigurationAsync(CancellationToken cancellationToken);
}
