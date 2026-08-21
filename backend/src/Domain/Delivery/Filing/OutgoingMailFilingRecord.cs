// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;

namespace MailFathom.Domain.Delivery.Filing;

/// <summary>Reports one copy of an outgoing message MailFathom put into a folder, and what has become of it since.</summary>
/// <remarks>
/// <para>
/// It hangs off the outgoing record rather than standing on its own, because the outgoing record is what an operator,
/// <c>mfctl</c>, and the MCP surface already read as the truth about a message MailFathom is sending. A filing is one
/// more thing that record says about it — where a copy of it is — and reading provenance through the same record is
/// what keeps this from being a second mechanism beside the one mutations already use.
/// </para>
/// <para>
/// The copy comes back through synchronization as ordinary new mail, and this is the whole of what tells it apart from
/// somebody else's message. Where the server advertises <c>UIDPLUS</c> the <c>APPENDUID</c> response names the
/// occurrence exactly and <see cref="AccountsForPlacementAt" /> is the join; where it does not,
/// <see cref="AccountsForMessageAt" /> falls back to the identity the message carries in its own headers, which is the
/// nearest thing to a fact available once the server has declined to say where it put the copy.
/// </para>
/// <para>
/// Nothing here is mail content. A folder, an alias, a UID, and a message identity MailFathom minted itself are its own
/// or the server's names for things, which is what lets the row be written and read without the message.
/// </para>
/// </remarks>
public sealed record OutgoingMailFilingRecord
{
    /// <summary>Gets the outgoing record this copy was filed from.</summary>
    public required OutgoingEmailId OutgoingEmailId { get; init; }

    /// <summary>Gets which place in the mailbox this copy was filed into.</summary>
    public required OutgoingMailFiling Filing { get; init; }

    /// <summary>Gets MailFathom's own name for the folder the copy went into, which is what a failure and a log line name.</summary>
    public required MailFolderAlias FolderAlias { get; init; }

    /// <summary>Gets the remote path the copy was appended to, which is what the join compares a discovery against.</summary>
    public required RemoteFolderPath FolderPath { get; init; }

    /// <summary>Gets how far the append has durably got.</summary>
    public required OutgoingMailFilingStage Stage { get; init; }

    /// <summary>Gets where the folder put the copy, as far as the server said.</summary>
    /// <remarks>
    /// It is <see cref="RemoteEmailPlacement.NotReported" /> both before the append is confirmed and after a server that
    /// advertises no <c>UIDPLUS</c> confirmed it. <see cref="Stage" /> is what tells the two apart.
    /// </remarks>
    public required RemoteEmailPlacement Placement { get; init; }

    /// <summary>Gets the <c>Message-ID</c> the appended copy carries, or <see langword="null" /> while none was read.</summary>
    /// <remarks>
    /// It is the identity MailFathom minted for the message itself, read back off the bytes that were appended rather
    /// than assumed, so the value recorded is the one a mail server will report. It is kept even where a placement was
    /// reported, because a folder recreated between the append and the discovery invalidates the UID and leaves this as
    /// the only thing still naming the copy.
    /// </remarks>
    public required string? InternetMessageId { get; init; }

    /// <summary>Gets when the append was issued.</summary>
    public required DateTimeOffset AppendedAt { get; init; }

    /// <summary>Gets when synchronization recognized the copy, or <see langword="null" /> while it has not.</summary>
    public required DateTimeOffset? ObservedAt { get; init; }

    /// <summary>Gets when the copy was taken back out of the folder, or <see langword="null" /> while it stands.</summary>
    public required DateTimeOffset? WithdrawnAt { get; init; }

    /// <summary>Gets whether the append went out and the server's answer to it never came back.</summary>
    /// <remarks>
    /// A row here is never appended again. A second <c>APPEND</c> is a second message in the owner's folder rather than a
    /// repeat of the first, and nothing the folder shows afterwards distinguishes them, so the row stands as the visible
    /// statement that the copy may or may not be there.
    /// </remarks>
    public bool HasUnknownOutcome => this.Stage == OutgoingMailFilingStage.Issued;

    /// <summary>Gets whether this copy is still in the folder as far as MailFathom knows.</summary>
    public bool IsStanding => this.Stage != OutgoingMailFilingStage.Withdrawn;

    /// <summary>Reports whether a newly discovered occurrence is the copy this filing appended.</summary>
    /// <param name="discoveredFolderPath">The remote path of the folder the occurrence was discovered in.</param>
    /// <param name="discoveredUidValidity">The UIDVALIDITY that folder reports now.</param>
    /// <param name="discoveredUid">The UID the discovered occurrence carries.</param>
    /// <returns><see langword="true" /> when the server itself named this occurrence as where it put the copy.</returns>
    /// <remarks>
    /// The UIDVALIDITY is compared as well as the UID, so a folder recreated between the append and the discovery matches
    /// nothing here: the recorded UID names a message in a UID space the folder no longer has. Such a discovery falls to
    /// <see cref="AccountsForMessageAt" />, which compares an identity a renumbering does not touch.
    /// </remarks>
    public bool AccountsForPlacementAt(
        RemoteFolderPath discoveredFolderPath,
        ImapUidValidity discoveredUidValidity,
        ImapUid discoveredUid) =>
        this.IsJoinable
        && this.NamesFolder(discoveredFolderPath)
        && this.Placement is { UidValidity: { } placedUidValidity, Uid: { } placedUid }
        && placedUidValidity == discoveredUidValidity
        && placedUid == discoveredUid;

    /// <summary>Reports whether a newly discovered occurrence carries the identity of the message this filing appended.</summary>
    /// <param name="discoveredFolderPath">The remote path of the folder the occurrence was discovered in.</param>
    /// <param name="discoveredMessageId">The <c>Message-ID</c> the server reported for the discovery, which may be absent.</param>
    /// <returns><see langword="true" /> when the discovery is this copy by the identity the message carries.</returns>
    /// <remarks>
    /// <para>
    /// This is the join for a server that advertises no <c>UIDPLUS</c>, and it is a comparison of identities rather than
    /// a search for something that looks like the message: MailFathom minted the value, it is unguessable, and it is in
    /// the bytes that were appended. What it is not is the server's own statement, which is why the reported placement is
    /// preferred wherever there is one.
    /// </para>
    /// <para>
    /// The folder is compared too, so the same message filed into two folders is joined to the filing whose folder it
    /// was found in rather than to whichever row sorted first.
    /// </para>
    /// </remarks>
    public bool AccountsForMessageAt(RemoteFolderPath discoveredFolderPath, string? discoveredMessageId) =>
        this.IsJoinable
        && this.NamesFolder(discoveredFolderPath)
        && this.InternetMessageId is { } appendedMessageId
        && discoveredMessageId is not null
        && string.Equals(appendedMessageId, discoveredMessageId, StringComparison.Ordinal);

    /// <summary>Gets whether this row is one a discovery may still be attributed to.</summary>
    /// <remarks>
    /// A confirmed append that nothing has met yet, and nothing else. An issued one names no copy anybody can point at,
    /// and an observed one has already answered for its discovery — which is what keeps a folder recreated under reused
    /// UIDs from being attributed to a filing that was met long ago.
    /// </remarks>
    private bool IsJoinable => this.Stage == OutgoingMailFilingStage.Confirmed && this.ObservedAt is null;

    private bool NamesFolder(RemoteFolderPath discoveredFolderPath) =>
        this.FolderPath.NamesSameFolderAs(discoveredFolderPath);
}
