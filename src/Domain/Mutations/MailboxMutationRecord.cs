// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;

namespace MailFathom.Domain.Mutations;

/// <summary>Reports what one durable mutation record holds: the change that was asked for and how far it has got.</summary>
/// <remarks>
/// <para>
/// The record is written before the first IMAP command and advanced as the sequence proceeds, which is what makes a
/// non-atomic sequence idempotent: a retry reads it and continues from the stage it names rather than starting over.
/// It is also what lets a change MailFathom made be told apart from the same change made by hand, which is a separate
/// reader of the same record rather than a second mechanism.
/// </para>
/// <para>
/// It is derived personal data. A mutation history says where a person's mail has been and what was done to it, so it
/// inherits the retention and deletion obligations of the email it describes and is removed with it.
/// </para>
/// </remarks>
public sealed record MailboxMutationRecord
{
    /// <summary>Gets what everything after the first write refers to this record by.</summary>
    public required MailboxMutationRecordId Id { get; init; }

    /// <summary>Gets the change that was asked for, restored exactly as it was written down.</summary>
    public required MailboxMutationRequest Request { get; init; }

    /// <summary>Gets how far along its protocol sequence the mutation has durably reached.</summary>
    public required MailboxMutationStage Stage { get; init; }

    /// <summary>Gets where the destination folder put the email, as far as the server has said.</summary>
    /// <remarks>
    /// It is <see cref="RemoteEmailPlacement.NotReported" /> both before the placement is confirmed and after a server
    /// that supplied no <c>COPYUID</c> response confirmed it. The two are told apart by <see cref="Stage" />, which is
    /// the value that says whether the placement has happened at all.
    /// </remarks>
    public required RemoteEmailPlacement Placement { get; init; }

    /// <summary>Gets whether the placement left a source occurrence that still has to be removed separately.</summary>
    /// <remarks>
    /// <para>
    /// Written when the placement command is issued and read by a resumed attempt, because it is the one thing about a
    /// half-finished relocation that cannot be worked out later. A relocation carried by <c>MOVE</c> removes the source
    /// as part of the same command and a relocation carried by copy does not, so a record stopped at
    /// <see cref="MailboxMutationStage.PlacementConfirmed" /> means opposite things depending on which ran.
    /// </para>
    /// <para>
    /// Re-deriving it from what the connection advertises at resume time is exactly what this replaces. A recovered
    /// connection can land on a server advertising something else, and a fallback relocation resumed against one
    /// reporting <c>MOVE</c> would be read as already complete — leaving the email in both folders permanently, with
    /// nothing left to surface it. That is the duplication this record exists to prevent, so the answer is durable
    /// rather than inferred.
    /// </para>
    /// <para>
    /// It is a fact about the sequence rather than a name for the operation. Which protocol path carried a relocation
    /// still reaches no log above <c>Debug</c>, no span, and no metric dimension, exactly as
    /// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0007-remote-mailbox-mutation-boundary-and-write-session.md">ADR 0007</see>
    /// requires.
    /// </para>
    /// </remarks>
    public required bool RequiresSourceRemoval { get; init; }

    /// <summary>Gets how many times this mutation has been attempted, counted before each attempt rather than after it.</summary>
    /// <remarks>
    /// Counting first is what makes the bound survive a crash loop: an attempt that kills the process still counted, so a
    /// mutation that crashes the host every time reaches its terminal stage instead of being retried forever.
    /// </remarks>
    public required int AttemptCount { get; init; }

    /// <summary>Gets when the intent was first written down.</summary>
    public required DateTimeOffset RecordedAt { get; init; }

    /// <summary>Gets when the record last moved, which is what says how long a stuck mutation has been stuck.</summary>
    public required DateTimeOffset StageChangedAt { get; init; }

    /// <summary>Gets the failure the last attempt ended in, or <see langword="null" /> while no attempt has failed.</summary>
    /// <remarks>
    /// The code is kept and the message is not. A code is a stable identity an operator can look up, while a message is
    /// text assembled at the failure site, and the record is read by an operator asking which mutations are stuck rather
    /// than by anybody re-reading a log line.
    /// </remarks>
    public required MailFathomErrorCode? LastFailure { get; init; }

    /// <summary>Gets when synchronization recognized the occurrence this mutation created, or <see langword="null" /> while it has not.</summary>
    /// <remarks>
    /// The stage says what the server acknowledged and this says what synchronization has since seen come back, which
    /// are different questions with different answers. A relocation is acknowledged the moment the server confirms it,
    /// and the message still arrives in the destination folder later as an ordinary discovery that something has to
    /// recognize as this mutation's own.
    /// </remarks>
    public required DateTimeOffset? PlacementObservedAt { get; init; }

    /// <summary>Gets when synchronization saw the source occurrence leave its folder, or <see langword="null" /> while it has not.</summary>
    public required DateTimeOffset? SourceRemovalObservedAt { get; init; }

    /// <summary>Gets whether the record has reached a stage nothing moves it out of.</summary>
    public bool IsTerminal => this.Stage is MailboxMutationStage.Completed or MailboxMutationStage.Abandoned;

    /// <summary>Gets where in its lifecycle the mutation stands, in the reading somebody watching a deployment asks for.</summary>
    public MailboxMutationLifecycle Lifecycle => MailboxMutationLifecycle.Of(this.Stage);

    /// <summary>Gets whether the one command that may never be issued twice went out and its answer never came back.</summary>
    /// <remarks>
    /// A record here is not resumed. Reissuing the placement would put a second message in the destination folder, and
    /// nothing in that folder afterwards distinguishes a copy MailFathom made from one a person made, so the outcome is
    /// re-established from what the mailbox now shows or the record is given up on visibly.
    /// </remarks>
    public bool HasUnknownOutcome => this.Stage == MailboxMutationStage.PlacementIssued;

    /// <summary>Reports whether an unacknowledged placement has since been settled by the source occurrence leaving its folder.</summary>
    /// <remarks>
    /// <para>
    /// It answers for one shape only, and the narrowness is the point. A relocation carried by <c>MOVE</c> removes the
    /// source as part of the same command, so a source that has gone is the server's own statement that the command
    /// ran — a fact about an occurrence the record names exactly, rather than a guess about which message in the
    /// destination folder is the one that was placed. A copy and a fallback relocation both leave the source where it
    /// was, so its presence says nothing about either and neither is settled here.
    /// </para>
    /// <para>
    /// The one way this reads wrong is an owner who deleted the source message themselves between the command going out
    /// and the folder being read again, which would credit the move for a disappearance it did not cause. The mutation
    /// is completed anyway, deliberately: the email has left the source folder either way, nothing is duplicated by
    /// saying so, and reissuing a <c>MOVE</c> against a UID the folder no longer holds could only fail. It is the same
    /// direction <see cref="AccountsForRemovalOf" /> already takes for the same reason.
    /// </para>
    /// </remarks>
    public bool IsUnknownPlacementSettledBySourceRemoval =>
        this.HasUnknownOutcome
        && this.Request.Mutation == MailboxMutation.Relocate
        && !this.RequiresSourceRemoval
        && this.SourceRemovalObservedAt is not null;

    /// <summary>Gets whether this mutation puts the email somewhere synchronization will later discover it.</summary>
    /// <remarks>
    /// A copy places an occurrence exactly as a relocation does, and is included for that reason: the discovery is
    /// MailFathom's own act either way. What a copy does not do is carry the local row across, because a second live
    /// occurrence of one message is a second local email — which
    /// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0008-copied-message-local-identity.md">ADR 0008</see>
    /// decided and which is why <see cref="IsPlacementOf" /> narrows to a relocation and this does not.
    /// </remarks>
    public bool ExpectsPlacementObservation =>
        this.Request.Mutation == MailboxMutation.Relocate || this.Request.Mutation == MailboxMutation.Copy;

    /// <summary>Gets whether this mutation takes the source occurrence out of the folder it was in.</summary>
    public bool ExpectsSourceRemovalObservation =>
        this.Request.Mutation == MailboxMutation.Relocate || this.Request.Mutation == MailboxMutation.Delete;

    /// <summary>Gets whether synchronization has accounted for every occurrence this mutation moved.</summary>
    /// <remarks>
    /// This is the terminal state of the join rather than of the protocol sequence, and the two are reached at
    /// different moments. <see cref="MailboxMutationStage.Completed" /> says the server did what was asked; this says
    /// the local mailbox has stopped owing anything about it, which is what takes the record out of the candidates a
    /// later discovery is matched against.
    /// </remarks>
    public bool IsReconciled =>
        (!this.ExpectsPlacementObservation || this.PlacementObservedAt is not null)
        && (!this.ExpectsSourceRemovalObservation || this.SourceRemovalObservedAt is not null);

    /// <summary>Reports whether a newly discovered occurrence is one this mutation put there.</summary>
    /// <param name="discoveredFolderPath">The remote path of the folder the occurrence was discovered in.</param>
    /// <param name="discoveredUidValidity">The UIDVALIDITY that folder reports now.</param>
    /// <param name="discoveredUid">The UID the discovered occurrence carries.</param>
    /// <returns><see langword="true" /> when the server itself named this occurrence as where it put the email.</returns>
    /// <remarks>
    /// This is the provenance question, and it is asked of every mutation that places an occurrence rather than only of
    /// the one that also carries the local row across. A message MailFathom put in a folder arrives at the forward pass
    /// as an ordinary discovery, and whether it was moved or copied there changes nothing about whose act it was.
    /// </remarks>
    public bool AccountsForPlacementAt(
        RemoteFolderPath discoveredFolderPath,
        ImapUidValidity discoveredUidValidity,
        ImapUid discoveredUid) =>
        this.ExpectsPlacementObservation
        && this.PlacementObservedAt is null
        && this.Stage == MailboxMutationStage.Completed
        && this.Request.DestinationPath is { } destinationPath
        && string.Equals(destinationPath.Value, discoveredFolderPath.Value, StringComparison.Ordinal)
        && this.Placement is { UidValidity: { } placedUidValidity, Uid: { } placedUid }
        && placedUidValidity == discoveredUidValidity
        && placedUid == discoveredUid;

    /// <summary>Reports whether the remote <c>\Seen</c> flag a folder just reported stands where this mutation set it.</summary>
    /// <param name="occurrence">The occurrence whose flag changed.</param>
    /// <param name="observedSeenState">The <c>\Seen</c> value the server has now reported.</param>
    /// <param name="previouslyObservedAt">When synchronization last read this occurrence's flags before the reading being judged.</param>
    /// <returns><see langword="true" /> when this mutation is what moved the flag to that value.</returns>
    /// <remarks>
    /// <para>
    /// The direction is compared as well as the occurrence, so a record that asked for the flag to be set accounts for
    /// the flag becoming set and never for it becoming clear. A rule that marks mail read is therefore not re-triggered
    /// by its own store, while the owner clearing that flag afterwards reaches evaluation as the change it is.
    /// </para>
    /// <para>
    /// A record still at <see cref="MailboxMutationStage.Recorded" /> matches nothing, because no <c>STORE</c> has
    /// reached the server for it and the flag standing where it would have put it is somebody else's doing.
    /// </para>
    /// <para>
    /// <paramref name="previouslyObservedAt" /> is what scopes the answer to the one change this record describes, and
    /// it is the occurrence's own observation rather than a mark on this row. A <c>\Seen</c> store reaches
    /// synchronization only as a value the flag now stands at, so the reading that first sees the mailbox after the
    /// store is the whole of what this record can account for — every reading after that is a mailbox somebody else has
    /// had the chance to change. Anchoring on the row instead would answer only for the readings that happened to
    /// differ: an owner who reverted the flag before the first reading would leave the record unspent and have their
    /// own later change silenced by it.
    /// </para>
    /// </remarks>
    public bool AccountsForSeenStateOf(
        EmailOccurrenceId occurrence,
        bool observedSeenState,
        DateTimeOffset previouslyObservedAt) =>
        this.Request.Mutation == MailboxMutation.SetSeen
        && this.Stage != MailboxMutationStage.Recorded
        && previouslyObservedAt < this.StageChangedAt
        && this.Request.Occurrence == occurrence
        && this.Request.DesiredSeenState == observedSeenState;

    /// <summary>Reports whether a newly discovered occurrence is the one this mutation's placement created.</summary>
    /// <param name="discoveredFolderPath">The remote path of the folder the occurrence was discovered in.</param>
    /// <param name="discoveredUidValidity">The UIDVALIDITY that folder reports now.</param>
    /// <param name="discoveredUid">The UID the discovered occurrence carries.</param>
    /// <returns><see langword="true" /> when the server itself named this occurrence as where it put the email.</returns>
    /// <remarks>
    /// <para>
    /// The join is the server's own <c>COPYUID</c> answer and nothing else. A record that carries no reported placement
    /// matches nothing here, because the only way to find the message without one is to search the destination folder
    /// for something that looks like it — which is a guess about identity rather than a fact, and is exactly what
    /// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0007-remote-mailbox-mutation-boundary-and-write-session.md">ADR 0007</see>
    /// refuses. A relocation whose placement was never reported leaves the record awaiting its observation, which is
    /// visible rather than guessed at.
    /// </para>
    /// <para>
    /// The UIDVALIDITY is compared as well as the UID, so a destination folder recreated between the placement and the
    /// discovery matches nothing: the recorded UID names a message in a UID space the folder no longer has.
    /// </para>
    /// <para>
    /// The stage has to be <see cref="MailboxMutationStage.Completed" />. Anything earlier means the sequence still owes
    /// a command — a fallback relocation stopped after its copy still has the email in both folders — and carrying the
    /// local row into the destination then would leave the source occurrence with nothing local pointing at it while
    /// convergence still has to remove it.
    /// </para>
    /// <para>
    /// A copy is placed by <see cref="AccountsForPlacementAt" /> and never by this, because a copy leaves the email
    /// where it was: carrying its row across would move an email that never moved.
    /// </para>
    /// </remarks>
    public bool IsPlacementOf(
        RemoteFolderPath discoveredFolderPath,
        ImapUidValidity discoveredUidValidity,
        ImapUid discoveredUid) =>
        this.Request.Mutation == MailboxMutation.Relocate
        && this.AccountsForPlacementAt(discoveredFolderPath, discoveredUidValidity, discoveredUid);

    /// <summary>Reports whether an occurrence that has left its folder left it because of this mutation.</summary>
    /// <param name="sourceOccurrence">The occurrence the folder no longer holds.</param>
    /// <returns><see langword="true" /> when this mutation is what took the occurrence out of the folder.</returns>
    /// <remarks>
    /// <para>
    /// This half needs no <c>COPYUID</c>, because the record names the source occurrence exactly: it is the identity an
    /// IMAP command was issued against, written down before the command went out. All four components are compared, so
    /// a folder renumbered under a new UIDVALIDITY matches nothing.
    /// </para>
    /// <para>
    /// A record still at <see cref="MailboxMutationStage.Recorded" /> matches nothing, because nothing has reached the
    /// server for it and a disappearance is therefore somebody else's act. Every later stage matches, including
    /// <see cref="MailboxMutationStage.Abandoned" />, and that direction is deliberate: an abandoned relocation may
    /// have placed the email in the destination folder before it ran out of attempts, and treating the vanished source
    /// as a remote deletion would erase the local copy of mail that still exists. Attributing one manual deletion to
    /// MailFathom keeps mail nobody asked to keep; the other way round loses mail.
    /// </para>
    /// <para>
    /// It stays true after the observation has been recorded. The record is a durable fact about that occurrence rather
    /// than a pending item, so a folder asked about the same disappearance twice gets the same answer both times.
    /// </para>
    /// </remarks>
    public bool AccountsForRemovalOf(EmailOccurrenceId sourceOccurrence) =>
        this.ExpectsSourceRemovalObservation
        && this.Stage != MailboxMutationStage.Recorded
        && this.Request.Occurrence == sourceOccurrence;
}
