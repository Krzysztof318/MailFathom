// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>The re-derivation of a scope's stored mail an operator asked for, and how far the jobs carrying it have come.</summary>
/// <remarks>
/// <para>
/// Keyed by the scope rather than by the run's own identifier, which is what makes one run per scope a property of the
/// schema instead of a check somebody has to remember: a second request reaches the same row, so two operators asking
/// together resolve to one run rather than to two walks over one mailbox.
/// </para>
/// <para>
/// A table of its own rather than columns on <see cref="MailRederivationPositionEntity" />, because the two have
/// different lifetimes. The position is the walk's cursor and exists only while a scope is part-walked; the run survives
/// its own ending, so that an operator asking afterwards reads what it found rather than reading silence and being
/// unable to tell a finished run from one nobody ever asked for.
/// </para>
/// <para>
/// No foreign key onto the mailbox account, for the reason the position row has none: the account row is written by
/// whichever synchronization run first binds a folder, and a re-derivation may be asked for before that has happened.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class MailRederivationRunEntity
{
    public required string MailboxAccountId { get; set; }

    /// <summary>Gets or sets the folder the run walks, or <see cref="MailRederivationPositionEntity.WholeAccountFolder" /> for a whole-account run.</summary>
    /// <remarks>The same keyed value as the position row's, so the two rows of one scope are keyed alike and a reader comparing them needs no second rule.</remarks>
    public required string FolderAlias { get; set; }

    public Guid RunId { get; set; }

    public DateTimeOffset RequestedAt { get; set; }

    /// <summary>Gets or sets how many segments of the run have been enqueued, which is also the key the next one is enqueued under.</summary>
    public int SegmentCount { get; set; }

    public int RederivedEmailCount { get; set; }

    public int UnreadableEmailCount { get; set; }

    public int MissingContentEmailCount { get; set; }

    /// <summary>Gets or sets when the run reached the end of its scope, absent while it has not.</summary>
    public DateTimeOffset? EndedAt { get; set; }

    /// <summary>Gets or sets the optimistic concurrency token, which is PostgreSQL's own <c>xmin</c> rather than a column.</summary>
    /// <remarks>
    /// Two attempts can reach this row at once for as long as it takes a lost lease to be noticed, and an arriving
    /// request can reach it while one of them is committing a segment's counts. The token is what turns that into a
    /// conflict the retry policy resolves from a fresh read instead of one writer overwriting the other's progress.
    /// </remarks>
    public uint ConcurrencyVersion { get; set; }
}
