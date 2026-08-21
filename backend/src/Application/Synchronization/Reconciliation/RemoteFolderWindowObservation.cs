// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Synchronization.Reconciliation;

/// <summary>What the mail server said about one bounded window of occurrences the local copy already holds.</summary>
/// <param name="Observations">The occurrences the server described, with the flags it reported for each.</param>
/// <param name="UnchangedUids">
/// The occurrences the server proved it still holds without describing them, because nothing about them has changed
/// since the modification sequence the caller supplied.
/// </param>
/// <param name="FolderHighestModSeq">
/// The folder's highest modification sequence at the moment of the read, or <see langword="null" /> when the server
/// reports none. A caller records it only after a pass that covered the whole folder.
/// </param>
/// <remarks>
/// <para>
/// The two lists together are the folder's answer about what it still holds, and a UID in neither of them is one the
/// folder no longer holds. That is the whole detection mechanism for a message deleted on the server, so an
/// implementation may place a UID in <paramref name="UnchangedUids" /> only where the server positively said the
/// message is still there — a listing of the surviving identifiers, or a vanished report that named the ones it is not
/// among. Silence is never enough: a modification-sequence-limited fetch says nothing about a message that was deleted
/// and nothing about one that did not change, and treating the second answer as the first would leave a deleted
/// message stored forever.
/// </para>
/// <para>
/// Nothing here carries a message. A UID, a flag snapshot, and a sequence number are what deciding existence needs, and
/// the read that produces them cannot set the remote <c>\Seen</c> flag.
/// </para>
/// </remarks>
public sealed record RemoteFolderWindowObservation(
    IReadOnlyList<RemoteEmailFlagObservation> Observations,
    IReadOnlyList<ImapUid> UnchangedUids,
    ulong? FolderHighestModSeq)
{
    /// <summary>Reports a window every occurrence of which the server was asked to describe.</summary>
    /// <param name="observations">The occurrences the server answered for.</param>
    /// <param name="folderHighestModSeq">The folder's highest modification sequence, when it reports one.</param>
    /// <returns>An observation whose surviving occurrences are exactly the ones described.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="observations" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// This is the shape of the full window scan, where an occurrence is either described or gone and there is no third
    /// answer. It exists so that path cannot accidentally claim an occurrence is unchanged.
    /// </remarks>
    public static RemoteFolderWindowObservation FromDescribedOccurrences(
        IReadOnlyList<RemoteEmailFlagObservation> observations,
        ulong? folderHighestModSeq)
    {
        ArgumentNullException.ThrowIfNull(observations);

        return new RemoteFolderWindowObservation(observations, [], folderHighestModSeq);
    }
}
