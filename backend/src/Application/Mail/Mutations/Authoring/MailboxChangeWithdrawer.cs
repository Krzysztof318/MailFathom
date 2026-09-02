// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Mutations;

namespace MailFathom.Application.Mail.Mutations.Authoring;

/// <summary>Withdraws changes a caller authored and no longer wants, while nothing has been asked of the mail server.</summary>
/// <remarks>
/// <para>
/// A change against an account nothing can reach stays pending for as long as the account stays unreachable, which is
/// the design rather than a defect: the change is kept so that it happens when the account comes back. What that leaves
/// a person needing is a way to say they have changed their mind, and this is it — the record moves to the stage that
/// says so, no command is ever issued for it, and the account's next pass does not see it at all.
/// </para>
/// <para>
/// It withdraws nothing that reached the server. A <c>STORE</c> already issued cannot be recalled, and a placement whose
/// answer never came back is the one outcome that must be re-established rather than declared void — so a record past
/// the stage it was written down at is reported where it stands instead of being refused, which is also what makes the
/// call safe to repeat.
/// </para>
/// <para>
/// The grant that authored a change is the grant that withdraws it, which is why there are two entry points rather than
/// one taking the caller's word for which surface it is. Withdrawing causes no mailbox change and cannot: the worst it
/// does is stop one, so what it needs is authority over the same kind of change rather than authority of its own.
/// </para>
/// </remarks>
public sealed class MailboxChangeWithdrawer
{
    /// <summary>The greatest number of records one call may withdraw.</summary>
    /// <remarks>The bound the submitting routes put on a batch, so everything one call authored can be withdrawn by one call.</remarks>
    public const int MaximumRecordsPerCall = 200;

    private readonly AccessAuthorization authorization;
    private readonly MailboxScopeResolver scopeResolver;
    private readonly IMailboxMutationRecordStore records;
    private readonly OptimisticConcurrencyRetryPolicy commitPolicy;

    /// <summary>Initializes the use case over the grant it asks first and the records it withdraws.</summary>
    /// <param name="authorization">Answers which principal reached this use case.</param>
    /// <param name="scopeResolver">Answers whose records these are and which folders the caller may reach.</param>
    /// <param name="records">Reads and withdraws the durable records.</param>
    /// <param name="commitPolicy">Commits a call's withdrawals together, retrying an optimistic conflict.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required collaborator is <see langword="null" />.</exception>
    public MailboxChangeWithdrawer(
        AccessAuthorization authorization,
        MailboxScopeResolver scopeResolver,
        IMailboxMutationRecordStore records,
        OptimisticConcurrencyRetryPolicy commitPolicy)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(scopeResolver);
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(commitPolicy);

        this.authorization = authorization;
        this.scopeResolver = scopeResolver;
        this.records = records;
        this.commitPolicy = commitPolicy;
    }

    /// <summary>Withdraws flag and keyword changes this caller authored.</summary>
    /// <param name="recordIds">The records to withdraw.</param>
    /// <param name="cancellationToken">Cancels the read and the commit.</param>
    /// <returns>One entry per record this caller holds under those identities, each reporting where it now stands.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="recordIds" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when more records are named than one call may withdraw.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold the flag-writing grant.</exception>
    /// <remarks>A record naming a move is absent from the answer, exactly as a record belonging to somebody else is: this entry point holds authority over flag changes and says nothing about anything else.</remarks>
    public Task<IReadOnlyList<MailboxChangeProgress>> WithdrawFlagChangesAsync(
        IReadOnlyList<MailboxMutationRecordId> recordIds,
        CancellationToken cancellationToken) => this.WithdrawAsync(
            recordIds,
            MailFathomPermission.MailFlagsWrite,
            MailboxMutation.FlagWriting,
            cancellationToken);

    /// <summary>Withdraws folder moves this caller authored.</summary>
    /// <param name="recordIds">The records to withdraw.</param>
    /// <param name="cancellationToken">Cancels the read and the commit.</param>
    /// <returns>One entry per record this caller holds under those identities, each reporting where it now stands.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="recordIds" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when more records are named than one call may withdraw.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold the moving grant.</exception>
    /// <remarks>A record naming a flag change is absent from the answer, for the reason the flag entry point gives about a move.</remarks>
    public Task<IReadOnlyList<MailboxChangeProgress>> WithdrawMovesAsync(
        IReadOnlyList<MailboxMutationRecordId> recordIds,
        CancellationToken cancellationToken) => this.WithdrawAsync(
            recordIds,
            MailFathomPermission.MailMove,
            [MailboxMutation.Relocate],
            cancellationToken);

    /// <summary>Withdraws every named record that this caller holds, may reach, and this grant covers.</summary>
    /// <remarks>
    /// The records are read first and withdrawn in one commit, so a call either withdraws everything it was going to or
    /// nothing: a caller told its call failed while half its changes had already been stopped would have no way of
    /// finding out which half.
    /// </remarks>
    private async Task<IReadOnlyList<MailboxChangeProgress>> WithdrawAsync(
        IReadOnlyList<MailboxMutationRecordId> recordIds,
        MailFathomPermission grant,
        IReadOnlyList<MailboxMutation> covered,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recordIds);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(recordIds.Count, MaximumRecordsPerCall);

        this.authorization.RequirePermission(grant);

        var held = await this.records.ReadAsync(this.scopeResolver.Owner, recordIds, cancellationToken);

        var withdrawable = held
            .Where(record => covered.Contains(record.Request.Mutation) && this.IsReadable(record))
            .Select(record => record.Id)
            .ToArray();

        if (withdrawable.Length == 0)
        {
            return [];
        }

        IReadOnlyList<MailboxChangeProgress> withdrawn = [];

        await this.commitPolicy.CommitAsync(
            async (session, attemptCancellationToken) =>
            {
                // Assigned rather than appended to, because a retried commit reads every record again and would
                // otherwise report the losing attempt's answers beside the winning one's.
                var records = await this.records.WithdrawAsync(
                    session,
                    this.scopeResolver.Owner,
                    withdrawable,
                    attemptCancellationToken);

                withdrawn = [.. records.Select(MailboxChangeProgress.Of)];
            },
            cancellationToken);

        return withdrawn;
    }

    /// <summary>Reports whether the caller may still reach the mailbox the change was recorded in.</summary>
    private bool IsReadable(MailboxMutationRecord record) => this.scopeResolver.IsReadableByTools(
        record.Request.Occurrence.AccountId,
        record.Request.Occurrence.FolderResolutionId.Alias);
}
