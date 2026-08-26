// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Destinations;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;

namespace MailFathom.Application.Rules.Actions;

/// <summary>Writes down the changes one email's matching rules ask for, as the mutation records every requester uses.</summary>
/// <remarks>
/// <para>
/// This is the join between a rule and a mailbox, and it is deliberately the whole of it. Nothing here issues an IMAP
/// command: it opens a durable record per action, and the account's own convergence pass carries each record to a
/// completed or a dead-lettered ending exactly as it carries a change somebody authored by hand. What this adds is the
/// identity, the order, and the refusal of an action whose account or destination has stopped permitting it.
/// </para>
/// <para>
/// What an account permits is read here as well as when the rule set is read, and the second reading is not redundant:
/// the two configuration sections reload independently, so narrowing what an account permits leaves a rule set nobody
/// edited in force. Without this, a revoked permission would take effect at the next edit of the rules rather than at
/// the next pass, which for a deletion is the wrong way round.
/// </para>
/// <para>
/// The records join the caller's session, so a batch's evaluations and the requests they produced commit together. A
/// crash between them is therefore impossible in the direction that matters: an email is never recorded as evaluated
/// while the change its rules asked for was lost, and a rolled-back batch is evaluated again and asks again under the
/// same identity.
/// </para>
/// <para>
/// Where a destination is is not resolved here. The answers are handed in, already resolved, because resolving one can
/// reach the mail server and nothing may do that while the batch's transaction is open. A rule is one author of a
/// filing mutation among others, and the folder it files into is found the same way for all of them.
/// </para>
/// </remarks>
public sealed class MailRuleActionRecorder
{
    private readonly IMailboxMutationRecordStore records;
    private readonly IAuthoredDeleteEmailDispositionReader deleteDispositions;
    private readonly IMailRuleActionPermissionReader permissions;

    /// <summary>Initializes the recorder from the record it writes and the decisions it has to read.</summary>
    /// <param name="records">Opens the durable record one action is carried by.</param>
    /// <param name="deleteDispositions">Answers what the account keeps locally of an email a rule deletes or files away.</param>
    /// <param name="permissions">Answers which changes the account currently permits a rule to make.</param>
    /// <exception cref="ArgumentNullException">Thrown when a collaborator is <see langword="null" />.</exception>
    public MailRuleActionRecorder(
        IMailboxMutationRecordStore records,
        IAuthoredDeleteEmailDispositionReader deleteDispositions,
        IMailRuleActionPermissionReader permissions)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(deleteDispositions);
        ArgumentNullException.ThrowIfNull(permissions);

        this.records = records;
        this.deleteDispositions = deleteDispositions;
        this.permissions = permissions;
    }

    /// <summary>Opens one mutation record per action the plan honors, in the order the changes are applied.</summary>
    /// <param name="session">The session the records are staged in, which is the one the batch commits.</param>
    /// <param name="storedEmailId">The local email the rules matched.</param>
    /// <param name="owner">The owner whose account the email belongs to, which every record written here carries.</param>
    /// <param name="occurrence">Where that email is, which is what an IMAP command will be issued against.</param>
    /// <param name="plan">What the matching rules together ask for.</param>
    /// <param name="revision">The rule set revision the pass ran under, which is part of every request's identity.</param>
    /// <param name="destinations">Where the folders this batch's actions name currently are, resolved before the transaction opened.</param>
    /// <param name="cancellationToken">Cancels the staging.</param>
    /// <returns>Every action a record was opened for, with the record that carries it, and every action nothing was opened for.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="revision" /> names no rule set.</exception>
    public async Task<MailRuleActionRecording> RecordAsync(
        IPersistenceSession session,
        StoredEmailId storedEmailId,
        MailOwnerId owner,
        EmailOccurrenceId occurrence,
        MailRuleActionPlan plan,
        MailRuleSetRevision revision,
        MailboxDestinations destinations,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(occurrence);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(destinations);

        if (!revision.IsSpecified)
        {
            throw new ArgumentException("A recorded rule action must name the revision it was planned under.", nameof(revision));
        }

        if (plan.IsEmpty)
        {
            return MailRuleActionRecording.Nothing;
        }

        if (this.TryReadPermissions(occurrence.AccountId) is not { } permitted)
        {
            return new MailRuleActionRecording([], WithdrawnAccountFailures(plan));
        }

        var failures = new List<MailRuleActionFailure>();
        var recorded = new List<RecordedMailRuleAction>();

        foreach (var planned in plan.Actions)
        {
            // Read again here rather than trusted from validation, because the synchronization section and the rule
            // section reload independently: narrowing what an account permits leaves a rule set nobody edited in force,
            // and the revocation has to reach the next pass rather than the next edit of the rules.
            if (!permitted.Permits(planned.Action.Mutation))
            {
                failures.Add(new MailRuleActionFailure(
                    planned.RuleName,
                    planned.Position,
                    planned.Action.Mutation,
                    MailRuleActionFailureReason.ActionNoLongerPermitted,
                    planned.Action.Destination));

                continue;
            }

            var request = this.BuildRequest(storedEmailId, owner, occurrence, planned, revision, destinations, failures);

            if (request is null)
            {
                continue;
            }

            var record = await this.records.OpenAsync(session, request, cancellationToken);

            recorded.Add(new RecordedMailRuleAction(
                planned.RuleName,
                planned.Position,
                planned.Action.Mutation,
                record.Id,
                RecordedDestinationAlias(destinations, planned.Action.Destination)));
        }

        return new MailRuleActionRecording(recorded, failures);
    }

    /// <summary>Reads what the account permits, or nothing when the configuration no longer declares it.</summary>
    private MailRuleActionPermissions? TryReadPermissions(MailAccountId accountId)
    {
        try
        {
            return this.permissions.GetRuleActionPermissions(accountId);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Reports every action of a plan as failed, for an account the configuration has stopped declaring.</summary>
    private static IReadOnlyList<MailRuleActionFailure> WithdrawnAccountFailures(MailRuleActionPlan plan) =>
    [
        .. plan.Actions.Select(planned => new MailRuleActionFailure(
            planned.RuleName,
            planned.Position,
            planned.Action.Mutation,
            MailRuleActionFailureReason.AccountNoLongerConfigured,
            planned.Action.Destination)),
    ];

    /// <summary>Turns one planned action into the request that carries it, or records why it has none.</summary>
    private MailboxMutationRequest? BuildRequest(
        StoredEmailId storedEmailId,
        MailOwnerId owner,
        EmailOccurrenceId occurrence,
        PlannedMailRuleAction planned,
        MailRuleSetRevision revision,
        MailboxDestinations destinations,
        List<MailRuleActionFailure> failures)
    {
        var action = planned.Action;
        var requester = MailboxMutationRequester.Rule(planned.RuleName, revision.Value);

        if (action.DesiredSeenState is { } isSeen)
        {
            return MailboxMutationRequest.SetSeen(storedEmailId, owner, occurrence, requester, isSeen);
        }

        if (action.DesiredFlaggedState is { } isFlagged)
        {
            return MailboxMutationRequest.SetFlagged(storedEmailId, owner, occurrence, requester, isFlagged);
        }

        if (action.Keywords is { } keywords)
        {
            return BuildKeywordRequest(storedEmailId, owner, occurrence, requester, action.Mutation, keywords);
        }

        if (action.Destination is { } destination)
        {
            return this.BuildFilingRequest(
                storedEmailId,
                owner,
                occurrence,
                planned,
                requester,
                destinations.Find(destination),
                failures);
        }

        return this.BuildDeleteRequest(storedEmailId, owner, occurrence, planned, requester, failures);
    }

    /// <summary>Builds the keyword change one action asked for, which needs nothing of the account to resolve.</summary>
    /// <remarks>
    /// A keyword is a label rather than a reference to something the account maps, so unlike a destination folder there
    /// is nothing here that could fail to resolve and no failure to record. The three mutations differ only in what the
    /// server is asked to do with the set, which is why they are told apart by the action's own mutation rather than by
    /// anything about the keywords.
    /// </remarks>
    private static MailboxMutationRequest BuildKeywordRequest(
        StoredEmailId storedEmailId,
        MailOwnerId owner,
        EmailOccurrenceId occurrence,
        MailboxMutationRequester requester,
        MailboxMutation mutation,
        AuthoredMailKeywords keywords)
    {
        if (mutation == MailboxMutation.AddKeywords)
        {
            return MailboxMutationRequest.AddKeywords(storedEmailId, owner, occurrence, requester, keywords);
        }

        return mutation == MailboxMutation.RemoveKeywords
            ? MailboxMutationRequest.RemoveKeywords(storedEmailId, owner, occurrence, requester, keywords)
            : MailboxMutationRequest.SetKeywords(storedEmailId, owner, occurrence, requester, keywords);
    }

    /// <summary>Builds the relocation or the copy one action asked for, against the folder its destination resolved to.</summary>
    /// <remarks>
    /// A relocation into a folder the account mirrors carries the local row into that folder and decides nothing about
    /// it. One into a folder the account only maps has taken the message out of the mirrored mailbox for good, so the
    /// request carries what the account says becomes of a local copy — the same answer a delete carries, resolved when
    /// the change is authored rather than when the source occurrence is later seen to be gone. A copy carries none
    /// either way, because the message it duplicates stays where it is.
    /// </remarks>
    private MailboxMutationRequest? BuildFilingRequest(
        StoredEmailId storedEmailId,
        MailOwnerId owner,
        EmailOccurrenceId occurrence,
        PlannedMailRuleAction planned,
        MailboxMutationRequester requester,
        MailboxDestinationResolution resolution,
        List<MailRuleActionFailure> failures)
    {
        if (resolution.Destination is not { } destination)
        {
            failures.Add(new MailRuleActionFailure(
                planned.RuleName,
                planned.Position,
                planned.Action.Mutation,
                FailureReasonOf(resolution.Outcome),
                planned.Action.Destination));

            return null;
        }

        if (planned.Action.Mutation == MailboxMutation.Copy)
        {
            return MailboxMutationRequest.Copy(storedEmailId, owner, occurrence, requester, destination.Path);
        }

        if (destination.IsMirrored)
        {
            return MailboxMutationRequest.Relocate(storedEmailId, owner, occurrence, requester, destination.Path);
        }

        return this.TryReadDeleteDisposition(occurrence.AccountId, planned, failures) is { } disposition
            ? MailboxMutationRequest.Relocate(storedEmailId, owner, occurrence, requester, destination.Path, disposition)
            : null;
    }

    /// <summary>Names the refusal one unresolved destination is reported to the operator as.</summary>
    private static MailRuleActionFailureReason FailureReasonOf(MailboxDestinationOutcome outcome) => outcome switch
    {
        MailboxDestinationOutcome.Unmapped => MailRuleActionFailureReason.DestinationFolderUnmapped,
        MailboxDestinationOutcome.NotAdvertised => MailRuleActionFailureReason.DestinationFolderNotAdvertised,
        MailboxDestinationOutcome.Ambiguous => MailRuleActionFailureReason.DestinationFolderAmbiguous,
        _ => MailRuleActionFailureReason.DestinationFolderUnresolved,
    };

    /// <summary>Builds a delete, whose local disposition is the account's answer at the moment the request is written.</summary>
    /// <remarks>
    /// The reader refuses an account the configuration no longer declares, which a reload can produce while a pass over
    /// that account's mail is still running. That is reported as a failed action rather than allowed to end the pass:
    /// what the withdrawn account decided about its own deletions is unknown, and no value invented here would be it.
    /// </remarks>
    private MailboxMutationRequest? BuildDeleteRequest(
        StoredEmailId storedEmailId,
        MailOwnerId owner,
        EmailOccurrenceId occurrence,
        PlannedMailRuleAction planned,
        MailboxMutationRequester requester,
        List<MailRuleActionFailure> failures) =>
        this.TryReadDeleteDisposition(occurrence.AccountId, planned, failures) is { } disposition
            ? MailboxMutationRequest.Delete(storedEmailId, owner, occurrence, requester, disposition)
            : null;

    /// <summary>Reads what becomes of the local copy, or records that the account deciding it is no longer declared.</summary>
    private AuthoredDeleteEmailDisposition? TryReadDeleteDisposition(
        MailAccountId accountId,
        PlannedMailRuleAction planned,
        List<MailRuleActionFailure> failures)
    {
        try
        {
            return this.deleteDispositions.GetAuthoredDeleteDisposition(accountId);
        }
        catch (InvalidOperationException)
        {
            failures.Add(new MailRuleActionFailure(
                planned.RuleName,
                planned.Position,
                planned.Action.Mutation,
                MailRuleActionFailureReason.AccountNoLongerConfigured,
                planned.Action.Destination));

            return null;
        }
    }

    /// <summary>Reads the alias a recorded action names its folder by, which is the folder mail was actually filed into.</summary>
    /// <remarks>
    /// It reads the answers the batch was handed rather than resolving again, and it is asked only after the request was
    /// built — so an action that reaches the history names the folder mail went to, whether the rule wrote that folder's
    /// alias or the role it plays.
    /// </remarks>
    private static MailFolderAlias? RecordedDestinationAlias(
        MailboxDestinations destinations,
        MailFolderReference? destination) =>
        destination is { } named ? destinations.Find(named).Destination?.Alias : null;
}
