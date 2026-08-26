// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Mail.Delivery.Filing;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Destinations;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization.Sessions;
using MailFathom.Domain.Delivery.Drafts;
using MailFathom.Domain.Delivery.Filing;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Mail.Delivery.Drafts;

/// <summary>Brings the drafts folder of one mailbox into step with one draft this deployment holds.</summary>
/// <remarks>
/// <para>
/// It is the same filing mechanism a sent copy goes through, specialized to the one message whose copy has to change.
/// The role and the flags are <see cref="OutgoingMailFiling.Draft" />'s, the folder is found by that role and never by
/// name, and the append and the withdrawal are the two operations the write session opens for exactly this. What is not
/// shared is the record: a filed copy is written once and kept, and a draft's copy is written, replaced, and taken back
/// out, so the durable account of it hangs off the draft rather than off an outgoing record.
/// </para>
/// <para>
/// <b>Replacing a draft is an append followed by a removal, in that order, and the order is the safety.</b> IMAP has no
/// command that changes a stored message. Removing first and then failing to append leaves the owner with no draft at
/// all — the version they were working on, gone — while appending first and then failing to remove leaves them with two,
/// which is untidy and loses nothing. The revision is durable before either command goes out, so a process that dies
/// between them is recognized by <see cref="MailDraftStage" /> and the resumed attempt finishes the pair.
/// </para>
/// <para>
/// <b>The only occurrence this can ever name is one an append of its own reported.</b> There is no path here from a
/// caller-supplied UID, a folder search, or a message identity to a removal, so a draft the owner wrote in their own
/// mail client is unreachable by construction rather than by a check. Where the tracked occurrence stops being provably
/// that copy — the role now resolves elsewhere, the folder was recreated, the server named no placement, an append was
/// never answered — the copy is left exactly where it is and the divergence is written onto the draft.
/// </para>
/// <para>
/// Nothing here raises for a copy that could not be settled. The draft itself is unharmed by any of it, and a failure
/// that ended the pass would leave the drafts beside this one unsettled over something that says nothing about them.
/// </para>
/// </remarks>
public sealed class MailDraftFiler
{
    private readonly MailboxCopyAppender appends;
    private readonly IMailboxWriteSessionFactory writeSessions;
    private readonly MailboxDestinationResolver destinations;
    private readonly IMailDraftStore drafts;
    private readonly IMailTransportSecurityPolicyReader transportSecurityPolicies;
    private readonly OptimisticConcurrencyRetryPolicy commitPolicy;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the filer from the append it files through and the record it writes each copy onto.</summary>
    /// <param name="appends">Puts each revision into the drafts folder, in the order that keeps it one copy.</param>
    /// <param name="writeSessions">Opens the one session able to change a mailbox, which a removal needs of its own.</param>
    /// <param name="destinations">Turns the drafts role into the folder of this account it means.</param>
    /// <param name="drafts">Keeps the durable account of every draft and every copy of one.</param>
    /// <param name="transportSecurityPolicies">Supplies the connection and authentication policy the commands obey.</param>
    /// <param name="commitPolicy">Commits each movement of the draft.</param>
    /// <param name="timeProvider">Stamps everything the record notes.</param>
    /// <exception cref="ArgumentNullException">Thrown when a collaborator is <see langword="null" />.</exception>
    public MailDraftFiler(
        MailboxCopyAppender appends,
        IMailboxWriteSessionFactory writeSessions,
        MailboxDestinationResolver destinations,
        IMailDraftStore drafts,
        IMailTransportSecurityPolicyReader transportSecurityPolicies,
        OptimisticConcurrencyRetryPolicy commitPolicy,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(appends);
        ArgumentNullException.ThrowIfNull(writeSessions);
        ArgumentNullException.ThrowIfNull(destinations);
        ArgumentNullException.ThrowIfNull(drafts);
        ArgumentNullException.ThrowIfNull(transportSecurityPolicies);
        ArgumentNullException.ThrowIfNull(commitPolicy);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.appends = appends;
        this.writeSessions = writeSessions;
        this.destinations = destinations;
        this.drafts = drafts;
        this.transportSecurityPolicies = transportSecurityPolicies;
        this.commitPolicy = commitPolicy;
        this.timeProvider = timeProvider;
    }

    /// <summary>Does whatever one draft's recorded stage says the mailbox is owed, and records what happened.</summary>
    /// <param name="draft">The draft, read with its copies.</param>
    /// <param name="cancellationToken">Cancels the commands and the writes around them.</param>
    /// <returns>What the attempt did, which is already durable by the time it is returned.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="draft" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A whole replacement is one call: the current revision is appended, and the copy it replaced is removed once the
    /// server has confirmed the new one is there. That is what leaves an owner who edited a draft looking at one draft
    /// rather than at two for as long as it takes a pass to come round.
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A draft whose copy could not be settled is a draft that still exists and is still editable; raising would end the pass that was settling the drafts beside it, and every failure is classified into a recorded code and returned as an outcome instead.")]
    public async Task<MailDraftFilingResult> SettleAsync(
        MailDraftRecord draft,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        try
        {
            return await this.RunAsync(draft, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller stopping is not something that happened to the copy. Everything past an issued append is
            // caught where it happens, so a cancellation reaching here left the mail server untouched and the next
            // pass settles the draft as though this attempt had never started.
            throw;
        }
        catch (Exception failure)
        {
            return await this.RecordFailureAsync(
                draft,
                MailboxCopyAppender.FailureCodeOf(failure),
                MailDraftFilingOutcome.Failed);
        }
    }

    /// <summary>Runs whatever the stage calls for, which is at most an append followed by the removals it caused.</summary>
    private async Task<MailDraftFilingResult> RunAsync(MailDraftRecord draft, CancellationToken cancellationToken)
    {
        if (draft.IsDiscarded)
        {
            return await this.DiscardAsync(draft, cancellationToken);
        }

        if (draft.HasUnansweredAppend)
        {
            return await this.LeaveUnansweredAppendAsync(draft);
        }

        if (draft.CurrentCopy is not null)
        {
            return draft.SupersededCopies.Count > 0
                ? await this.RemoveSupersededAsync(draft, cancellationToken)
                : Result(draft, MailDraftFilingOutcome.AlreadySettled);
        }

        var appended = await this.AppendCurrentRevisionAsync(draft, cancellationToken);

        if (appended.Outcome != MailDraftFilingOutcome.Filed)
        {
            return appended;
        }

        // Read again rather than reasoned about, because the removal needs the copy row the append just wrote and the
        // record in hand predates it. A draft that vanished in between is one somebody discarded while this ran, and
        // the pass that discarded it owns the removal.
        if (await this.drafts.FindAsync(draft.Id, cancellationToken) is not { } settled)
        {
            return appended;
        }

        return settled.SupersededCopies.Count > 0
            ? await this.RemoveSupersededAsync(settled, cancellationToken)
            : appended;
    }

    /// <summary>Appends the stored message as a new copy in the drafts folder, moving the draft as the append passes.</summary>
    private async Task<MailDraftFilingResult> AppendCurrentRevisionAsync(
        MailDraftRecord draft,
        CancellationToken cancellationToken)
    {
        var appended = await this.appends.AppendAsync(
            draft.Account,
            OutgoingMailFiling.Draft,
            MailboxCopySource.MailDraft(draft.Id),
            (binding, token) => this.commitPolicy.CommitAsync(
                (persistenceSession, commitToken) => this.drafts.RecordAppendIssuedAsync(
                    persistenceSession,
                    draft.Id,
                    binding,
                    this.timeProvider.GetUtcNow(),
                    commitToken),
                token),
            copy => this.commitPolicy.CommitAsync(
                (persistenceSession, commitToken) => this.drafts.RecordAppendConfirmedAsync(
                    persistenceSession,
                    draft.Id,
                    copy,
                    commitToken),
                CancellationToken.None),
            cancellationToken);

        if (appended.Failure is not { } failure)
        {
            return Result(draft, MailDraftFilingOutcome.Filed);
        }

        return await this.RecordFailureAsync(draft, failure, FilingOutcomeOf(appended.Outcome));
    }

    /// <summary>Takes every copy a revision replaced back out of the folder, leaving the current one standing.</summary>
    private async Task<MailDraftFilingResult> RemoveSupersededAsync(
        MailDraftRecord draft,
        CancellationToken cancellationToken)
    {
        var divergence = await this.WithdrawAsync(draft, draft.SupersededCopies, cancellationToken);

        return divergence is { } reason
            ? Result(draft, MailDraftFilingOutcome.Diverged, divergence: reason)
            : Result(draft, MailDraftFilingOutcome.Replaced);
    }

    /// <summary>Takes every standing copy of a given-up draft out of the folder, then removes the record.</summary>
    /// <remarks>
    /// The record goes last and goes whatever the copies did. A draft whose copy could not be reached would otherwise
    /// be undeletable, so the copy is marked as one nothing will touch again, the divergence is recorded, and the owner
    /// is left with one message in a folder they can delete with the gesture they would have used anyway.
    /// </remarks>
    private async Task<MailDraftFilingResult> DiscardAsync(
        MailDraftRecord draft,
        CancellationToken cancellationToken)
    {
        var standing = draft.Copies.Where(copy => copy.IsStanding).ToArray();

        // Both are asked, because a draft can have a copy that could not be withdrawn and a separate copy whose append
        // was never answered, and each is a message left in the folder on its own account. The result carries one
        // reason, so what is reported is the withdrawal's where there is one — a copy this system proved it could not
        // reach is the stronger statement of the two.
        var withdrawal = await this.WithdrawAsync(draft, standing, cancellationToken);
        var unanswered = await this.AbandonUnansweredAppendsAsync(draft);

        var divergence = withdrawal ?? unanswered;

        await this.commitPolicy.CommitAsync(
            (session, token) => this.drafts.RemoveAsync(session, draft.Id, token),
            CancellationToken.None);

        return Result(draft, MailDraftFilingOutcome.Discarded, divergence: divergence);
    }

    /// <summary>Withdraws the copies it still can, and abandons the ones it cannot with the reason it could not.</summary>
    /// <returns>The reason a copy was left standing, or <see langword="null" /> when every copy was taken out.</returns>
    /// <remarks>
    /// The destination is resolved once and compared against every copy's recorded path, because the path is where the
    /// folder was when that copy was appended and an alias repointed since names another folder entirely. A copy in a
    /// folder the role no longer means is somebody else's mail as far as this system can prove, so it is abandoned
    /// rather than expunged.
    /// </remarks>
    private async Task<MailDraftDivergenceReason?> WithdrawAsync(
        MailDraftRecord draft,
        IReadOnlyList<MailDraftServerCopy> copies,
        CancellationToken cancellationToken)
    {
        if (copies.Count == 0)
        {
            return null;
        }

        if (await this.ResolveDraftsFolderAsync(draft, cancellationToken) is not { } destination)
        {
            return await this.AbandonAsync(draft, copies, MailDraftDivergenceReason.DestinationChanged);
        }

        MailDraftDivergenceReason? divergence = null;

        foreach (var copy in copies)
        {
            var reason = await this.WithdrawOneAsync(draft, copy, destination, cancellationToken);

            divergence ??= reason;
        }

        return divergence;
    }

    /// <summary>Withdraws one copy, or abandons it naming what put it out of reach.</summary>
    private async Task<MailDraftDivergenceReason?> WithdrawOneAsync(
        MailDraftRecord draft,
        MailDraftServerCopy copy,
        MailboxDestination destination,
        CancellationToken cancellationToken)
    {
        if (!copy.NamesFolder(destination.Path))
        {
            return await this.AbandonAsync(draft, [copy], MailDraftDivergenceReason.DestinationChanged);
        }

        if (copy.Placement is not { UidValidity: { } uidValidity, Uid: { } uid })
        {
            return await this.AbandonAsync(draft, [copy], MailDraftDivergenceReason.PlacementUnreported);
        }

        var transportSecurityPolicy = this.transportSecurityPolicies.GetPolicy(draft.AccountId);

        try
        {
            await using var session = await this.writeSessions.OpenForWritingAsync(
                draft.AccountId,
                destination.Binding,
                transportSecurityPolicy,
                cancellationToken);

            await session.WithdrawAppendedAsync(uidValidity, uid, cancellationToken);
        }
        catch (MailboxFolderRecreatedException)
        {
            // The folder renumbered every message in it since the append, so the recorded UID names somebody else's
            // mail. Nothing is issued against it and nothing ever will be.
            return await this.AbandonAsync(draft, [copy], MailDraftDivergenceReason.FolderRecreated);
        }

        await this.SettleCopyAsync(draft.Id, copy.Revision, MailDraftCopyStage.Withdrawn);

        return null;
    }

    /// <summary>Marks copies as ones nothing will touch again, and writes why onto the draft.</summary>
    private async Task<MailDraftDivergenceReason> AbandonAsync(
        MailDraftRecord draft,
        IReadOnlyList<MailDraftServerCopy> copies,
        MailDraftDivergenceReason reason)
    {
        foreach (var copy in copies)
        {
            await this.SettleCopyAsync(draft.Id, copy.Revision, MailDraftCopyStage.Abandoned);
        }

        await this.drafts.RecordDivergenceAsync(
            draft.Id,
            reason,
            this.timeProvider.GetUtcNow(),
            CancellationToken.None);

        return reason;
    }

    /// <summary>Marks every append this draft never got an answer for as one nothing will touch again.</summary>
    /// <returns>The reason recorded, or <see langword="null" /> when there was no such append.</returns>
    private async Task<MailDraftDivergenceReason?> AbandonUnansweredAppendsAsync(MailDraftRecord draft)
    {
        var unanswered = draft.Copies.Where(copy => copy.HasUnknownOutcome).ToArray();

        return unanswered.Length == 0
            ? null
            : await this.AbandonAsync(draft, unanswered, MailDraftDivergenceReason.AppendOutcomeUnknown);
    }

    /// <summary>Reports a draft whose append the server never answered, and says so on the record once.</summary>
    /// <remarks>
    /// Nothing is issued and nothing is abandoned. The copy may or may not be in the folder and the draft is still
    /// editable here, so what an operator needs is the statement that the two stopped following each other.
    /// </remarks>
    private async Task<MailDraftFilingResult> LeaveUnansweredAppendAsync(MailDraftRecord draft)
    {
        if (draft.Divergence?.Reason != MailDraftDivergenceReason.AppendOutcomeUnknown)
        {
            await this.drafts.RecordDivergenceAsync(
                draft.Id,
                MailDraftDivergenceReason.AppendOutcomeUnknown,
                this.timeProvider.GetUtcNow(),
                CancellationToken.None);
        }

        return Result(
            draft,
            MailDraftFilingOutcome.OutcomeUnknown,
            divergence: MailDraftDivergenceReason.AppendOutcomeUnknown);
    }

    private Task SettleCopyAsync(MailDraftId draftId, int revision, MailDraftCopyStage stage) =>
        this.commitPolicy.CommitAsync(
            (session, token) => this.drafts.RecordCopySettledAsync(
                session,
                draftId,
                revision,
                stage,
                this.timeProvider.GetUtcNow(),
                token),
            CancellationToken.None);

    /// <summary>Finds the folder of this account that plays the drafts role, as it currently resolves.</summary>
    private async Task<MailboxDestination?> ResolveDraftsFolderAsync(
        MailDraftRecord draft,
        CancellationToken cancellationToken)
    {
        var reference = MailFolderReference.ToRole(OutgoingMailFiling.Draft.Role);

        var resolved = await this.destinations.ResolveAsync(draft.Account, [reference], cancellationToken);

        return resolved.Find(reference).Destination;
    }

    /// <summary>Writes the reason a draft is not where it belongs onto the record, without touching the draft itself.</summary>
    private async Task<MailDraftFilingResult> RecordFailureAsync(
        MailDraftRecord draft,
        MailFathomErrorCode failure,
        MailDraftFilingOutcome outcome)
    {
        await this.drafts.RecordFailureAsync(draft.Id, failure, CancellationToken.None);

        return Result(draft, outcome, failure);
    }

    /// <summary>Reads what the append reported as what it means for the draft the copy belongs to.</summary>
    /// <remarks>
    /// <see cref="MailboxCopyAppendOutcome.Appended" /> never reaches here, because a result carrying no failure is the
    /// copy that was filed. What is left is a revision that could not be appended, which the draft outlives.
    /// </remarks>
    private static MailDraftFilingOutcome FilingOutcomeOf(MailboxCopyAppendOutcome outcome) => outcome switch
    {
        MailboxCopyAppendOutcome.DestinationUnavailable => MailDraftFilingOutcome.DestinationUnavailable,
        MailboxCopyAppendOutcome.OutcomeUnknown => MailDraftFilingOutcome.OutcomeUnknown,
        _ => MailDraftFilingOutcome.Failed,
    };

    private static MailDraftFilingResult Result(
        MailDraftRecord draft,
        MailDraftFilingOutcome outcome,
        MailFathomErrorCode? failure = null,
        MailDraftDivergenceReason? divergence = null) =>
        new(draft.Id, outcome, failure, divergence);
}
