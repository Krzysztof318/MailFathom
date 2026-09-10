// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Mutations;

namespace MailFathom.Application.Mail.Mutations.Authoring;

/// <summary>Writes down the deletes a caller asks for, as the mutation records every requester uses.</summary>
/// <remarks>
/// <para>
/// It is the deleting half of what
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0007-remote-mailbox-mutation-boundary-and-write-session.md">ADR 0007</see>
/// permits a caller to author, and it is built exactly as <see cref="MailRelocationRecorder" /> is: it asks for the
/// grant before it asks about the email, it answers the same question about which mail may be written as about which
/// mail may be read, and it issues no IMAP command at all. The account's own convergence pass expunges the remote
/// occurrence, which is why an unreachable account leaves the delete pending rather than failing it.
/// </para>
/// <para>
/// The grant is its own, and is not the one that moves mail. Filing a message in the trash puts it somewhere the user
/// has to look for it; this destroys the remote occurrence, so <see cref="MailFathomPermission.MailDelete" /> is a name
/// a deployment grants separately from <see cref="MailFathomPermission.MailMove" />.
/// </para>
/// <para>
/// What becomes of MailFathom's own copy is the account's configured answer rather than anything a caller states.
/// Reading it here rather than where the delete completes is what makes it the answer that was true when somebody
/// asked: completion happens in a later convergence pass that would otherwise read whatever the configuration says by
/// then.
/// </para>
/// <para>
/// Which folder the message is in decides nothing here. That a client offers the act only in the trash is a sentence on
/// a screen rather than a rule of the deployment's — the grant is what an operator withholds from a credential that may
/// not destroy mail, and a rule action already authors the same delete from wherever the message happens to be.
/// </para>
/// <para>
/// <b>A caller may ask for the record to wait before anything is asked of the mail server.</b> Deleting is the one act
/// with nothing to reverse it, so a question in front of it is all a person gets — and a question is answered before
/// anybody has seen what happened. A withdrawal window is the way back that a confirmation is not: the record opens as
/// every other does and is simply not taken in hand until the window has passed, which is what the two withdrawal
/// routes already act on. Whether there is one, and how long it is, is the asking surface's — a client says how long a
/// person's own notification stands, and a rule action or an MCP caller has nobody watching and asks for none.
/// </para>
/// </remarks>
public sealed class MailDeletionRecorder
{
    private readonly AccessAuthorization authorization;
    private readonly MailboxScopeResolver scopeResolver;
    private readonly IAuthoredMailboxTargetReader targets;
    private readonly IAuthoredDeleteEmailDispositionReader deleteDispositions;
    private readonly IMailboxMutationRecordStore records;
    private readonly OptimisticConcurrencyRetryPolicy commitPolicy;
    private readonly MailAccountRunSignal runSignal;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the use case over the grant it asks first, the email it is about, and the record it writes.</summary>
    /// <param name="authorization">Answers which principal reached this use case.</param>
    /// <param name="scopeResolver">Answers whether a caller may reach the folder the email is in.</param>
    /// <param name="targets">Answers where the named email currently is.</param>
    /// <param name="deleteDispositions">Answers what the account keeps locally of mail the server has let go of.</param>
    /// <param name="records">Opens the durable record the delete is carried by.</param>
    /// <param name="commitPolicy">Commits the record, retrying an optimistic conflict.</param>
    /// <param name="runSignal">Brings the account's next synchronization run forward, which is what carries the delete to the mail server.</param>
    /// <param name="timeProvider">Measures the withdrawal window a caller asks for, from the moment the record is written.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required collaborator is <see langword="null" />.</exception>
    public MailDeletionRecorder(
        AccessAuthorization authorization,
        MailboxScopeResolver scopeResolver,
        IAuthoredMailboxTargetReader targets,
        IAuthoredDeleteEmailDispositionReader deleteDispositions,
        IMailboxMutationRecordStore records,
        OptimisticConcurrencyRetryPolicy commitPolicy,
        MailAccountRunSignal runSignal,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(scopeResolver);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(deleteDispositions);
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(commitPolicy);
        ArgumentNullException.ThrowIfNull(runSignal);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.authorization = authorization;
        this.scopeResolver = scopeResolver;
        this.targets = targets;
        this.deleteDispositions = deleteDispositions;
        this.records = records;
        this.commitPolicy = commitPolicy;
        this.runSignal = runSignal;
        this.timeProvider = timeProvider;
    }

    /// <summary>Writes down one delete, against the email a caller named.</summary>
    /// <param name="storedEmailId">The email to delete, as a listing, a search, or a read returned it.</param>
    /// <param name="requester">The invocation asking, which is what decides whether asking again is the same request.</param>
    /// <param name="withdrawalWindow">How long the record waits before a convergence pass may take it in hand, or <see langword="null" /> where nothing waits.</param>
    /// <param name="cancellationToken">Cancels the resolution and the write.</param>
    /// <returns>The record that was opened, or the reason none was.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="requester" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="withdrawalWindow" /> is negative.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold the deleting grant.</exception>
    public async Task<AuthoredMailDeletionResult> RecordAsync(
        StoredEmailId storedEmailId,
        MailboxMutationRequester requester,
        TimeSpan? withdrawalWindow,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requester);

        if (withdrawalWindow is { } window)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(window, TimeSpan.Zero, nameof(withdrawalWindow));
        }

        // Asked before the email is read at all, so a caller that holds nothing is refused as unauthorized rather than
        // told the message it named is not there — which would be an answer about somebody else's mailbox.
        this.authorization.RequirePermission(MailFathomPermission.MailDelete);

        var target = await this.targets.FindAsync(storedEmailId, cancellationToken);

        if (target is null || !this.scopeResolver.IsReadableByTools(target.Occurrence.AccountId, target.Folder.Alias))
        {
            return AuthoredMailDeletionResult.NotRecorded(MailDeletionOutcome.MessageNotFound);
        }

        AuthoredDeleteEmailDisposition localDisposition;

        try
        {
            localDisposition = this.deleteDispositions.GetAuthoredDeleteDisposition(target.Occurrence.AccountId);
        }
        catch (InvalidOperationException)
        {
            return AuthoredMailDeletionResult.NotRecorded(MailDeletionOutcome.AccountNoLongerConfigured);
        }

        var request = MailboxMutationRequest.Delete(
            storedEmailId,
            target.User,
            target.Occurrence,
            requester,
            localDisposition);

        var heldUntil = withdrawalWindow is { } granted ? this.timeProvider.GetUtcNow() + granted : (DateTimeOffset?)null;

        var record = await this.commitPolicy.CommitAsync(
            (session, attemptCancellationToken) => this.records.OpenAsync(
                session,
                request,
                heldUntil,
                attemptCancellationToken),
            cancellationToken);

        if (heldUntil is null)
        {
            // Raised once the record is durable, for the reason MailFlagChangeRecorder gives: the run reads the records
            // rather than the raise. A delete waits on the convergence pass exactly as a move does, so it waits on the
            // same raise.
            //
            // A held record is the one case where it is not raised at all. Nothing a run could do with it has become
            // due, so bringing one forward would spend a pass on a record it must leave where it is; what carries a
            // held delete to the mail server is the account's ordinary schedule once the window has elapsed, or the
            // release that lifts the hold and raises this itself.
            this.runSignal.BringForward(target.Occurrence.AccountId);
        }

        return AuthoredMailDeletionResult.Recorded(record.Id, record.Lifecycle);
    }
}
