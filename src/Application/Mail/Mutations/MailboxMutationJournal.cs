// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Mutations;

namespace MailFathom.Application.Mail.Mutations;

/// <summary>Writes one mutation record's every movement, each in a transaction of its own.</summary>
/// <remarks>
/// <para>
/// It is the single writer of a record after the record exists, which is what keeps the rule that a stage only moves
/// forward in one place. Every write commits before it returns and none of them joins a transaction the caller is
/// holding: a stage remembered only in memory is exactly the stage lost to the crash the record exists to survive, and a
/// transaction held open across a mail server would be the one thing the persistence rules forbid outright.
/// </para>
/// <para>
/// Each write is idempotent from a fresh read, so it goes through the optimistic concurrency policy rather than being
/// attempted once. A concurrent reader of the same account's mutations is ordinary, and losing a race is not a reason to
/// leave a stage unwritten.
/// </para>
/// </remarks>
internal sealed class MailboxMutationJournal : IMailboxMutationJournal
{
    private readonly IMailboxMutationRecordStore store;
    private readonly OptimisticConcurrencyRetryPolicy commitPolicy;

    internal MailboxMutationJournal(
        IMailboxMutationRecordStore store,
        OptimisticConcurrencyRetryPolicy commitPolicy,
        MailboxMutationRecord record)
    {
        this.store = store;
        this.commitPolicy = commitPolicy;
        this.RecordId = record.Id;
        this.Stage = record.Stage;
        this.Placement = record.Placement;
        this.AttemptCount = record.AttemptCount;
        this.LastFailure = record.LastFailure;
    }

    /// <summary>Gets the record every write here names.</summary>
    internal MailboxMutationRecordId RecordId { get; }

    /// <inheritdoc />
    public MailboxMutationStage Stage { get; private set; }

    /// <inheritdoc />
    public RemoteEmailPlacement Placement { get; private set; }

    /// <summary>Gets how many attempts the record has counted, including one this journal has just counted.</summary>
    internal int AttemptCount { get; private set; }

    /// <summary>Gets the failure the last attempt ended in, or <see langword="null" /> while none has.</summary>
    internal MailFathomErrorCode? LastFailure { get; private set; }

    /// <inheritdoc />
    public Task PlacementIssuedAsync(CancellationToken cancellationToken) =>
        this.AdvanceAsync(MailboxMutationStage.PlacementIssued, placement: null, cancellationToken);

    /// <inheritdoc />
    public Task PlacementConfirmedAsync(RemoteEmailPlacement placement, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(placement);

        return this.AdvanceAsync(MailboxMutationStage.PlacementConfirmed, placement, cancellationToken);
    }

    /// <inheritdoc />
    public Task SourceFlaggedDeletedAsync(CancellationToken cancellationToken) =>
        this.AdvanceAsync(MailboxMutationStage.SourceFlaggedDeleted, placement: null, cancellationToken);

    /// <summary>Counts one attempt before it is made, so an attempt that never returns still counted.</summary>
    internal async Task CountAttemptAsync(CancellationToken cancellationToken)
    {
        await this.commitPolicy.CommitAsync(
            async (session, token) =>
            {
                this.AttemptCount = await this.store.CountAttemptAsync(session, this.RecordId, token);
            },
            cancellationToken);
    }

    /// <summary>Ends the record in the stage that says the change was made.</summary>
    internal Task CompleteAsync(RemoteEmailPlacement placement, CancellationToken cancellationToken) =>
        this.AdvanceAsync(MailboxMutationStage.Completed, placement, cancellationToken);

    /// <summary>Ends the record in the stage that says nothing will attempt the change again.</summary>
    internal async Task AbandonAsync(MailFathomErrorCode failure, CancellationToken cancellationToken)
    {
        await this.RecordFailureAsync(failure, cancellationToken);
        await this.AdvanceAsync(MailboxMutationStage.Abandoned, placement: null, cancellationToken);
    }

    /// <summary>Records why an attempt ended without moving the stage the next attempt resumes from.</summary>
    internal async Task RecordFailureAsync(MailFathomErrorCode failure, CancellationToken cancellationToken)
    {
        await this.commitPolicy.CommitAsync(
            (session, token) => this.store.RecordFailureAsync(session, this.RecordId, failure, token),
            cancellationToken);

        this.LastFailure = failure;
    }

    private async Task AdvanceAsync(
        MailboxMutationStage stage,
        RemoteEmailPlacement? placement,
        CancellationToken cancellationToken)
    {
        await this.commitPolicy.CommitAsync(
            (session, token) => this.store.AdvanceAsync(session, this.RecordId, stage, placement, token),
            cancellationToken);

        this.Stage = stage;

        if (placement is not null)
        {
            this.Placement = placement;
        }
    }
}
