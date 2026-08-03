// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Synchronization.Reconciliation;

/// <summary>Pairs one locally stored email with the flags the server reported for it.</summary>
/// <param name="StoredEmailId">The local identity to write the snapshot onto.</param>
/// <param name="Snapshot">What the server reported, and when it was read.</param>
public sealed record ObservedEmailFlags(StoredEmailId StoredEmailId, RemoteEmailFlagSnapshot Snapshot);

/// <summary>Everything one reconciliation window learned, as one thing to apply.</summary>
/// <param name="StillPresent">The emails the folder still holds, with the flags to write onto them.</param>
/// <param name="Disappeared">The emails the folder no longer holds.</param>
/// <param name="Disposition">What becomes of the local copy of each disappeared email.</param>
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
/// </remarks>
public sealed record ReconciledFolderOutcome(
    IReadOnlyList<ObservedEmailFlags> StillPresent,
    IReadOnlyList<StoredEmailId> Disappeared,
    RemotelyDeletedEmailDisposition Disposition,
    DateTimeOffset ObservedAt);
