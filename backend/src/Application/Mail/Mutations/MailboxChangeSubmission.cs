// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Folders.Local;
using MailFathom.Application.Mail.Mutations.Audit;
using MailFathom.Application.Mail.Mutations.Destinations;
using MailFathom.Application.Mail.Mutations.Local;
using MailFathom.Application.Persistence;
using MailFathom.Application.Signals;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Mutations.Audit;

namespace MailFathom.Application.Mail.Mutations;

/// <summary>Takes one authored change in hand, choosing from the account's custody phase whether it is carried to a mail server or committed locally.</summary>
/// <remarks>
/// <para>
/// This is the one place that choice is made. Every requester — a person through the client, a tool, a rule, and a spam
/// verdict — reaches a mailbox through a use case that submits here, so none of them branches on where the mail lives:
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0034-holding-a-mailbox-mailfathom-alone-keeps.md">ADR 0034</see>
/// requires that the executor be chosen once, in the mutation layer, from the account's phase.
/// </para>
/// <para>
/// On an account that is not held, the change is written down as the durable record the account's convergence pass
/// carries, exactly as before. On a held account there is no server to carry it to, so it is committed to stored state
/// together with its audit entry, in the caller's transaction, and no record is written — a transaction either happened
/// or it did not, and there is no sequence for a crash to interrupt. The one exception is erasure: a delete of a message
/// already in the trash opens the ordinary record, held for the caller's window, so the existing withdrawal and release
/// routes keep acting on it and the cascade runs only once the window has passed.
/// </para>
/// <para>
/// The phase is read inside the caller's transaction rather than before it, so a change and the phase it was judged
/// under commit together.
/// </para>
/// </remarks>
public sealed class MailboxChangeSubmission
{
    private readonly ILocalMailFolderStore localFolders;
    private readonly IMailboxMutationRecordStore records;
    private readonly ILocalEmailStateStore states;
    private readonly IMailboxMutationAuditSettingsReader auditSettings;
    private readonly IMailboxMutationAuditEntryStore auditEntries;
    private readonly ClientSignals signals;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the submission over both executors and what a local change is audited and announced through.</summary>
    /// <param name="localFolders">Answers the account's custody phase and the local folders a held account keeps.</param>
    /// <param name="records">Opens the durable record a change on a mirrored account, and an erasure on a held one, is carried by.</param>
    /// <param name="states">Reads and writes the stored state a local change commits to.</param>
    /// <param name="auditSettings">Answers whether the account keeps an audit trail.</param>
    /// <param name="auditEntries">Appends a local change's audit entry in the change's own transaction.</param>
    /// <param name="signals">Announces a local change to the clients watching the account.</param>
    /// <param name="timeProvider">Stamps a local change's audit entry and identity.</param>
    /// <exception cref="ArgumentNullException">Thrown when a collaborator is <see langword="null" />.</exception>
    public MailboxChangeSubmission(
        ILocalMailFolderStore localFolders,
        IMailboxMutationRecordStore records,
        ILocalEmailStateStore states,
        IMailboxMutationAuditSettingsReader auditSettings,
        IMailboxMutationAuditEntryStore auditEntries,
        ClientSignals signals,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(localFolders);
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(states);
        ArgumentNullException.ThrowIfNull(auditSettings);
        ArgumentNullException.ThrowIfNull(auditEntries);
        ArgumentNullException.ThrowIfNull(signals);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.localFolders = localFolders;
        this.records = records;
        this.states = states;
        this.auditSettings = auditSettings;
        this.auditEntries = auditEntries;
        this.signals = signals;
        this.timeProvider = timeProvider;
    }

    /// <summary>Records or commits one change, inside the caller's transaction.</summary>
    /// <param name="session">The transaction the change commits in.</param>
    /// <param name="request">The change asked for.</param>
    /// <param name="destination">Where a relocation or a copy files the message, resolved before the transaction opened; <see langword="null" /> for every other change.</param>
    /// <param name="heldUntil">How long a written record waits before a convergence pass may take it in hand.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The record written, the local change committed, or why neither happened.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> or <paramref name="request" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A local change is announced only once the caller's transaction has committed, which is why this returns what to
    /// announce and <see cref="Announce" /> publishes it: a signal raised inside a transaction that then rolls back would
    /// send a client to read a change that never happened.
    /// </remarks>
    public Task<SubmittedMailboxChange> SubmitAsync(
        IPersistenceSession session,
        MailboxMutationRequest request,
        MailboxDestination? destination,
        DateTimeOffset? heldUntil,
        CancellationToken cancellationToken) =>
        this.SubmitAsync(session, request, destination, heldUntil, refusesMoveIntoCurrentFolder: false, cancellationToken);

    /// <summary>Records or commits a move a person asked for, answering one into the folder the message is in as already there.</summary>
    /// <param name="session">The transaction the move commits in.</param>
    /// <param name="request">The move asked for.</param>
    /// <param name="destination">Where the move files the message, resolved before the transaction opened.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The record written, the local move committed, or why neither happened.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> or <paramref name="request" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A rule's move into the folder a mirrored message is in is still recorded, as it always was, which is why the
    /// refusal is asked for rather than applied to every requester. The question is answered here rather than by the
    /// caller because it differs by phase: on a held account the folder a message is in is local, not the alias it was
    /// stored under.
    /// </remarks>
    public Task<SubmittedMailboxChange> SubmitMoveAsync(
        IPersistenceSession session,
        MailboxMutationRequest request,
        MailboxDestination destination,
        CancellationToken cancellationToken) =>
        this.SubmitAsync(session, request, destination, heldUntil: null, refusesMoveIntoCurrentFolder: true, cancellationToken);

    private async Task<SubmittedMailboxChange> SubmitAsync(
        IPersistenceSession session,
        MailboxMutationRequest request,
        MailboxDestination? destination,
        DateTimeOffset? heldUntil,
        bool refusesMoveIntoCurrentFolder,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        var account = request.Account;
        var holding = await this.localFolders.ReadAsync(session, account, cancellationToken);

        if (holding is not { Phase: MailAccountCustodyPhase.Held })
        {
            if (refusesMoveIntoCurrentFolder
                && request.Mutation == MailboxMutation.Relocate
                && destination?.Alias == request.Occurrence.FolderResolutionId.Alias)
            {
                return SubmittedMailboxChange.NotSubmitted(MailboxChangeSubmissionOutcome.AlreadyInDestination);
            }

            var record = await this.records.OpenAsync(session, request, heldUntil, cancellationToken);

            return SubmittedMailboxChange.Recorded(record);
        }

        var state = await this.states.ReadAsync(session, account, request.StoredEmailId, cancellationToken);

        if (state is null)
        {
            return SubmittedMailboxChange.NotSubmitted(MailboxChangeSubmissionOutcome.MessageMissing);
        }

        return await this.CommitLocallyAsync(session, request, destination, heldUntil, holding, state, cancellationToken);
    }

    /// <summary>Runs a held erasure whose window has passed, removing the message and everything derived from it.</summary>
    /// <param name="session">The transaction the erasure commits in.</param>
    /// <param name="record">The erasure's record, which the cascade removes with the message.</param>
    /// <param name="cancellationToken">Cancels the erasure.</param>
    /// <returns>What to announce once the transaction commits, or <see langword="null" /> where the message is already gone.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> or <paramref name="record" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The audit entry is written from the record completed, so it names the record the person could have withdrawn and
    /// the moment they asked; it survives the cascade because the trail holds values rather than associations.
    /// </remarks>
    public async Task<AppliedMailboxChange?> EraseAsync(
        IPersistenceSession session,
        MailboxMutationRecord record,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(record);

        var account = record.Request.Account;
        var state = await this.states.ReadAsync(session, account, record.Request.StoredEmailId, cancellationToken);

        if (state is null)
        {
            return null;
        }

        var now = this.timeProvider.GetUtcNow();

        if (record.IsAudited)
        {
            await this.auditEntries.AppendAsync(
                session,
                MailboxMutationAuditEntry.Of(
                    this.MintAuditEntryId(now),
                    record with { Stage = MailboxMutationStage.Completed, StageChangedAt = now },
                    state.SourceFolder,
                    now),
                cancellationToken);
        }

        await this.states.EraseAsync(session, account, record.Request.StoredEmailId, cancellationToken);

        return new AppliedMailboxChange(account, state.SourceFolder.Alias, record.Request.StoredEmailId, Flags: null);
    }

    /// <summary>Tells the clients watching an account about a local change that has committed.</summary>
    /// <param name="applied">What was committed.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="applied" /> is <see langword="null" />.</exception>
    public void Announce(AppliedMailboxChange applied)
    {
        ArgumentNullException.ThrowIfNull(applied);

        if (!this.signals.Reaches)
        {
            return;
        }

        this.signals.Publish(applied.Flags is { } flags
            ? ClientSignal.MailFlagsChanged(applied.Account, applied.Folder, [flags])
            : ClientSignal.MailChanged(applied.Account, applied.Folder, [applied.Email]));
    }

    /// <summary>Finds the local folder a destination resolved on the source corresponds to.</summary>
    /// <remarks>
    /// The correspondence is the one arrivals follow: a protected role is that role's own local folder, and any other
    /// folder is the local folder its source folder created. A folder whose source has sent nothing yet has no local
    /// folder, and one a person deleted into the trash is not a place to file mail into, so both answer as missing.
    /// </remarks>
    private static LocalMailFolder? CorrespondingFolder(LocalMailFolderTree tree, MailboxDestination destination) =>
        destination.Role is { } role && LocalMailFolderTree.ProtectedRoles.Contains(role)
            ? tree.Folders.FirstOrDefault(folder => folder.Role == role)
            : tree.Folders.FirstOrDefault(folder =>
                folder.SourceFolderAlias == destination.Alias && !tree.IsInTrash(folder.Id));

    /// <summary>Builds the keyword set a keyword change leaves on a message that carried the earlier one.</summary>
    private static RemoteEmailKeywords KeywordsAfter(MailboxMutationRequest request, RemoteEmailKeywords carried)
    {
        var named = request.Keywords!.Values;

        if (request.Mutation == MailboxMutation.SetKeywords)
        {
            return RemoteEmailKeywords.Create(named);
        }

        if (request.Mutation == MailboxMutation.AddKeywords)
        {
            return RemoteEmailKeywords.Create(carried.Values.Concat(named));
        }

        var removed = named.Select(RemoteEmailKeywords.Normalized).ToHashSet(StringComparer.Ordinal);

        return RemoteEmailKeywords.Create(carried.Values.Where(keyword => !removed.Contains(keyword)));
    }

    private async Task<SubmittedMailboxChange> CommitLocallyAsync(
        IPersistenceSession session,
        MailboxMutationRequest request,
        MailboxDestination? destination,
        DateTimeOffset? heldUntil,
        LocalMailFolderHolding holding,
        LocalEmailState state,
        CancellationToken cancellationToken)
    {
        var mutation = request.Mutation;

        if (mutation == MailboxMutation.Copy)
        {
            // ponytail: a local copy is a second stored message with its own payload under ADR 0008 and ADR 0017, and
            // the payload has to be placed before the transaction that writes its row — which a rule's batch
            // transaction, already open here, cannot do. Refused until copy becomes a two-phase act of its own.
            return SubmittedMailboxChange.NotSubmitted(MailboxChangeSubmissionOutcome.NotAvailableLocally);
        }

        if (mutation == MailboxMutation.SetSeen || mutation == MailboxMutation.SetFlagged
            || mutation == MailboxMutation.AddKeywords || mutation == MailboxMutation.RemoveKeywords
            || mutation == MailboxMutation.SetKeywords)
        {
            var flagged = mutation == MailboxMutation.SetSeen ? state with { IsSeen = request.DesiredSeenState!.Value }
                : mutation == MailboxMutation.SetFlagged ? state with { IsFlagged = request.DesiredFlaggedState!.Value }
                : state with { Keywords = KeywordsAfter(request, state.Keywords) };

            return await this.CommitAsync(session, request, state, flagged, cancellationToken);
        }

        // Every other change moves the message, so the hierarchy is saved with it: a save is what makes the commit
        // conditional on the hierarchy it read, and a folder erased by an edit committing meanwhile would otherwise
        // receive a message after its erasure pass ended — the reason LocalMailFolderArrivals saves on every arrival.
        var found = holding.ToTree();
        var missing = found.MissingProtectedFolders(this.MintFolderId);
        var tree = found.With(missing);

        await this.localFolders.SaveAsync(session, request.Account, missing, [], cancellationToken);

        if (mutation == MailboxMutation.Delete)
        {
            if (state.Folder is { } current && tree.IsInTrash(current))
            {
                var record = await this.records.OpenAsync(session, request, heldUntil, cancellationToken);

                return SubmittedMailboxChange.Recorded(record);
            }

            var trash = tree.Folders.First(folder => folder.Role == MailFolderSpecialUse.Trash);

            return await this.CommitAsync(session, request, state, state with { Folder = trash.Id }, cancellationToken);
        }

        if (destination is null || CorrespondingFolder(tree, destination) is not { } target)
        {
            return SubmittedMailboxChange.NotSubmitted(MailboxChangeSubmissionOutcome.DestinationMissing);
        }

        if (state.Folder == target.Id)
        {
            return SubmittedMailboxChange.NotSubmitted(MailboxChangeSubmissionOutcome.AlreadyInDestination);
        }

        return await this.CommitAsync(session, request, state, state with { Folder = target.Id }, cancellationToken);
    }

    private async Task<SubmittedMailboxChange> CommitAsync(
        IPersistenceSession session,
        MailboxMutationRequest request,
        LocalEmailState before,
        LocalEmailState after,
        CancellationToken cancellationToken)
    {
        var now = this.timeProvider.GetUtcNow();

        if (this.auditSettings.GetAuditSettings(request.Occurrence.AccountId).IsEnabled)
        {
            await this.auditEntries.AppendAsync(
                session,
                MailboxMutationAuditEntry.OfLocalAct(
                    this.MintAuditEntryId(now),
                    MailboxMutationRecordId.Create(Guid.CreateVersion7(now)),
                    request,
                    before.SourceFolder,
                    now),
                cancellationToken);
        }

        await this.states.WriteAsync(session, request.Account, request.StoredEmailId, after, cancellationToken);

        var flags = request.Mutation == MailboxMutation.SetSeen || request.Mutation == MailboxMutation.SetFlagged
            ? new SignalledEmailFlags(request.StoredEmailId, request.DesiredSeenState, request.DesiredFlaggedState)
            : (SignalledEmailFlags?)null;

        return SubmittedMailboxChange.Applied(
            new AppliedMailboxChange(request.Account, before.SourceFolder.Alias, request.StoredEmailId, flags));
    }

    private MailboxMutationAuditEntryId MintAuditEntryId(DateTimeOffset now) =>
        MailboxMutationAuditEntryId.Create(Guid.CreateVersion7(now));

    private LocalMailFolderId MintFolderId() =>
        LocalMailFolderId.Create(Guid.CreateVersion7(this.timeProvider.GetUtcNow()));
}

/// <summary>What became of one submitted change.</summary>
public enum MailboxChangeSubmissionOutcome
{
    /// <summary>A durable record was written, which a convergence pass carries.</summary>
    Recorded = 0,

    /// <summary>The change was committed to stored state with its audit entry, and nothing is left to carry.</summary>
    Applied = 1,

    /// <summary>The held account no longer stores the message, so there was nothing to change.</summary>
    MessageMissing = 2,

    /// <summary>The destination corresponds to no local folder a message can be filed into.</summary>
    DestinationMissing = 3,

    /// <summary>The message is already in the destination, so nothing was written.</summary>
    AlreadyInDestination = 4,

    /// <summary>The change is one a held account cannot commit locally yet.</summary>
    NotAvailableLocally = 5,
}

/// <summary>The answer one submitted change produced.</summary>
/// <param name="Outcome">What became of the change.</param>
/// <param name="Record">The record written, for <see cref="MailboxChangeSubmissionOutcome.Recorded" /> alone.</param>
/// <param name="Change">What to announce once the transaction commits, for <see cref="MailboxChangeSubmissionOutcome.Applied" /> alone.</param>
public sealed record SubmittedMailboxChange(
    MailboxChangeSubmissionOutcome Outcome,
    MailboxMutationRecord? Record,
    AppliedMailboxChange? Change)
{
    /// <summary>Describes a change written down as a durable record.</summary>
    /// <param name="record">The record.</param>
    /// <returns>The answer.</returns>
    public static SubmittedMailboxChange Recorded(MailboxMutationRecord record) =>
        new(MailboxChangeSubmissionOutcome.Recorded, record ?? throw new ArgumentNullException(nameof(record)), Change: null);

    /// <summary>Describes a change committed to stored state.</summary>
    /// <param name="change">What to announce.</param>
    /// <returns>The answer.</returns>
    public static SubmittedMailboxChange Applied(AppliedMailboxChange change) =>
        new(MailboxChangeSubmissionOutcome.Applied, Record: null, change ?? throw new ArgumentNullException(nameof(change)));

    /// <summary>Describes a change nothing was written for.</summary>
    /// <param name="outcome">Why.</param>
    /// <returns>The answer.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="outcome" /> is one that writes something.</exception>
    public static SubmittedMailboxChange NotSubmitted(MailboxChangeSubmissionOutcome outcome) =>
        outcome is MailboxChangeSubmissionOutcome.Recorded or MailboxChangeSubmissionOutcome.Applied
            ? throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "A change that wrote something carries what it wrote.")
            : new(outcome, Record: null, Change: null);
}

/// <summary>One change committed to stored state, as the clients watching its account are told about it.</summary>
/// <param name="Account">The account the message is stored for.</param>
/// <param name="Folder">The folder alias a client lists the message under.</param>
/// <param name="Email">The message.</param>
/// <param name="Flags">Where a flag change left the message, or <see langword="null" /> for a change that is not about a flag.</param>
public sealed record AppliedMailboxChange(
    MailAccountIdentity Account,
    MailFolderAlias Folder,
    StoredEmailId Email,
    SignalledEmailFlags? Flags);
