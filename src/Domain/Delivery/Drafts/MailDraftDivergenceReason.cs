// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Delivery.Drafts;

/// <summary>Names why a copy MailFathom appended stopped being one it can still show is its own.</summary>
/// <remarks>
/// <para>
/// The whole point of tracking a draft's copy is that MailFathom may replace and remove that copy and nothing else. The
/// moment the tracked occurrence stops being provably the one that was appended, the honest act is to leave it where it
/// is — a draft the owner may be working on is worth more than a folder without a stray message in it — and to say why,
/// so an operator reading the record is not left to guess which of these happened.
/// </para>
/// <para>
/// Each member is a different fact with a different remedy, which is why they are not collapsed into one. One is the
/// deployment's own configuration moving, one is a server capability that was always this way, one is the server
/// renumbering a folder, and one is a command whose answer never came back.
/// </para>
/// <para>
/// The reason is stored as its name, for the reason every stage in this system is.
/// </para>
/// </remarks>
public enum MailDraftDivergenceReason
{
    /// <summary>The append went out and the server's answer to it never came back, so nothing names the copy.</summary>
    AppendOutcomeUnknown = 0,

    /// <summary>The server accepted the append and named no placement, which a server advertising no <c>UIDPLUS</c> does.</summary>
    /// <remarks>
    /// The copy is in the folder and there is no occurrence to point at, so a replacement would append beside it and a
    /// removal would have to search for something that looks like the message. Both are guesses about identity.
    /// </remarks>
    PlacementUnreported = 1,

    /// <summary>The drafts role now resolves to a different folder than the copy was appended to.</summary>
    /// <remarks>An alias repointed by an operator's edit is the ordinary cause, and their edit is what changes it back.</remarks>
    DestinationChanged = 2,

    /// <summary>The folder reports a different UIDVALIDITY than the append did, so the recorded UID names somebody else's message.</summary>
    FolderRecreated = 3,
}
