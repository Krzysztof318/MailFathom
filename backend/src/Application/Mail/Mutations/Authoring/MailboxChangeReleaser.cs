// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Access;
using MailFathom.Domain.Mutations;

namespace MailFathom.Application.Mail.Mutations.Authoring;

/// <summary>Ends the wait a change was opened under, for a caller that has stopped offering the way back.</summary>
/// <remarks>
/// <para>
/// A delete a client authors is written down with a window in front of it, so the person who answered the question can
/// still take it back. The window is theirs rather than the deployment's, and it ends in one of two ways: they withdraw
/// the change, which is <see cref="MailboxChangeWithdrawer" />, or the notification offering that goes and the client
/// says so — which is this. Saying so is what turns the seconds a person was given into seconds the mailbox actually
/// waited rather than a delay every delete pays whether or not anybody was watching.
/// </para>
/// <para>
/// <b>Nothing depends on being told.</b> A window elapses on its own, and the record is taken in hand by the account's
/// ordinary pass exactly as if this had been called — so a closed tab, a lost network, or a machine put to sleep costs
/// the mailbox nothing. That is what makes this an optimization of the waiting rather than a step in the delete, and it
/// is why asking twice, or asking after the window has already passed, is the same as asking once.
/// </para>
/// <para>
/// It carries the grant that authored the change, for the reason the withdrawal routes carry theirs: releasing performs
/// no mailbox change of its own, it only stops one being deferred, so what it needs is authority over the same kind of
/// change. Only deletes are ever held today, which is why the one entry point names them.
/// </para>
/// </remarks>
public sealed class MailboxChangeReleaser
{
    /// <summary>The greatest number of records one call may release.</summary>
    /// <remarks>The bound the submitting routes put on a batch, so everything one call authored can be released by one call.</remarks>
    public const int MaximumRecordsPerCall = MailboxChangeWithdrawer.MaximumRecordsPerCall;

    private readonly AccessAuthorization authorization;
    private readonly MailboxScopeResolver scopeResolver;
    private readonly IMailboxMutationRecordStore records;
    private readonly OptimisticConcurrencyRetryPolicy commitPolicy;
    private readonly MailAccountRunSignal runSignal;

    /// <summary>Initializes the use case over the grant it asks first, the records it releases, and the run that carries them.</summary>
    /// <param name="authorization">Answers which principal reached this use case.</param>
    /// <param name="scopeResolver">Answers whose records these are and which folders the caller may reach.</param>
    /// <param name="records">Reads the durable records and lifts the hold on them.</param>
    /// <param name="commitPolicy">Commits a call's releases together, retrying an optimistic conflict.</param>
    /// <param name="runSignal">Brings each released record's account run forward, which is what carries the change to the mail server.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required collaborator is <see langword="null" />.</exception>
    public MailboxChangeReleaser(
        AccessAuthorization authorization,
        MailboxScopeResolver scopeResolver,
        IMailboxMutationRecordStore records,
        OptimisticConcurrencyRetryPolicy commitPolicy,
        MailAccountRunSignal runSignal)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(scopeResolver);
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(commitPolicy);
        ArgumentNullException.ThrowIfNull(runSignal);

        this.authorization = authorization;
        this.scopeResolver = scopeResolver;
        this.records = records;
        this.commitPolicy = commitPolicy;
        this.runSignal = runSignal;
    }

    /// <summary>Ends the wait in front of the deletes this caller authored, so the next pass may take each in hand.</summary>
    /// <param name="recordIds">The records to release.</param>
    /// <param name="cancellationToken">Cancels the read and the commit.</param>
    /// <returns>One entry per record this caller holds under those identities, each reporting where it now stands.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="recordIds" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when more records are named than one call may release.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold the deleting grant.</exception>
    /// <remarks>A record naming anything but a delete is absent from the answer, exactly as a record belonging to somebody else is: this entry point holds authority over deletes and says nothing about anything else.</remarks>
    public async Task<IReadOnlyList<MailboxChangeProgress>> ReleaseDeletesAsync(
        IReadOnlyList<MailboxMutationRecordId> recordIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recordIds);

        // Asked before the batch is measured, so a caller holding nothing is refused as unauthorized rather than told
        // how large its batch was — the order MailboxChangeWithdrawer holds, for the same reason.
        this.authorization.RequirePermission(MailFathomPermission.MailDelete);

        ArgumentOutOfRangeException.ThrowIfGreaterThan(recordIds.Count, MaximumRecordsPerCall);

        var held = await this.records.ReadAsync(this.scopeResolver.User, recordIds, cancellationToken);

        var releasable = held
            .Where(record => record.Request.Mutation == MailboxMutation.Delete && this.IsReadable(record))
            .Select(record => record.Id)
            .ToArray();

        if (releasable.Length == 0)
        {
            return [];
        }

        IReadOnlyList<MailboxMutationRecord> released = [];

        await this.commitPolicy.CommitAsync(
            async (session, attemptCancellationToken) =>
            {
                // Assigned rather than appended to, for the reason the withdrawer gives about its own: a retried commit
                // reads every record again and would otherwise report the losing attempt's answers beside the winner's.
                released = await this.records.ReleaseAsync(
                    session,
                    this.scopeResolver.User,
                    releasable,
                    attemptCancellationToken);
            },
            cancellationToken);

        // Raised once the release is durable, exactly as the recorder raises its own: the run reads the records rather
        // than the raise, so a signal sent before the commit could reach a pass that still sees the record held.
        foreach (var account in released
            .Where(record => record.Stage == MailboxMutationStage.Recorded)
            .Select(record => record.Request.Occurrence.AccountId)
            .Distinct())
        {
            this.runSignal.BringForward(account);
        }

        return [.. released.Select(MailboxChangeProgress.Of)];
    }

    /// <summary>Reports whether the caller may still reach the mailbox the change was recorded in.</summary>
    private bool IsReadable(MailboxMutationRecord record) => this.scopeResolver.IsReadableByTools(
        record.Request.Occurrence.AccountId,
        record.Request.Occurrence.FolderResolutionId.Alias);
}
