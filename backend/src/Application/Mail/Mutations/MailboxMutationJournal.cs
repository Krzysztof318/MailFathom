// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Mutations.Audit;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;
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
/// <para>
/// Being that single writer is also why the audit trail is appended from here rather than from the callers. The two
/// terminal stages are reached from four places between the performer and convergence, and an entry owed by one of them
/// and not written by another would be a history whose gaps mean nothing. The append happens after the terminal stage is
/// durable and cannot fail the mutation, which <see cref="IMailboxMutationAuditTrail" /> states in full.
/// </para>
/// </remarks>
internal sealed class MailboxMutationJournal : IMailboxMutationJournal
{
    private readonly IMailboxMutationRecordStore store;
    private readonly OptimisticConcurrencyRetryPolicy commitPolicy;
    private readonly IMailboxMutationAuditTrail auditTrail;
    private readonly MailFolderResolution sourceFolder;
    private MailboxMutationRecord record;

    internal MailboxMutationJournal(
        IMailboxMutationRecordStore store,
        OptimisticConcurrencyRetryPolicy commitPolicy,
        IMailboxMutationAuditTrail auditTrail,
        MailboxMutationRecord record,
        MailFolderResolution sourceFolder)
    {
        this.store = store;
        this.commitPolicy = commitPolicy;
        this.auditTrail = auditTrail;
        this.record = record;
        this.sourceFolder = sourceFolder;
    }

    /// <summary>Gets the record every write here names.</summary>
    internal MailboxMutationRecordId RecordId => this.record.Id;

    /// <inheritdoc />
    public MailboxMutationStage Stage => this.record.Stage;

    /// <inheritdoc />
    public RemoteEmailPlacement Placement => this.record.Placement;

    /// <inheritdoc />
    public bool RequiresSourceRemoval => this.record.RequiresSourceRemoval;

    /// <summary>Gets how many attempts the record has counted, including one this journal has just counted.</summary>
    internal int AttemptCount => this.record.AttemptCount;

    /// <summary>Gets the failure the last attempt ended in, or <see langword="null" /> while none has.</summary>
    internal MailFathomErrorCode? LastFailure => this.record.LastFailure;

    /// <inheritdoc />
    public async Task PlacementIssuedAsync(bool requiresSourceRemoval, CancellationToken cancellationToken)
    {
        await this.commitPolicy.CommitAsync(
            (session, token) => this.store.RecordPlacementIssuedAsync(
                session,
                this.RecordId,
                requiresSourceRemoval,
                token),
            cancellationToken);

        this.record = this.record with
        {
            Stage = MailboxMutationStage.PlacementIssued,
            RequiresSourceRemoval = requiresSourceRemoval,
        };
    }

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
        var countedAttempts = 0;

        await this.commitPolicy.CommitAsync(
            async (session, token) =>
            {
                countedAttempts = await this.store.CountAttemptAsync(session, this.RecordId, token);
            },
            cancellationToken);

        this.record = this.record with { AttemptCount = countedAttempts };
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

        this.record = this.record with { LastFailure = failure };
    }

    private async Task AdvanceAsync(
        MailboxMutationStage stage,
        RemoteEmailPlacement? placement,
        CancellationToken cancellationToken)
    {
        await this.commitPolicy.CommitAsync(
            (session, token) => this.store.AdvanceAsync(session, this.RecordId, stage, placement, token),
            cancellationToken);

        this.record = this.record with
        {
            Stage = stage,
            Placement = placement ?? this.record.Placement,
        };

        if (this.record.IsTerminal)
        {
            await this.auditTrail.RecordAsync(this.record, this.sourceFolder, cancellationToken);
        }
    }
}
