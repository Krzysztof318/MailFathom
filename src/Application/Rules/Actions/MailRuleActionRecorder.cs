// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Folders;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Persistence;
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
/// A destination is resolved to a remote folder once per pass and remembered, because a batch of two hundred emails
/// matching one filing rule would otherwise re-read one binding two hundred times. The binding a pass began with is the
/// one it finishes with, which is the same contract the rule set itself is read under. A destination named by role is
/// turned into a folder through the resolution every caller naming a folder shares, so a role the account has stopped
/// mapping fails here exactly as an alias whose binding went missing does.
/// </para>
/// </remarks>
public sealed class MailRuleActionRecorder
{
    private readonly IMailboxMutationRecordStore records;
    private readonly IMailFolderResolutionStore folderResolutions;
    private readonly MailFolderReferenceResolver folderReferences;
    private readonly IAuthoredDeleteEmailDispositionReader deleteDispositions;
    private readonly IMailRuleActionPermissionReader permissions;
    private readonly Dictionary<(MailAccountId Account, MailFolderReference Destination), ResolvedDestination?> destinations = [];

    /// <summary>Initializes the recorder from the record it writes and the decisions it has to read.</summary>
    /// <param name="records">Opens the durable record one action is carried by.</param>
    /// <param name="folderResolutions">Resolves a destination alias to the remote folder it currently names.</param>
    /// <param name="folderReferences">Turns the alias or the role a rule named into the folder of the account it means.</param>
    /// <param name="deleteDispositions">Answers what the account keeps locally of an email a rule deletes.</param>
    /// <param name="permissions">Answers which changes the account currently permits a rule to make.</param>
    /// <exception cref="ArgumentNullException">Thrown when a collaborator is <see langword="null" />.</exception>
    public MailRuleActionRecorder(
        IMailboxMutationRecordStore records,
        IMailFolderResolutionStore folderResolutions,
        MailFolderReferenceResolver folderReferences,
        IAuthoredDeleteEmailDispositionReader deleteDispositions,
        IMailRuleActionPermissionReader permissions)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(folderResolutions);
        ArgumentNullException.ThrowIfNull(folderReferences);
        ArgumentNullException.ThrowIfNull(deleteDispositions);
        ArgumentNullException.ThrowIfNull(permissions);

        this.records = records;
        this.folderResolutions = folderResolutions;
        this.folderReferences = folderReferences;
        this.deleteDispositions = deleteDispositions;
        this.permissions = permissions;
    }

    /// <summary>Opens one mutation record per action the plan honors, in the order the changes are applied.</summary>
    /// <param name="session">The session the records are staged in, which is the one the batch commits.</param>
    /// <param name="storedEmailId">The local email the rules matched.</param>
    /// <param name="occurrence">Where that email is, which is what an IMAP command will be issued against.</param>
    /// <param name="plan">What the matching rules together ask for.</param>
    /// <param name="revision">The rule set revision the pass ran under, which is part of every request's identity.</param>
    /// <param name="cancellationToken">Cancels the staging.</param>
    /// <returns>Every action a record was opened for, with the record that carries it, and every action nothing was opened for.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="revision" /> names no rule set.</exception>
    public async Task<MailRuleActionRecording> RecordAsync(
        IPersistenceSession session,
        StoredEmailId storedEmailId,
        EmailOccurrenceId occurrence,
        MailRuleActionPlan plan,
        MailRuleSetRevision revision,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(occurrence);
        ArgumentNullException.ThrowIfNull(plan);

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

            var request = await this.BuildRequestAsync(
                storedEmailId,
                occurrence,
                planned,
                revision,
                failures,
                cancellationToken);

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
                this.RecordedDestinationAlias(occurrence.AccountId, planned.Action.Destination)));
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
    private async Task<MailboxMutationRequest?> BuildRequestAsync(
        StoredEmailId storedEmailId,
        EmailOccurrenceId occurrence,
        PlannedMailRuleAction planned,
        MailRuleSetRevision revision,
        List<MailRuleActionFailure> failures,
        CancellationToken cancellationToken)
    {
        var action = planned.Action;
        var requester = MailboxMutationRequester.Rule(planned.RuleName, revision.Value);

        if (action.DesiredSeenState is { } isSeen)
        {
            return MailboxMutationRequest.SetSeen(storedEmailId, occurrence, requester, isSeen);
        }

        if (action.Destination is { } destination)
        {
            var resolved = await this.ResolveDestinationAsync(
                occurrence.AccountId,
                destination,
                cancellationToken);

            if (resolved is not { Path: var path })
            {
                failures.Add(new MailRuleActionFailure(
                    planned.RuleName,
                    planned.Position,
                    action.Mutation,
                    MailRuleActionFailureReason.DestinationFolderUnresolved,
                    destination));

                return null;
            }

            // No local disposition travels with a relocation here. One is supplied exactly when the destination is a
            // folder MailFathom does not mirror, and a rule may not name such a folder at all: the rule set is refused
            // when it is read if it does, so a destination that reaches this point is one whose mail stays mirrored.
            return action.Mutation == MailboxMutation.Relocate
                ? MailboxMutationRequest.Relocate(storedEmailId, occurrence, requester, path)
                : MailboxMutationRequest.Copy(storedEmailId, occurrence, requester, path);
        }

        return this.BuildDeleteRequest(storedEmailId, occurrence, planned, requester, failures);
    }

    /// <summary>Builds a delete, whose local disposition is the account's answer at the moment the request is written.</summary>
    /// <remarks>
    /// The reader refuses an account the configuration no longer declares, which a reload can produce while a pass over
    /// that account's mail is still running. That is reported as a failed action rather than allowed to end the pass:
    /// what the withdrawn account decided about its own deletions is unknown, and no value invented here would be it.
    /// </remarks>
    private MailboxMutationRequest? BuildDeleteRequest(
        StoredEmailId storedEmailId,
        EmailOccurrenceId occurrence,
        PlannedMailRuleAction planned,
        MailboxMutationRequester requester,
        List<MailRuleActionFailure> failures)
    {
        AuthoredDeleteEmailDisposition disposition;

        try
        {
            disposition = this.deleteDispositions.GetAuthoredDeleteDisposition(occurrence.AccountId);
        }
        catch (InvalidOperationException)
        {
            failures.Add(new MailRuleActionFailure(
                planned.RuleName,
                planned.Position,
                planned.Action.Mutation,
                MailRuleActionFailureReason.AccountNoLongerConfigured));

            return null;
        }

        return MailboxMutationRequest.Delete(storedEmailId, occurrence, requester, disposition);
    }

    /// <summary>Finds the folder a destination currently names, remembering the answer for the rest of the pass.</summary>
    /// <returns>The folder, or <see langword="null" /> when the account maps no such folder or nothing has bound one yet.</returns>
    private async Task<ResolvedDestination?> ResolveDestinationAsync(
        MailAccountId accountId,
        MailFolderReference destination,
        CancellationToken cancellationToken)
    {
        // Keyed by the account as well as the destination, because one name means a different folder on each account
        // and nothing in this type's contract says a scope holds one account's pass.
        if (this.destinations.TryGetValue((accountId, destination), out var remembered))
        {
            return remembered;
        }

        var resolved = await this.ReadCurrentDestinationAsync(accountId, destination, cancellationToken);

        this.destinations[(accountId, destination)] = resolved;

        return resolved;
    }

    /// <summary>Reads the alias a recorded action names its folder by, which is what this pass resolved the destination to.</summary>
    /// <remarks>
    /// It reads the answer this pass already has rather than resolving again, and it is asked only after the request was
    /// built — so an action that reaches the history names the folder mail was actually filed into, whether the rule
    /// wrote that folder's alias or the role it plays.
    /// </remarks>
    private MailFolderAlias? RecordedDestinationAlias(MailAccountId accountId, MailFolderReference? destination) =>
        destination is { } named && this.destinations.TryGetValue((accountId, named), out var resolved)
            ? resolved?.Alias
            : null;

    /// <summary>Turns a destination into the folder it currently names, in the two steps a name goes through.</summary>
    /// <remarks>
    /// A role the account no longer maps is caught rather than propagated, because it says the same thing to this pass
    /// as an alias whose binding is missing: the rule has nowhere to file, the action is reported as failed, and the
    /// rest of the batch keeps moving. Letting it out would stop every other rule over one rule's destination.
    /// </remarks>
    private async Task<ResolvedDestination?> ReadCurrentDestinationAsync(
        MailAccountId accountId,
        MailFolderReference destination,
        CancellationToken cancellationToken)
    {
        MailFolderAlias destinationAlias;

        try
        {
            destinationAlias = this.folderReferences.ResolveAlias(accountId, destination);
        }
        catch (MailFolderRoleUnmappedException)
        {
            return null;
        }

        var resolution = await this.folderResolutions.GetCurrentResolutionAsync(
            accountId,
            destinationAlias,
            cancellationToken);

        return resolution is { RemotePath: var remotePath }
            ? new ResolvedDestination(destinationAlias, remotePath)
            : null;
    }

    /// <summary>One destination as this pass resolved it: the alias it names the folder by, and where that folder is.</summary>
    /// <remarks>
    /// Both halves are kept because two different things need them. The request carries the path, which is what a
    /// mailbox command is issued against; the history names the alias, which is MailFathom's own name for the folder and
    /// the one a rule written against a role never spelled out.
    /// </remarks>
    private sealed record ResolvedDestination(MailFolderAlias Alias, RemoteFolderPath Path);
}
