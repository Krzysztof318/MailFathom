// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One Discover run, from the moment it is opened until nobody can come back for it.</summary>
/// <remarks>
/// <para>
/// The row is what makes a run reachable from every replica: the one executing it writes here, and any of them answers
/// a read for it. It carries no part of the answer — the events beside it do — so what a replica needs to decide
/// whether a run exists, whether it is still working, and whether the person asking may see it is one narrow row.
/// </para>
/// <para>
/// Nothing here is mail. A generated run identifier, a generated user identity, and four instants are the whole row.
/// What the run read and what it composed is in
/// <see cref="DiscoveryRunEventEntity" />, which cascades from this, and the removal of this row is the storage
/// limitation on both.
/// </para>
/// <para>
/// It carries no concurrency token. Every statement against it is composed and conditional on the state it read — a run
/// opens only while its user is under the deployment's bound, an event is written only while the run is running and
/// unstopped, and a stop is recorded only while there is something to stop — so what would be a read followed by a
/// write is one statement PostgreSQL settles instead.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class DiscoveryRunEntity
{
    /// <summary>The table these rows live in, named here because every statement against it is composed.</summary>
    internal const string TableName = "discovery_runs";

    /// <summary>The key column, named here for the same reason the table is.</summary>
    internal const string IdColumnName = "Id";

    /// <summary>The column naming whose question the run answers, named here for the same reason the table is.</summary>
    internal const string UserIdColumnName = "UserId";

    /// <summary>The column holding when the run was opened, named here for the same reason the table is.</summary>
    internal const string StartedAtColumnName = "StartedAt";

    /// <summary>The column holding when the run was last read or written, named here for the same reason the table is.</summary>
    internal const string LastUsedAtColumnName = "LastUsedAt";

    /// <summary>The column holding when the run published its ending, named here for the same reason the table is.</summary>
    internal const string EndedAtColumnName = "EndedAt";

    /// <summary>The column holding when somebody asked the run to stop, named here for the same reason the table is.</summary>
    internal const string StopRequestedAtColumnName = "StopRequestedAt";

    /// <summary>Gets or sets the identifier the run is addressed by, which a client is handed and presents back.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the user whose mail the run reads, which is who may read what it wrote.</summary>
    public Guid UserId { get; set; }

    /// <summary>Gets or sets when the run was opened, in UTC.</summary>
    /// <remarks>What the ceiling on a run nothing ever ended is measured from, which is the window that closes a run whose replica went away without reporting.</remarks>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>Gets or sets when anything last read or wrote the run, in UTC.</summary>
    /// <remarks>What the retention window of an ended run is measured from, so a client that is still reading is never forgotten out from under itself.</remarks>
    public DateTimeOffset LastUsedAt { get; set; }

    /// <summary>Gets or sets when the run wrote its ending, in UTC, and <see langword="null" /> while it is still executing.</summary>
    public DateTimeOffset? EndedAt { get; set; }

    /// <summary>Gets or sets when somebody asked the run to stop, in UTC, and <see langword="null" /> where nobody did.</summary>
    /// <remarks>
    /// Recorded rather than acted on here, because what a run spent is the executing replica's ledger to report: the
    /// next event that replica writes is refused on this column, and the ending it then writes carries the true counts.
    /// It is how a stop that landed on one replica reaches a run executing on another.
    /// </remarks>
    public DateTimeOffset? StopRequestedAt { get; set; }
}
