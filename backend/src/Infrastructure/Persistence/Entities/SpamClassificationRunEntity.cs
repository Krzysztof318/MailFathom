// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Spam.Actions;
using MailFathom.Application.Spam.Runs;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Spam;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>The whole-mailbox classification run an account has been asked for, and how far it has been carried.</summary>
/// <remarks>
/// <para>
/// Keyed by the account rather than by a run identifier, which is what makes one outstanding run per account a property
/// of the schema instead of a check somebody has to remember: a second request reaches the same row, so two callers
/// asking together resolve to one run rather than to two walks over one mailbox.
/// </para>
/// <para>
/// The row survives the run it describes, holding the terms and the ending of the last one until a new request replaces
/// it. That is what lets a request be answered with "the previous run finished and here is what it found" rather than
/// with silence, and it costs one row per account.
/// </para>
/// <para>
/// No foreign key onto the mailbox account, for the reason the rule run's row has none: the account row is written by
/// whichever synchronization run first binds a folder, and a run may be asked for before that has happened.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class SpamClassificationRunEntity
{
    /// <summary>The greatest length a classification profile has, which is the derived identity's own fixed width.</summary>
    internal const int ProfileLength = SpamClassificationProfile.LengthInCharacters;

    /// <summary>The greatest length one scanned folder alias may carry, which the domain value already refuses to exceed.</summary>
    internal const int MaximumFolderAliasLength = 128;

    public required string MailboxAccountId { get; set; }

    public DateTimeOffset RequestedAt { get; set; }

    /// <summary>Gets or sets the folders the run walks, as the aliases the operator's configuration names them by.</summary>
    /// <remarks>
    /// Stored rather than read again from configuration, because the run outlives the settings it was asked under and a
    /// record whose scope moved under it could not say which mail the run had covered.
    /// </remarks>
    public required string[] FolderAliases { get; set; }

    /// <summary>Gets or sets whether the run writes down what its verdicts ask of the mailbox, or only works it out.</summary>
    public SpamActionPosture Posture { get; set; }

    /// <summary>Gets or sets whether mail already decided under the run's profile is scored again rather than skipped.</summary>
    public bool Rescores { get; set; }

    /// <summary>Gets or sets the settings the run is bound to, absent until the first pass picks the run up.</summary>
    public string? Profile { get; set; }

    /// <summary>Gets or sets the identity of the last occurrence a batch committed, absent while the run has committed none.</summary>
    public Guid? Position { get; set; }

    public int ClassifiedEmailCount { get; set; }

    public int SpamEmailCount { get; set; }

    public int UndeterminedEmailCount { get; set; }

    public int SkippedEmailCount { get; set; }

    public int UnclassifiableEmailCount { get; set; }

    public int ActedEmailCount { get; set; }

    /// <summary>Gets or sets when the run stopped being outstanding, absent while it still is.</summary>
    public DateTimeOffset? EndedAt { get; set; }

    /// <summary>Gets or sets how the run ended, absent for exactly as long as <see cref="EndedAt" /> is.</summary>
    public SpamClassificationRunEnding? Ending { get; set; }

    /// <summary>Gets or sets the optimistic concurrency token, which is PostgreSQL's own <c>xmin</c> rather than a column.</summary>
    /// <remarks>
    /// A pass and an arriving request can both reach this row: the pass commits a position while somebody asks for a run
    /// the pass is about to finish. The token is what turns that into a conflict the retry policy resolves from a fresh
    /// read instead of one writer overwriting the other's decision.
    /// </remarks>
    public uint ConcurrencyVersion { get; set; }
}
