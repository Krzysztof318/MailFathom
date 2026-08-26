// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Mail.Mutations.Authoring.Failures;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Mutations;

namespace MailFathom.Application.Mail.Mutations.Authoring;

/// <summary>Writes down the flag and keyword changes a caller asks for, as the mutation records every requester uses.</summary>
/// <remarks>
/// <para>
/// This is the join between a caller and a mailbox, and it is deliberately the whole of it. Nothing here issues an IMAP
/// command or holds a type that could: the account's own convergence pass carries each record to a completed or a
/// dead-lettered ending exactly as it carries a change a rule authored, which is what keeps
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0007-remote-mailbox-mutation-boundary-and-write-session.md">ADR 0007</see>'s
/// separation a property of the types rather than a rule somebody has to notice. What this adds is the grant, the
/// visibility rule, the identity, and one record per value asked for.
/// </para>
/// <para>
/// The grant is asked for here rather than only at the transport, so an entrypoint added later cannot change somebody's
/// mailbox by arriving another way. It is asked before the email is looked up, because whether a caller may write at
/// all is not a question about which email it named.
/// </para>
/// <para>
/// Which mail may be written is the same question as which mail may be read, answered by the same resolver: an account
/// this deployment no longer serves and a folder an operator withheld from tools are both mail no tool may reach, and a
/// surface that could write what it may not read would be the way round that withholding. A fourth case joins them and
/// is the one a read does not share: a local copy retained after MailFathom deleted the message, which a listing serves
/// because the mail is readable while the UID it carries names an occurrence the server expunged. All four produce one
/// answer, because telling them apart would let a caller learn which identifiers exist by asking about them.
/// </para>
/// <para>
/// The records for one call are opened in one commit, so a call either writes down everything it asked for or nothing.
/// A partially recorded triage is the outcome worth avoiding: a caller told its call failed while one of the three
/// values is already on its way to the server has no way to find out which.
/// </para>
/// </remarks>
public sealed class MailFlagChangeRecorder
{
    private readonly AccessAuthorization authorization;
    private readonly MailboxScopeResolver scopeResolver;
    private readonly IAuthoredMailboxTargetReader targets;
    private readonly IMailboxMutationRecordStore records;
    private readonly OptimisticConcurrencyRetryPolicy commitPolicy;

    /// <summary>Initializes the use case over the grant it asks first and the record it writes.</summary>
    /// <param name="authorization">Answers which principal reached this use case.</param>
    /// <param name="scopeResolver">Answers whether a tool may reach the mailbox an email was stored from.</param>
    /// <param name="targets">Answers where the named email currently is.</param>
    /// <param name="records">Opens the durable record one change is carried by.</param>
    /// <param name="commitPolicy">Commits a call's records together, retrying an optimistic conflict.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required collaborator is <see langword="null" />.</exception>
    public MailFlagChangeRecorder(
        AccessAuthorization authorization,
        MailboxScopeResolver scopeResolver,
        IAuthoredMailboxTargetReader targets,
        IMailboxMutationRecordStore records,
        OptimisticConcurrencyRetryPolicy commitPolicy)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(scopeResolver);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(commitPolicy);

        this.authorization = authorization;
        this.scopeResolver = scopeResolver;
        this.targets = targets;
        this.records = records;
        this.commitPolicy = commitPolicy;
    }

    /// <summary>Writes down every value one change asks for, against the email it names.</summary>
    /// <param name="change">What the caller asked for.</param>
    /// <param name="requester">The invocation asking, which is what decides whether asking again is the same request.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The record opened for each value, in the order the change states them.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="change" /> or <paramref name="requester" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold the writing grant.</exception>
    /// <exception cref="MailFlagChangeTargetNotFoundException">Thrown when this deployment serves no readable email under that identity, or when the email it serves names a remote occurrence the mail server no longer holds.</exception>
    /// <exception cref="MailFlagChangeInvalidException">Thrown when the requester identity already names one of these mutations on this occurrence with a different value.</exception>
    /// <remarks>
    /// A change asked for twice under one requester identity is one change: the record store admits one record per
    /// occurrence, requester, and mutation, and the second call is answered with the record the first opened. That is
    /// what makes a retried call safe to make, and it is why the identity is the invocation's rather than the change's —
    /// a caller that starred a message, unstarred it, and starred it again has made three requests and means all three.
    /// A second call is a retry only when it asks for what the first asked for, which is why the terms are compared
    /// rather than assumed from the identity.
    /// </remarks>
    public async Task<AuthoredMailFlagChangeResult> RecordAsync(
        AuthoredMailFlagChange change,
        MailboxMutationRequester requester,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(requester);

        this.authorization.RequirePermission(MailFathomPermission.MailFlagsWrite);

        var target = await this.targets.FindAsync(change.StoredEmailId, cancellationToken);

        if (target is null
            || !this.scopeResolver.IsReadableByTools(target.Occurrence.AccountId, target.Folder.Alias))
        {
            throw new MailFlagChangeTargetNotFoundException();
        }

        var requests = change.Mutations()
            .Select(mutation => RequestFor(change, mutation, target, requester))
            .ToArray();

        var opened = new List<RecordedMailFlagMutation>(requests.Length);

        await this.commitPolicy.CommitAsync(
            async (session, attemptCancellationToken) =>
            {
                // Cleared per attempt, because a retried commit re-opens every record and would otherwise report the
                // losing attempt's rows beside the winning one's.
                opened.Clear();

                foreach (var request in requests)
                {
                    var record = await this.records.OpenAsync(session, request, attemptCancellationToken);

                    if (!StatesTheSameChangeAs(record.Request, request))
                    {
                        throw MailFlagChangeInvalidException.RequestIdAlreadyAskedForAnother();
                    }

                    opened.Add(new RecordedMailFlagMutation(request.Mutation, record.Id, record.Lifecycle));
                }
            },
            cancellationToken);

        return new AuthoredMailFlagChangeResult(
            change.StoredEmailId,
            target.Occurrence.AccountId,
            target.Folder.Alias,
            opened);
    }

    /// <summary>Reports whether the record this call was answered with asks for what this call asked for.</summary>
    /// <remarks>
    /// The idempotency identity is the occurrence, the requester, and the mutation, and none of the three carries the
    /// value asked for. That was enough while every requester encoded its terms in its own identity — a rule carries its
    /// revision, a classification its corpus and threshold — but a caller-authored request is identified by text the
    /// caller picked, so starring and then unstarring one message under one identity would be answered twice with the
    /// first record and the star would never come off. Comparing the terms is what turns that into a refusal the caller
    /// can act on rather than a success that changed nothing.
    /// </remarks>
    private static bool StatesTheSameChangeAs(MailboxMutationRequest recorded, MailboxMutationRequest asked) =>
        recorded.DesiredSeenState == asked.DesiredSeenState
        && recorded.DesiredFlaggedState == asked.DesiredFlaggedState
        && recorded.Keywords == asked.Keywords;

    /// <summary>Builds the durable request one value of a change is written down as.</summary>
    /// <remarks>
    /// The parameters are already the ones that mutation needs, because the change decided which value produced which
    /// mutation; the factories restate the pairing as their own invariant, which is where it is enforced.
    /// </remarks>
    private static MailboxMutationRequest RequestFor(
        AuthoredMailFlagChange change,
        AuthoredMailFlagMutation mutation,
        AuthoredMailboxTarget target,
        MailboxMutationRequester requester) =>

        // Matched with `when` clauses rather than constant patterns, because a closed enumeration's members are static
        // properties and a switch arm cannot pattern-match against one.
        mutation.Mutation switch
        {
            _ when mutation.Mutation == MailboxMutation.SetSeen => MailboxMutationRequest.SetSeen(
                change.StoredEmailId,
                target.Owner,
                target.Occurrence,
                requester,
                mutation.DesiredSeenState!.Value),
            _ when mutation.Mutation == MailboxMutation.SetFlagged => MailboxMutationRequest.SetFlagged(
                change.StoredEmailId,
                target.Owner,
                target.Occurrence,
                requester,
                mutation.DesiredFlaggedState!.Value),
            _ when mutation.Mutation == MailboxMutation.AddKeywords => MailboxMutationRequest.AddKeywords(
                change.StoredEmailId,
                target.Owner,
                target.Occurrence,
                requester,
                mutation.Keywords!),
            _ when mutation.Mutation == MailboxMutation.RemoveKeywords => MailboxMutationRequest.RemoveKeywords(
                change.StoredEmailId,
                target.Owner,
                target.Occurrence,
                requester,
                mutation.Keywords!),
            _ when mutation.Mutation == MailboxMutation.SetKeywords => MailboxMutationRequest.SetKeywords(
                change.StoredEmailId,
                target.Owner,
                target.Occurrence,
                requester,
                mutation.Keywords!),
            _ => throw new ArgumentOutOfRangeException(
                nameof(mutation),
                mutation.Mutation,
                "An authored flag change names a mutation this use case writes down."),
        };
}
