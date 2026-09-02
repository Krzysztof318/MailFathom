// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Mail.Mutations.Destinations;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;

namespace MailFathom.Application.Mail.Mutations.Authoring;

/// <summary>Writes down the folder moves a caller asks for, as the mutation records every requester uses.</summary>
/// <remarks>
/// <para>
/// It is the relocation half of what
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0007-remote-mailbox-mutation-boundary-and-write-session.md">ADR 0007</see>
/// permits a caller to author, and it is built exactly as <see cref="MailFlagChangeRecorder" /> is and for the same
/// reasons: it asks for the grant before it asks about the email, it answers the same question about which mail may be
/// written as about which mail may be read, and it issues no IMAP command at all. The account's own convergence pass
/// carries the record to a completed or a dead-lettered ending, which is what keeps a move crash-safe — the sequence a
/// server without <c>MOVE</c> forces is a copy and a delete, and a record is what stops a crash between the two from
/// losing the message.
/// </para>
/// <para>
/// The grant is its own, and is not the one that writes flags. A flag misdescribes mail the owner can still find; a
/// move puts the mail somewhere else, which is why <see cref="MailFathomPermission.MailMove" /> is a name a deployment
/// grants separately.
/// </para>
/// <para>
/// The destination is judged by the same visibility rule as the source. A folder an operator withheld from this caller
/// is no more a destination than it is a source, because filing mail into a folder the caller cannot read would be a
/// way of moving mail out of sight rather than a capability of its own.
/// </para>
/// <para>
/// Every refusal is a result rather than an exception, because a caller moving several messages at once acts on each
/// answer and carries on with the rest. The one failure that is not about a message is: a caller without the grant
/// gets no answer about any of them, which is the whole request's outcome rather than one item's.
/// </para>
/// </remarks>
public sealed class MailRelocationRecorder
{
    private readonly AccessAuthorization authorization;
    private readonly MailboxScopeResolver scopeResolver;
    private readonly IAuthoredMailboxTargetReader targets;
    private readonly MailboxDestinationResolver destinations;
    private readonly IAuthoredDeleteEmailDispositionReader deleteDispositions;
    private readonly IMailboxMutationRecordStore records;
    private readonly OptimisticConcurrencyRetryPolicy commitPolicy;

    /// <summary>Initializes the use case over the grant it asks first, the folder it files into, and the record it writes.</summary>
    /// <param name="authorization">Answers which principal reached this use case.</param>
    /// <param name="scopeResolver">Answers whether a caller may reach the folder an email is in and the folder it is going to.</param>
    /// <param name="targets">Answers where the named email currently is.</param>
    /// <param name="destinations">Turns the folder a caller named into the folder on the server it currently means.</param>
    /// <param name="deleteDispositions">Answers what the account keeps locally of mail that leaves the mirror for good.</param>
    /// <param name="records">Opens the durable record the move is carried by.</param>
    /// <param name="commitPolicy">Commits the record, retrying an optimistic conflict.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required collaborator is <see langword="null" />.</exception>
    public MailRelocationRecorder(
        AccessAuthorization authorization,
        MailboxScopeResolver scopeResolver,
        IAuthoredMailboxTargetReader targets,
        MailboxDestinationResolver destinations,
        IAuthoredDeleteEmailDispositionReader deleteDispositions,
        IMailboxMutationRecordStore records,
        OptimisticConcurrencyRetryPolicy commitPolicy)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(scopeResolver);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(destinations);
        ArgumentNullException.ThrowIfNull(deleteDispositions);
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(commitPolicy);

        this.authorization = authorization;
        this.scopeResolver = scopeResolver;
        this.targets = targets;
        this.destinations = destinations;
        this.deleteDispositions = deleteDispositions;
        this.records = records;
        this.commitPolicy = commitPolicy;
    }

    /// <summary>Writes down one move, against the email and the folder a caller named.</summary>
    /// <param name="storedEmailId">The email to move, as a listing, a search, or a read returned it.</param>
    /// <param name="destination">MailFathom's own name for the folder to move it into.</param>
    /// <param name="requester">The invocation asking, which is what decides whether asking again is the same request.</param>
    /// <param name="cancellationToken">Cancels the resolution and the write.</param>
    /// <returns>The record that was opened, or the reason none was.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="requester" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="destination" /> names no folder.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold the moving grant.</exception>
    /// <remarks>
    /// The destination is resolved before the commit is opened, because resolving a folder the account maps and does not
    /// mirror reaches the mail server, and a transaction must never be held open across one.
    /// </remarks>
    public async Task<AuthoredMailRelocationResult> RecordAsync(
        StoredEmailId storedEmailId,
        MailFolderAlias destination,
        MailboxMutationRequester requester,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requester);

        if (string.IsNullOrEmpty(destination.Value))
        {
            throw new ArgumentException("A move names the folder it files into.", nameof(destination));
        }

        this.authorization.RequirePermission(MailFathomPermission.MailMove);

        var target = await this.targets.FindAsync(storedEmailId, cancellationToken);

        // The two withheld folders are reported as the two absences they are. A source this caller may not read is a
        // message it may not learn about at all, and a destination it may not read is a folder it may not learn about,
        // so neither answer says anything about the other.
        if (target is null || !this.scopeResolver.IsReadableByTools(target.Occurrence.AccountId, target.Folder.Alias))
        {
            return AuthoredMailRelocationResult.NotRecorded(MailRelocationOutcome.MessageNotFound);
        }

        if (!this.scopeResolver.IsReadableByTools(target.Occurrence.AccountId, destination))
        {
            return AuthoredMailRelocationResult.NotRecorded(MailRelocationOutcome.DestinationNotFound);
        }

        var account = MailAccountIdentity.Create(target.Owner, target.Occurrence.AccountId);
        var reference = MailFolderReference.ToAlias(destination);
        var resolved = await this.destinations.ResolveAsync(account, [reference], cancellationToken);

        if (resolved.Find(reference).Destination is not { } folder)
        {
            return AuthoredMailRelocationResult.NotRecorded(MailRelocationOutcome.DestinationNotFound);
        }

        // Asked after the folder is known, because the alias a caller wrote and the folder it currently names are two
        // different things: a caller may name the alias the message is already filed under by another spelling of it.
        if (folder.Alias == target.Folder.Alias)
        {
            return AuthoredMailRelocationResult.NotRecorded(MailRelocationOutcome.AlreadyInDestination);
        }

        AuthoredDeleteEmailDisposition? localDisposition;

        try
        {
            localDisposition = folder.IsMirrored
                ? null
                : this.deleteDispositions.GetAuthoredDeleteDisposition(target.Occurrence.AccountId);
        }
        catch (InvalidOperationException)
        {
            return AuthoredMailRelocationResult.NotRecorded(MailRelocationOutcome.AccountNoLongerConfigured);
        }

        var request = MailboxMutationRequest.Relocate(
            storedEmailId,
            target.Owner,
            target.Occurrence,
            requester,
            folder.Path,
            localDisposition);

        var record = await this.commitPolicy.CommitAsync(
            (session, attemptCancellationToken) => this.records.OpenAsync(session, request, attemptCancellationToken),
            cancellationToken);

        return AuthoredMailRelocationResult.Recorded(folder.Alias, record.Id, record.Lifecycle);
    }
}
