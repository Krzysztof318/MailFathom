// Copyright © 2026 Krzysztof Kasprowicz

namespace MailMcp.Application.Persistence;

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
}
