// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;

namespace MailFathom.Domain.Delivery.Drafts;

/// <summary>Reports one copy of a draft that MailFathom put into the drafts folder, and what has become of it since.</summary>
/// <remarks>
/// <para>
/// One row per revision of the draft, which is what makes a replacement expressible at all. IMAP has no command that
/// changes a stored message, so a new version of a draft is a new message beside the old one and the old one is removed
/// afterwards — and between those two commands the folder holds two copies that both belong to this draft. A single
/// slot could not say that, and a slot overwritten by the newer copy would lose the only thing naming the one still to
/// be taken out.
/// </para>
/// <para>
/// The occurrence recorded here is the server's own statement about the copy it accepted, never a search for something
/// that looks like the message. That is the whole of what makes the removal safe: the only UID this system ever expunges
/// is one an <c>APPEND</c> of its own reported, so a draft the owner wrote themselves is unreachable from here by
/// construction rather than by a check.
/// </para>
/// <para>
/// Nothing here is mail content. A folder, an alias, a UID, and an identity MailFathom minted itself are its own or the
/// server's names for things.
/// </para>
/// </remarks>
public sealed record MailDraftServerCopy
{
    /// <summary>Gets which revision of the draft this copy carries.</summary>
    public required int Revision { get; init; }

    /// <summary>Gets MailFathom's own name for the folder the copy went into, which is what a failure and a log line name.</summary>
    public required MailFolderAlias FolderAlias { get; init; }

    /// <summary>Gets the remote path the copy was appended to, which is what a later attempt compares the resolution against.</summary>
    public required RemoteFolderPath FolderPath { get; init; }

    /// <summary>Gets what has become of the copy.</summary>
    public required MailDraftCopyStage Stage { get; init; }

    /// <summary>Gets where the folder put the copy, as far as the server said.</summary>
    /// <remarks>
    /// It is <see cref="RemoteEmailPlacement.NotReported" /> both before the append is confirmed and after a server
    /// advertising no <c>UIDPLUS</c> confirmed it. <see cref="Stage" /> is what tells the two apart.
    /// </remarks>
    public required RemoteEmailPlacement Placement { get; init; }

    /// <summary>Gets the <c>Message-ID</c> the appended copy carries, or <see langword="null" /> while none was read.</summary>
    /// <remarks>
    /// It is read back off the bytes that were appended rather than assumed, so the value recorded is the one a mail
    /// server will report. Nothing removes a copy by it — a removal names a UID — but it is what lets an operator find
    /// the message a divergence left behind.
    /// </remarks>
    public required string? InternetMessageId { get; init; }

    /// <summary>Gets when the append was issued.</summary>
    public required DateTimeOffset AppendedAt { get; init; }

    /// <summary>Gets when the copy stopped standing in the folder, or <see langword="null" /> while it still does.</summary>
    public required DateTimeOffset? SettledAt { get; init; }

    /// <summary>Gets whether the append went out and the server's answer to it never came back.</summary>
    public bool HasUnknownOutcome => this.Stage == MailDraftCopyStage.Issued;

    /// <summary>Gets whether the folder holds this copy as far as MailFathom knows.</summary>
    public bool IsStanding => this.Stage == MailDraftCopyStage.Standing;

    /// <summary>Gets whether MailFathom can still name this copy to a server well enough to remove it.</summary>
    /// <remarks>
    /// Both halves are required. A copy the server named no placement for is in the folder and cannot be pointed at, and
    /// a copy whose append was never answered may not even be there — so neither is removable, and both are left as the
    /// owner's with the divergence recorded.
    /// </remarks>
    public bool IsRemovable => this.IsStanding && this.Placement is { UidValidity: not null, Uid: not null };

    /// <summary>Reports whether this copy still names the folder a fresh resolution of the drafts role points at.</summary>
    /// <param name="resolvedFolderPath">The remote path the drafts role resolves to now.</param>
    /// <returns><see langword="true" /> when the copy is in the folder the role currently means.</returns>
    /// <remarks>
    /// An alias repointed since the append names another folder, and the recorded UID names a message in it that
    /// MailFathom never put there. Comparing the path before anything is issued is what keeps a removal from reaching
    /// somebody else's mail.
    /// </remarks>
    public bool NamesFolder(RemoteFolderPath resolvedFolderPath) =>
        this.FolderPath.NamesSameFolderAs(resolvedFolderPath);
}
