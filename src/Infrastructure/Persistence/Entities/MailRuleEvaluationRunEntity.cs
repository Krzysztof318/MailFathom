// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Rules.Evaluation;
using MailFathom.Application.Rules.History;
using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>The whole-mailbox rule run an account has been asked for, and how far its synchronization runs have carried it.</summary>
/// <remarks>
/// <para>
/// Keyed by the account rather than by a run identifier, which is what makes one outstanding run per account a property
/// of the schema instead of a check somebody has to remember: a second request reaches the same row, so two callers
/// asking together resolve to one run rather than to two walks over one mailbox.
/// </para>
/// <para>
/// The row survives the run it describes, holding the ending of the last one until a new request replaces it. That is
/// what lets a request be answered with "the previous run finished" rather than with silence, and it costs one row per
/// account.
/// </para>
/// <para>
/// No foreign key onto the mailbox account, for the reason the refresh-token row has none: the account row is written by
/// whichever synchronization run first binds a folder, and a run may be asked for before that has happened.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class MailRuleEvaluationRunEntity
{
    /// <summary>The greatest length a rule set revision has, which is the derived identity's own fixed width.</summary>
    internal const int RevisionLength = 12;

    public required string MailboxAccountId { get; set; }

    public DateTimeOffset RequestedAt { get; set; }

    /// <summary>Gets or sets what started the run, which decides the rules it reaches and what its executions are recorded as.</summary>
    /// <remarks>
    /// Stored as text for the reason every other outcome here is, and non-null with the requested run as its default, so
    /// a row written before schedules existed reads back as what it was rather than as an unset value.
    /// </remarks>
    public MailRuleExecutionTrigger Trigger { get; set; }

    /// <summary>Gets or sets the rule set the run is bound to, absent until the first pass picks the run up.</summary>
    public string? Revision { get; set; }

    /// <summary>Gets or sets the identity of the last email a batch committed, absent while the run has committed none.</summary>
    public Guid? Position { get; set; }

    public int EvaluatedEmailCount { get; set; }

    public int MatchedEmailCount { get; set; }

    public int SkippedEmailCount { get; set; }

    /// <summary>Gets or sets when the run stopped being outstanding, absent while it still is.</summary>
    public DateTimeOffset? EndedAt { get; set; }

    /// <summary>Gets or sets how the run ended, absent for exactly as long as <see cref="EndedAt" /> is.</summary>
    public MailRuleEvaluationRunEnding? Ending { get; set; }

    /// <summary>Gets or sets the optimistic concurrency token, which is PostgreSQL's own <c>xmin</c> rather than a column.</summary>
    /// <remarks>
    /// A pass and an arriving request can both reach this row: the pass commits a position while somebody asks for a run
    /// the pass is about to finish. The token is what turns that into a conflict the retry policy resolves from a fresh
    /// read instead of one writer overwriting the other's decision.
    /// </remarks>
    public uint ConcurrencyVersion { get; set; }
}
