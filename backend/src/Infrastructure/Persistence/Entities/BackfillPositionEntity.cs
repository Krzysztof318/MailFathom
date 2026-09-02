// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>How far one named background backfill has walked the stored emails.</summary>
/// <remarks>
/// Keyed by the backfill's name rather than being a single-row table, so a later backfill over the same emails records
/// its own progress here instead of needing a table of its own. The position is a stored-email identifier because the
/// walk is ordered by that identifier, which is the only ordering that is total, stable, and already indexed.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class BackfillPositionEntity
{
    /// <summary>The name the extraction backfill records its progress under.</summary>
    internal const string StoredEmailExtractionName = "stored-email-extraction";

    /// <summary>The name the embedding backfill records its progress under.</summary>
    /// <remarks>
    /// A row this backfill writes is removed rather than parked at the end, because its walk is a repeating sweep: the
    /// absence of a row means the next run starts at the beginning, which is what the extraction backfill's absence of
    /// a row means as well.
    /// </remarks>
    internal const string StoredEmailEmbeddingName = "stored-email-embedding";

    /// <summary>The greatest length a backfill name may have, which bounds the key column.</summary>
    internal const int MaximumNameLength = 64;

    public required string Name { get; set; }

    public Guid LastProcessedStoredEmailId { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Gets or sets the sensitive-content configuration this walk reached its position under, or
    /// <see langword="null" /> when it walked with no scanner switched on.
    /// </summary>
    /// <remarks>
    /// The position means "everything behind here is done", and what counts as done depends on the configuration the
    /// walk was judging rows against. A walk that skipped a message it could not re-read — one whose raw MIME is gone,
    /// or parses for no reader — leaves that row outstanding forever, so without this column the cursor would sit past
    /// it and a later configuration change would never revisit anything. A recorded value that is not the current one
    /// therefore restarts the walk from the beginning rather than resuming it.
    /// </remarks>
    public string? SensitiveContentStamp { get; set; }
}
