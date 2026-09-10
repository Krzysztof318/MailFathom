// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Persistence;
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
/// </remarks>
public sealed class MailDeletionRecorder
{
    private readonly AccessAuthorization authorization;
    private readonly MailboxScopeResolver scopeResolver;
    private readonly IAuthoredMailboxTargetReader targets;
    private readonly IAuthoredDeleteEmailDispositionReader deleteDispositions;
    private readonly IMailboxMutationRecordStore records;
    private readonly OptimisticConcurrencyRetryPolicy commitPolicy;

    /// <summary>Initializes the use case over the grant it asks first, the email it is about, and the record it writes.</summary>
    /// <param name="authorization">Answers which principal reached this use case.</param>
    /// <param name="scopeResolver">Answers whether a caller may reach the folder the email is in.</param>
    /// <param name="targets">Answers where the named email currently is.</param>
    /// <param name="deleteDispositions">Answers what the account keeps locally of mail the server has let go of.</param>
    /// <param name="records">Opens the durable record the delete is carried by.</param>
    /// <param name="commitPolicy">Commits the record, retrying an optimistic conflict.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required collaborator is <see langword="null" />.</exception>
    public MailDeletionRecorder(
        AccessAuthorization authorization,
        MailboxScopeResolver scopeResolver,
        IAuthoredMailboxTargetReader targets,
        IAuthoredDeleteEmailDispositionReader deleteDispositions,
        IMailboxMutationRecordStore records,
        OptimisticConcurrencyRetryPolicy commitPolicy)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(scopeResolver);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(deleteDispositions);
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(commitPolicy);

        this.authorization = authorization;
        this.scopeResolver = scopeResolver;
        this.targets = targets;
        this.deleteDispositions = deleteDispositions;
        this.records = records;
        this.commitPolicy = commitPolicy;
    }

    /// <summary>Writes down one delete, against the email a caller named.</summary>
    /// <param name="storedEmailId">The email to delete, as a listing, a search, or a read returned it.</param>
    /// <param name="requester">The invocation asking, which is what decides whether asking again is the same request.</param>
    /// <param name="cancellationToken">Cancels the resolution and the write.</param>
    /// <returns>The record that was opened, or the reason none was.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="requester" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold the deleting grant.</exception>
    public async Task<AuthoredMailDeletionResult> RecordAsync(
        StoredEmailId storedEmailId,
        MailboxMutationRequester requester,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requester);

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

        var record = await this.commitPolicy.CommitAsync(
            (session, attemptCancellationToken) => this.records.OpenAsync(session, request, attemptCancellationToken),
            cancellationToken);

        return AuthoredMailDeletionResult.Recorded(record.Id, record.Lifecycle);
    }
}
