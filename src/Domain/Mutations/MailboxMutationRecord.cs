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

    /// <summary>Gets whether this mutation puts the email somewhere synchronization will later discover it.</summary>
    /// <remarks>
    /// A copy places one too, and is deliberately absent: whether a second live occurrence of one message is one local
    /// row or two is the copy action's decision rather than this join's, so nothing here carries a row across for one.
    /// </remarks>
    public bool ExpectsPlacementObservation => this.Request.Mutation == MailboxMutation.Relocate;

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
    /// </remarks>
    public bool IsPlacementOf(
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
