// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
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
}
