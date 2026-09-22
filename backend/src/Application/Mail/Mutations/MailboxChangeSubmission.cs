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
/// On an account whose source is the truth, the change is written down as the durable record the account's convergence
/// pass carries, exactly as before. On a held account there is no server to carry it to, so it is committed to stored
/// state together with its audit entry, in the caller's transaction, and no record is written — a transaction either
/// happened or it did not, and there is no sequence for a crash to interrupt. The one exception is erasure: a delete of
/// a message already in the trash opens a record marked as the local erasure it is, held for the caller's window, so the
/// existing withdrawal and release routes keep acting on it and the cascade runs only once the window has passed.
/// </para>
/// <para>
/// A restoring account commits locally for the same reason a held one does — MailFathom is still the truth until the
/// mailbox has been appended back — and owes its source one thing more. Where the message holds an occurrence, never
/// drained or already appended, the same transaction opens the ordinary record the converger carries under ADR 0007, so
/// an act taken during the restore reaches the source instead of being undone by it; where the message holds none, the
/// local commit is the whole of the act and the restore carries it when it appends the message.
/// </para>
/// <para>
/// A copy is the one local change that takes two phases, because it creates a second stored message with a payload of
/// its own and a payload is placed with no transaction open across the placement. The caller places one through
/// <see cref="PrepareCopiesAsync" /> before it opens its transaction and hands it back in, which is the same shape the
/// destination folders already take and for the same reason.
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
    private readonly LocalMailCopier copier;
    private readonly IMailboxMutationAuditSettingsReader auditSettings;
    private readonly IMailboxMutationAuditEntryStore auditEntries;
    private readonly ClientSignals signals;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the submission over both executors and what a local change is audited and announced through.</summary>
    /// <param name="localFolders">Answers the account's custody phase and the local folders a held account keeps.</param>
    /// <param name="records">Opens the durable record a change on a mirrored account, and an erasure on a held one, is carried by.</param>
    /// <param name="states">Reads and writes the stored state a local change commits to.</param>
    /// <param name="copier">Places and writes the second stored message a local copy produces.</param>
    /// <param name="auditSettings">Answers whether the account keeps an audit trail.</param>
    /// <param name="auditEntries">Appends a local change's audit entry in the change's own transaction.</param>
    /// <param name="signals">Announces a local change to the clients watching the account.</param>
    /// <param name="timeProvider">Stamps a local change's audit entry and identity.</param>
    /// <exception cref="ArgumentNullException">Thrown when a collaborator is <see langword="null" />.</exception>
    public MailboxChangeSubmission(
        ILocalMailFolderStore localFolders,
        IMailboxMutationRecordStore records,
        ILocalEmailStateStore states,
        LocalMailCopier copier,
        IMailboxMutationAuditSettingsReader auditSettings,
        IMailboxMutationAuditEntryStore auditEntries,
        ClientSignals signals,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(localFolders);
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(states);
        ArgumentNullException.ThrowIfNull(copier);
        ArgumentNullException.ThrowIfNull(auditSettings);
        ArgumentNullException.ThrowIfNull(auditEntries);
        ArgumentNullException.ThrowIfNull(signals);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.localFolders = localFolders;
        this.records = records;
        this.states = states;
        this.copier = copier;
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
        this.SubmitAsync(session, request, destination, heldUntil, preparedCopy: null, cancellationToken);

    /// <summary>Records or commits one change, inside the caller's transaction, with the copy a held account would need.</summary>
    /// <param name="session">The transaction the change commits in.</param>
    /// <param name="request">The change asked for.</param>
    /// <param name="destination">Where a relocation or a copy files the message, resolved before the transaction opened; <see langword="null" /> for every other change.</param>
    /// <param name="heldUntil">How long a written record waits before a convergence pass may take it in hand.</param>
    /// <param name="preparedCopy">The payload a held copy was placed with, from <see cref="PrepareCopiesAsync" />; <see langword="null" /> for every other change and for an account that is not held.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The record written, the local change committed, or why neither happened.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> or <paramref name="request" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A caller hands the copy in rather than the submission placing one, because placing a payload must not happen with
    /// a transaction open across it and this is called inside one. Handing in nothing is what a caller that could not
    /// place one has, and a copy then answers <see cref="MailboxChangeSubmissionOutcome.SourceContentMissing" /> rather
    /// than committing a row naming a payload that was never placed.
    /// </remarks>
    public Task<SubmittedMailboxChange> SubmitAsync(
        IPersistenceSession session,
        MailboxMutationRequest request,
        MailboxDestination? destination,
        DateTimeOffset? heldUntil,
        PreparedLocalCopy? preparedCopy,
        CancellationToken cancellationToken) =>
        this.SubmitAsync(
            session,
            request,
            destination,
            heldUntil,
            refusesMoveIntoCurrentFolder: false,
            preparedCopy,
            cancellationToken);

    /// <summary>Places the payload of every copy a batch asks for, before the transaction that commits them opens.</summary>
    /// <param name="account">The account the batch acts on.</param>
    /// <param name="sources">The message each planned copy duplicates, one entry per copy, which may name one message twice.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The placed copies, or none at all where the account's source is the truth about its mailbox.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sources" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// The phase is read here as well as inside the transaction, and the two readings do different work: this one
    /// decides whether anything is worth placing, and the one inside decides what is committed. An account that stops
    /// being held in between has copies placed for it that nothing commits, which is the orphaned object the content
    /// reclamation pass takes; the reverse ordering would be a copy the transaction cannot make.
    /// </para>
    /// <para>
    /// A restoring account places one for the same reason a held one does: its stored mail is still what the local
    /// change commits against, so a copy there is a second stored message rather than a command for the source. Asking
    /// only for <see cref="MailAccountCustodyPhase.Held" /> would leave the transaction with nothing to commit and a
    /// rule's copy answering <see cref="MailboxChangeSubmissionOutcome.SourceContentMissing" /> for the whole restore.
    /// </para>
    /// </remarks>
    public async Task<PreparedLocalCopies> PrepareCopiesAsync(
        MailAccountId account,
        IReadOnlyCollection<StoredEmailId> sources,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sources);

        if (sources.Count == 0
            || await this.localFolders.ReadAsync(account, cancellationToken)
                is not { Phase: MailAccountCustodyPhase.Held or MailAccountCustodyPhase.Restoring })
        {
            return PreparedLocalCopies.None;
        }

        var placed = new Dictionary<StoredEmailId, IReadOnlyList<PreparedLocalCopy>>();

        foreach (var source in sources)
        {
            if (await this.copier.PrepareAsync(account, source, cancellationToken) is not { } copy)
            {
                continue;
            }

            placed[source] = placed.TryGetValue(source, out var already) ? [.. already, copy] : [copy];
        }

        return new PreparedLocalCopies(placed);
    }

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
        this.SubmitAsync(
            session,
            request,
            destination,
            heldUntil: null,
            refusesMoveIntoCurrentFolder: true,
            preparedCopy: null,
            cancellationToken);

    private async Task<SubmittedMailboxChange> SubmitAsync(
        IPersistenceSession session,
        MailboxMutationRequest request,
        MailboxDestination? destination,
        DateTimeOffset? heldUntil,
        bool refusesMoveIntoCurrentFolder,
        PreparedLocalCopy? preparedCopy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        var account = request.Account;
        var holding = await this.localFolders.ReadAsync(session, account, cancellationToken);

        if (holding is not { Phase: MailAccountCustodyPhase.Held or MailAccountCustodyPhase.Restoring })
        {
            if (refusesMoveIntoCurrentFolder
                && request.Mutation == MailboxMutation.Relocate
                && destination?.Alias == request.Occurrence.FolderResolutionId.Alias)
            {
                return SubmittedMailboxChange.NotSubmitted(MailboxChangeSubmissionOutcome.AlreadyInDestination);
            }

            var record = await this.records.OpenAsync(session, request, heldUntil, erasesLocalCopy: false, cancellationToken);

            return SubmittedMailboxChange.Recorded(record);
        }

        var state = await this.states.ReadAsync(session, account, request.StoredEmailId, cancellationToken);

        if (state is null)
        {
            return SubmittedMailboxChange.NotSubmitted(MailboxChangeSubmissionOutcome.MessageMissing);
        }

        return await this.CommitLocallyAsync(
            session,
            request,
            destination,
            heldUntil,
            holding,
            state,
            preparedCopy,
            cancellationToken);
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

        // The record was read at the start of the pass, and a person may have withdrawn it since. Advancing it here is the
        // claim: a stage only moves forward, so a cancelled record refuses and the transaction ends before the cascade.
        await this.records.AdvanceAsync(session, record.Id, MailboxMutationStage.Completed, placement: null, cancellationToken);

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

        // The erasing transaction also writes the source removal record naming the occurrence, so a later drain can
        // still remove the message from the source once this cascade has taken the row that said where it was.
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
            // ponytail: past MaximumKeywords the set gives up its ordinally greatest value, which on a held account is
            // the only copy of it; refuse an add that would pass the bound once the four requesters share an outcome for it.
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
        PreparedLocalCopy? preparedCopy,
        CancellationToken cancellationToken)
    {
        var mutation = request.Mutation;

        if (mutation == MailboxMutation.SetSeen || mutation == MailboxMutation.SetFlagged
            || mutation == MailboxMutation.AddKeywords || mutation == MailboxMutation.RemoveKeywords
            || mutation == MailboxMutation.SetKeywords)
        {
            var flagged = mutation == MailboxMutation.SetSeen ? state with { IsSeen = request.DesiredSeenState!.Value }
                : mutation == MailboxMutation.SetFlagged ? state with { IsFlagged = request.DesiredFlaggedState!.Value }
                : state with { Keywords = KeywordsAfter(request, state.Keywords) };

            return await this.CommitAsync(session, request, state, flagged, heldUntil, holding.Phase, cancellationToken);
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
                // Erasure is the one irreversible act, so it is only ever a person's second delete, held for their
                // window; a rule or a verdict meeting its own earlier delete again finds nothing left to do.
                if (request.Requester.Origin != MailboxMutationOrigin.Command)
                {
                    return SubmittedMailboxChange.NotSubmitted(MailboxChangeSubmissionOutcome.AlreadyInDestination);
                }

                var record = await this.records.OpenAsync(session, request, heldUntil, erasesLocalCopy: true, cancellationToken);

                return SubmittedMailboxChange.Recorded(record);
            }

            var trash = tree.Folders.First(folder => folder.Role == MailFolderSpecialUse.Trash);

            return await this.CommitAsync(
                session,
                request,
                state,
                state with { Folder = trash.Id },
                heldUntil,
                holding.Phase,
                cancellationToken);
        }

        if (destination is null || CorrespondingFolder(tree, destination) is not { } target)
        {
            return SubmittedMailboxChange.NotSubmitted(MailboxChangeSubmissionOutcome.DestinationMissing);
        }

        if (state.Folder == target.Id)
        {
            return SubmittedMailboxChange.NotSubmitted(MailboxChangeSubmissionOutcome.AlreadyInDestination);
        }

        // A copy answers both refusals above exactly as a move does, which is why it is asked after them rather than
        // before: filing a second message into the folder it is already in, or into a folder the account does not
        // have, is the same nothing either way.
        if (mutation == MailboxMutation.Copy)
        {
            return preparedCopy is null
                ? SubmittedMailboxChange.NotSubmitted(MailboxChangeSubmissionOutcome.SourceContentMissing)
                : await this.CommitCopyAsync(session, request, state, target.Id, preparedCopy, cancellationToken);
        }

        return await this.CommitAsync(
            session,
            request,
            state,
            state with { Folder = target.Id },
            heldUntil,
            holding.Phase,
            cancellationToken);
    }

    /// <summary>Writes the second stored message a copy produces, from the payload placed before this transaction.</summary>
    /// <remarks>
    /// <para>
    /// The audit entry names the copied message and the folder it was copied into, exactly as a move's does, because the
    /// act the trail records is the one somebody asked for rather than the row it produced. What is announced is the
    /// copy: a client told about the copied message would re-read a message that has not changed.
    /// </para>
    /// <para>
    /// It is appended after the copy is written rather than before it, which is where a move's differs. A local act's
    /// entry is always a performed change, and this is the one local act that can decline to write anything while its
    /// caller commits the transaction anyway — a rule records the refusal and carries on — so auditing first would
    /// leave the trail permanently stating a copy that produced no row.
    /// </para>
    /// <para>
    /// It is the one local change a restoring account owes its source no record for. A record carries an act to a
    /// message the source already holds, and the copy is a message the source has never seen: it holds no occurrence,
    /// which is exactly what the restore appends. Asking for a record beside it would ask the source twice.
    /// </para>
    /// </remarks>
    private async Task<SubmittedMailboxChange> CommitCopyAsync(
        IPersistenceSession session,
        MailboxMutationRequest request,
        LocalEmailState state,
        LocalMailFolderId target,
        PreparedLocalCopy preparedCopy,
        CancellationToken cancellationToken)
    {
        var copied = await this.copier.CommitAsync(
            session,
            preparedCopy,
            state.SourceFolder.Id,
            target,
            new CopiedMailFlags(state.IsSeen, state.IsFlagged, state.Keywords),
            cancellationToken);

        // Nothing written means the copied message's own binding went while this ran, which is the message going rather
        // than the destination: answering for the destination would send a rule's author to correct a name that is right.
        if (copied is null)
        {
            return SubmittedMailboxChange.NotSubmitted(MailboxChangeSubmissionOutcome.MessageMissing);
        }

        await this.AuditAsync(session, request, state, cancellationToken);

        return SubmittedMailboxChange.Applied(
            new AppliedMailboxChange(request.Account, state.SourceFolder.Alias, copied.Value, Flags: null));
    }

    /// <summary>Writes the change onto the stored message, and beside it the record a restoring account owes its source.</summary>
    /// <remarks>
    /// The record is opened in the same transaction as the commit, so a message acted on during the restore cannot end
    /// up committed locally with nothing to carry the act to the source — which is what would leave it arriving back in
    /// <see cref="MailAccountCustodyPhase.Mirrored" /> in the state the source last had. It is opened against the
    /// occurrence the requester resolved, because a restore appends only a message that holds none and therefore never
    /// moves the occurrence this request names.
    /// </remarks>
    private async Task<SubmittedMailboxChange> CommitAsync(
        IPersistenceSession session,
        MailboxMutationRequest request,
        LocalEmailState before,
        LocalEmailState after,
        DateTimeOffset? heldUntil,
        MailAccountCustodyPhase phase,
        CancellationToken cancellationToken)
    {
        await this.AuditAsync(session, request, before, cancellationToken);
        await this.states.WriteAsync(session, request.Account, request.StoredEmailId, after, cancellationToken);

        var carried = phase == MailAccountCustodyPhase.Restoring && before.HoldsSourceOccurrence
            ? await this.records.OpenAsync(session, request, heldUntil, erasesLocalCopy: false, cancellationToken)
            : null;

        var flags = request.Mutation == MailboxMutation.SetSeen || request.Mutation == MailboxMutation.SetFlagged
            ? new SignalledEmailFlags(request.StoredEmailId, request.DesiredSeenState, request.DesiredFlaggedState)
            : (SignalledEmailFlags?)null;

        return SubmittedMailboxChange.Applied(
            new AppliedMailboxChange(request.Account, before.SourceFolder.Alias, request.StoredEmailId, flags),
            carried);
    }

    /// <summary>Appends the audit entry one local act owes, where the account keeps a trail.</summary>
    private async Task AuditAsync(
        IPersistenceSession session,
        MailboxMutationRequest request,
        LocalEmailState before,
        CancellationToken cancellationToken)
    {
        if (!this.auditSettings.GetAuditSettings(request.Occurrence.AccountId).IsEnabled)
        {
            return;
        }

        var now = this.timeProvider.GetUtcNow();

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

    /// <summary>The change was committed to stored state with its audit entry, and carries a record only where a restoring account owes its source one.</summary>
    Applied = 1,

    /// <summary>The held account no longer stores the message, so there was nothing to change.</summary>
    MessageMissing = 2,

    /// <summary>The destination corresponds to no local folder a message can be filed into.</summary>
    DestinationMissing = 3,

    /// <summary>The message is already in the destination, so nothing was written.</summary>
    AlreadyInDestination = 4,

    /// <summary>The held account stores no payload for the message a copy would duplicate.</summary>
    /// <remarks>
    /// A copy is the one change that needs the message itself rather than only what is recorded about it, so it is the
    /// one a storage ceiling's deferral or an unreadable payload can refuse while every other change still commits.
    /// </remarks>
    SourceContentMissing = 5,
}

/// <summary>The answer one submitted change produced.</summary>
/// <param name="Outcome">What became of the change.</param>
/// <param name="Record">The record written, which a change committed locally also carries where the account owes its source one.</param>
/// <param name="Change">What to announce once the transaction commits, for <see cref="MailboxChangeSubmissionOutcome.Applied" /> alone.</param>
/// <remarks>
/// The outcome names what the requester tells its caller and the record names what convergence will carry, and the two
/// stopped being the same question once an account could be restoring: a change committed to stored state there is
/// applied exactly as a held account's is, and still leaves a record behind for the source.
/// </remarks>
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
    /// <param name="record">The record the account's source is still owed, or <see langword="null" /> where there is nothing to carry.</param>
    /// <returns>The answer.</returns>
    public static SubmittedMailboxChange Applied(AppliedMailboxChange change, MailboxMutationRecord? record = null) =>
        new(MailboxChangeSubmissionOutcome.Applied, record, change ?? throw new ArgumentNullException(nameof(change)));

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
    MailAccountId Account,
    MailFolderAlias Folder,
    StoredEmailId Email,
    SignalledEmailFlags? Flags);
