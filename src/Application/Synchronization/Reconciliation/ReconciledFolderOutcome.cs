// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;
using MailFathom.Domain.Mutations;

namespace MailFathom.Application.Synchronization.Reconciliation;

/// <summary>Pairs one locally stored email with the flags the server reported for it.</summary>
/// <param name="StoredEmailId">The local identity to write the snapshot onto.</param>
/// <param name="Snapshot">What the server reported, and when it was read.</param>
public sealed record ObservedEmailFlags(StoredEmailId StoredEmailId, RemoteEmailFlagSnapshot Snapshot);

/// <summary>Pairs one occurrence the folder no longer holds with the change MailFathom made that took it out.</summary>
/// <param name="StoredEmailId">The local identity whose occurrence has gone.</param>
/// <param name="MutationRecordId">The durable record of the change that removed it.</param>
/// <param name="Mutation">
/// Which change it was, carried here so the suppression a run reports names the same word its log line and its counter
/// already use rather than sending a reader back to the record to find out.
/// </param>
/// <param name="LocalDisposition">
/// What the delete decided about the local copy, and <see langword="null" /> for every other mutation. It is read off
/// the record rather than from the account's configuration, because the configuration says what a delete authored
/// *now* would do and this window is applying one authored earlier.
/// </param>
public sealed record MutationAttributedDisappearance(
    StoredEmailId StoredEmailId,
    MailboxMutationRecordId MutationRecordId,
    MailboxMutation Mutation,
    AuthoredDeleteEmailDisposition? LocalDisposition);

/// <summary>Everything one reconciliation window learned, as one thing to apply.</summary>
/// <param name="StillPresent">The emails the folder still holds, with the flags to write onto them.</param>
/// <param name="ConfirmedUnchanged">
/// The emails the folder still holds and reported no change to, so the stored flags already describe them and only the
/// record of when they were last asked about moves.
/// </param>
/// <param name="Disappeared">The emails the folder no longer holds and nothing MailFathom did accounts for.</param>
/// <param name="RemovedByOwnMutation">
/// The emails the folder no longer holds because MailFathom itself relocated or deleted them, each named with the record
/// that says so. They are separated from <paramref name="Disappeared" /> before <paramref name="Disposition" /> is
/// reached, because that setting answers what becomes of mail somebody else deleted and these are not that. A relocation
/// moves the queue timestamp and nothing else, leaving the row for the placement to carry across; a delete additionally
/// applies the disposition its own record carries, which is the one the owner authored it under.
/// </param>
/// <param name="Disposition">
/// What becomes of the local copy of each email in <paramref name="Disappeared" />. It never reaches
/// <paramref name="RemovedByOwnMutation" />, which is the whole point of the split: an account configured to erase what
/// its server loses must not thereby erase what MailFathom itself was told to delete.
/// </param>
/// <param name="ObservedAt">When this window was read, which orders it against what other writers have recorded.</param>
/// <remarks>
/// <para>
/// The window travels as one value rather than as a call per email so that applying it is one bounded set of database
/// work instead of a query per row inside an open write transaction. It is also what makes the window atomic: a run
/// either records what it found or records none of it.
/// </para>
/// <para>
/// <paramref name="ObservedAt" /> is the ordering key an implementation compares against what is already stored, so a
/// window replayed after a commit conflict cannot overwrite an observation that is newer than itself. Every snapshot in
/// <paramref name="StillPresent" /> carries its own reading of the same moment, taken where the server answered.
/// </para>
/// <para>
/// <paramref name="ConfirmedUnchanged" /> is applied like the rest and is not an optimization an implementation may
/// skip. The observation timestamp is what moves an email to the back of the reconciliation queue, so an email the
/// server confirmed and this window left untouched would be selected again on every run and the window would never
/// reach anything else.
/// </para>
/// </remarks>
public sealed record ReconciledFolderOutcome(
    IReadOnlyList<ObservedEmailFlags> StillPresent,
    IReadOnlyList<StoredEmailId> ConfirmedUnchanged,
    IReadOnlyList<StoredEmailId> Disappeared,
    IReadOnlyList<MutationAttributedDisappearance> RemovedByOwnMutation,
    RemotelyDeletedEmailDisposition Disposition,
    DateTimeOffset ObservedAt);
