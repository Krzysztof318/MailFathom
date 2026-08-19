// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery.Operations;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Failures;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Delivery;

/// <summary>Reads an outbox as an operator sees it, and writes the two decisions they take about one send, in PostgreSQL.</summary>
/// <remarks>
/// <para>
/// The listing is a projection rather than an entity load, and the recipients are the reason: an address is personal
/// data, nothing on that surface reports one, and a projection is what keeps them from being read at all rather than
/// read and then dropped. The stored MIME is left unread for the same reason and one more — a page of it would pull
/// every queued message's bytes into memory.
/// </para>
/// <para>
/// Both writes are one conditional update apiece. The condition is the stage the decision applies at together with the
/// absence of a live lease, which makes the statement the whole of the exclusion: an operator and a delivery attempt
/// racing for the same record produce one change, and the operator reads the row afterwards to find out that the
/// attempt got there first. Nothing here takes a lease of its own, because an operator is not an attempt and inventing
/// one would let a decision hold a record no worker could then claim.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class OutboxOperationStore(MailFathomDbContext dbContext, TimeProvider timeProvider)
    : IOutboxOperationStore
{
    /// <inheritdoc />
    /// <remarks>
    /// The count covers every stage rather than the outstanding ones alone, because the summary is what an operator
    /// reads to see that mail is leaving as well as that it is stuck. It is therefore a grouped count over the whole of
    /// what the deployment has sent, which is why it is asked for by a command an operator types rather than published
    /// on a collector's interval; the level a dashboard graphs is measured over the outstanding rows alone.
    /// </remarks>
    public async Task<IReadOnlyList<OutboxStageCount>> CountByStageAsync(
        MailAccountId? accountId,
        CancellationToken cancellationToken)
    {
        var sends = dbContext.OutgoingEmails.AsNoTracking();

        if (accountId is { } account)
        {
            var accountValue = account.Value;

            sends = sends.Where(message => message.MailboxAccountId == accountValue);
        }

        var counted = await sends
            .GroupBy(message => message.Stage)
            .Select(group => new { Stage = group.Key, Count = group.Count() })
            .ToArrayAsync(cancellationToken);

        return [.. counted.Select(group => new OutboxStageCount(group.Stage, group.Count))];
    }

    /// <inheritdoc />
    public async Task<OutboxPage> ReadPageAsync(OutboxQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var rows = await this.Filter(query)
            .OrderByDescending(message => message.RecordedAt)
            .ThenByDescending(message => message.Id)

            // One more than the page holds, which is how the answer says whether a following page exists without a
            // second count query over the same filtered set.
            .Take(query.PageSize + 1)
            .Select(message => new OutboxRow(
                message.Id,
                message.MailboxAccountId,
                message.Stage,
                message.RequesterOrigin,
                message.AttemptCount,
                message.MimeByteLength,
                message.RecordedAt,
                message.StageChangedAt,
                message.AvailableAt,
                message.LastFailureCode,
                message.LastReplyCode))
            .ToArrayAsync(cancellationToken);

        var pageRows = rows.Take(query.PageSize).ToArray();

        return new OutboxPage(
            [.. pageRows.Select(ToEntry)],
            rows.Length > query.PageSize && pageRows.Length > 0
                ? OutboxCursor.After(
                    pageRows[^1].RecordedAt,
                    OutgoingEmailId.Create(pageRows[^1].Id),
                    query.FilterFingerprint)
                : null);
    }

    /// <inheritdoc />
    public async Task<OutboxDecisionOutcome> CancelAsync(
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken)
    {
        var cancelledAt = timeProvider.GetUtcNow();
        var recordId = outgoingEmailId.Value;
        var recorded = nameof(OutgoingEmailStage.Recorded);
        var cancelled = nameof(OutgoingEmailStage.Cancelled);

        var cancelledRows = await dbContext.Database.ExecuteSqlAsync(
            $"""
             UPDATE outgoing_emails
             SET "Stage" = {cancelled},
                 "StageChangedAt" = {cancelledAt}
             WHERE "Id" = {recordId}
               AND "Stage" = {recorded}
               AND ("LeaseExpiresAt" IS NULL OR "LeaseExpiresAt" <= {cancelledAt})
             """,
            cancellationToken);

        return cancelledRows == 1
            ? OutboxDecisionOutcome.Accepted
            : await this.ExplainRefusalAsync(recordId, CancellableStages, refusalRestated: true, cancelledAt, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OutboxDecisionOutcome> RequeueAsync(
        OutgoingEmailId outgoingEmailId,
        bool refusalRestated,
        CancellationToken cancellationToken)
    {
        var requeuedAt = timeProvider.GetUtcNow();
        var recordId = outgoingEmailId.Value;
        var recorded = nameof(OutgoingEmailStage.Recorded);
        var transmissionBegun = nameof(OutgoingEmailStage.TransmissionBegun);
        var refused = nameof(OutgoingEmailStage.Refused);

        // A refusal the caller has not restated is excluded in the statement rather than checked beforehand, so the
        // decision and its condition stay one write: a send refused between the check and the update would otherwise be
        // offered again on the strength of a stage it no longer stands at.
        var requeuedRows = await dbContext.Database.ExecuteSqlAsync(
            $"""
             UPDATE outgoing_emails
             SET "Stage" = {recorded},
                 "StageChangedAt" = {requeuedAt},
                 "AvailableAt" = {requeuedAt},
                 "AttemptCount" = 0,
                 "LeaseOwner" = NULL,
                 "LeaseExpiresAt" = NULL
             WHERE "Id" = {recordId}
               AND ("Stage" IN ({recorded}, {transmissionBegun})
                    OR ("Stage" = {refused} AND {refusalRestated}))
               AND ("LeaseExpiresAt" IS NULL OR "LeaseExpiresAt" <= {requeuedAt})
             """,
            cancellationToken);

        return requeuedRows == 1
            ? OutboxDecisionOutcome.Accepted
            : await this.ExplainRefusalAsync(recordId, RequeueableStages, refusalRestated, requeuedAt, cancellationToken);
    }

    /// <summary>The stages a withdrawal may be written from.</summary>
    private static OutgoingEmailStage[] CancellableStages => [OutgoingEmailStage.Recorded];

    /// <summary>The stages a send may be offered again from, before the restatement rule narrows them.</summary>
    private static OutgoingEmailStage[] RequeueableStages =>
        [OutgoingEmailStage.Recorded, OutgoingEmailStage.TransmissionBegun, OutgoingEmailStage.Refused];

    /// <summary>Applies the filters a query names, leaving the ordering and the page bound to the caller.</summary>
    private IQueryable<OutgoingEmailEntity> Filter(OutboxQuery query)
    {
        var sends = dbContext.OutgoingEmails.AsNoTracking();

        if (query.AccountId is { } accountId)
        {
            var accountValue = accountId.Value;

            sends = sends.Where(message => message.MailboxAccountId == accountValue);
        }

        if (query.Stage is { } stage)
        {
            sends = sends.Where(message => message.Stage == stage);
        }

        // The keyset boundary is the pair the order is taken on, so a send recorded in the same instant as the last one
        // of the previous page is served exactly once rather than skipped or repeated. The identifier comparison is
        // evaluated by PostgreSQL as a `uuid` comparison, so it never has to agree with how the CLR happens to compare
        // two `Guid` values.
        if (query.Cursor is { } cursor)
        {
            var boundaryRecordedAt = cursor.RecordedAt;
            var boundaryId = cursor.OutgoingEmailId.Value;

            sends = sends.Where(message =>
                message.RecordedAt < boundaryRecordedAt
                || (message.RecordedAt == boundaryRecordedAt && message.Id < boundaryId));
        }

        return sends;
    }

    /// <summary>Says why a conditional update wrote nothing, which is the one thing the row count cannot.</summary>
    /// <remarks>
    /// Asked only after a write that changed no row, so the ordinary path costs one statement. The answer may already
    /// be stale by the time it is read — an attempt can claim the record in between — and that is acceptable: every
    /// refusal tells the caller the same thing, which is that this decision was not the one that took effect.
    /// </remarks>
    private async Task<OutboxDecisionOutcome> ExplainRefusalAsync(
        Guid recordId,
        IReadOnlyList<OutgoingEmailStage> allowedStages,
        bool refusalRestated,
        DateTimeOffset decidedAt,
        CancellationToken cancellationToken)
    {
        var row = await dbContext.OutgoingEmails
            .AsNoTracking()
            .Where(message => message.Id == recordId)
            .Select(message => new { message.Stage, message.LeaseExpiresAt })
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return OutboxDecisionOutcome.RecordUnknown;
        }

        // The restatement is reported before the lease, because it is the one refusal the operator can act on by
        // repeating the command differently; a lease is something they wait out rather than something they answer.
        if (!refusalRestated && row.Stage == OutgoingEmailStage.Refused)
        {
            return OutboxDecisionOutcome.RefusalNotRestated;
        }

        if (!allowedStages.Contains(row.Stage))
        {
            return OutboxDecisionOutcome.StageDoesNotAllowIt;
        }

        return row.LeaseExpiresAt is { } expiresAt && expiresAt > decidedAt
            ? OutboxDecisionOutcome.AttemptUnderWay
            : OutboxDecisionOutcome.StageDoesNotAllowIt;
    }

    /// <summary>Rebuilds the listing entry one projected row describes.</summary>
    private static OutboxEntry ToEntry(OutboxRow row) => new()
    {
        OutgoingEmailId = OutgoingEmailId.Create(row.Id),
        AccountId = MailAccountId.Create(row.MailboxAccountId),
        Stage = row.Stage,
        Origin = row.RequesterOrigin,
        AttemptCount = row.AttemptCount,
        MimeByteLength = row.MimeByteLength,
        RecordedAt = row.RecordedAt,
        StageChangedAt = row.StageChangedAt,
        AvailableAt = row.AvailableAt,
        LastFailure = ToFailure(row.LastFailureCode),
        LastReplyCode = row.LastReplyCode,
    };

    /// <summary>Reads back the code of the failure the last attempt ended in.</summary>
    /// <remarks>
    /// A number this build does not recognize is a row written by one that allocated a code since. It is diagnostic
    /// detail rather than something acted on, so it is reported as absent instead of failing the read of a page that is
    /// otherwise perfectly readable — which is the same rule the record mapping follows.
    /// </remarks>
    private static MailFathomErrorCode? ToFailure(int? storedCode)
    {
        if (storedCode is not { } failureCode)
        {
            return null;
        }

        return MailFathomErrorCode.TryParse(failureCode, out var failure) ? failure : null;
    }

    /// <summary>The columns one listing row is read from, which are the ones that name no person.</summary>
    private sealed record OutboxRow(
        Guid Id,
        string MailboxAccountId,
        OutgoingEmailStage Stage,
        OutgoingEmailOrigin RequesterOrigin,
        int AttemptCount,
        long MimeByteLength,
        DateTimeOffset RecordedAt,
        DateTimeOffset StageChangedAt,
        DateTimeOffset AvailableAt,
        int? LastFailureCode,
        int? LastReplyCode);
}
