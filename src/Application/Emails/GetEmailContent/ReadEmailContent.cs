// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Emails.GetEmailContent;

/// <summary>One email as a reader receives it: its headers, its body, and what it carries besides.</summary>
/// <remarks>
/// <para>
/// This is the most sensitive projection MailFathom publishes. It is message content in full, and it inherits every
/// classification, retention, access, and erasure constraint of the mail it was read from. Nothing in it may be logged.
/// </para>
/// <para>
/// It carries attachment octets only where a request asked to describe the attachments, and only within the two octet
/// bounds the deployment configured. Everything else about the projection is unchanged by that: no other read model
/// carries content, and the summary beside the list still counts what the message holds without describing any of it.
/// </para>
/// </remarks>
public sealed record ReadEmailContent
{
    /// <summary>Gets the stable local identity of the email, which is the one the request named.</summary>
    public required StoredEmailId StoredEmailId { get; init; }

    /// <summary>Gets the account whose mailbox the email was read from.</summary>
    public required MailAccountId AccountId { get; init; }

    /// <summary>Gets the folder alias the email was read from, which is MailFathom's own name for that folder.</summary>
    public required MailFolderAlias FolderAlias { get; init; }

    /// <summary>Gets the size the mail server reported for the message.</summary>
    /// <remarks>It is the size of the whole message on the server, which no sum over the returned representations reproduces.</remarks>
    public long SizeOctets { get; init; }

    /// <summary>Gets the normalized headers the message displays.</summary>
    public required EmailContentHeaders Headers { get; init; }

    /// <summary>Gets the body representations, or the reason there are none.</summary>
    public required EmailContentBody Body { get; init; }

    /// <summary>Gets the counts for what the message carries besides its body, or <see langword="null" /> when nobody has counted them.</summary>
    /// <remarks>
    /// <para>
    /// They come from the same parse as <see cref="Attachments" /> whenever the stored MIME could be read, so the two
    /// can never disagree.
    /// </para>
    /// <para>
    /// It is absent, rather than zero, for a message whose content the size limit kept out of storage. Nothing has ever
    /// read that message's parts: synchronization recorded what the server's envelope reported and the envelope does
    /// not describe attachments, so the row's counts are unset defaults rather than a finding. Publishing them would
    /// tell a caller that an oversized message carries no attachments, which is a claim no code here is in a position
    /// to make.
    /// </para>
    /// </remarks>
    public StoredEmailAttachmentSummary? AttachmentSummary { get; init; }

    /// <summary>Gets one entry per attachment when the request asked to describe them, each with its octets or the bound that withheld them.</summary>
    /// <remarks>
    /// <para>
    /// It is <see langword="null" /> when the request did not ask, which is not the same finding as an empty list: a
    /// file name is sender-chosen mail content and frequently the most identifying string a message carries, so a read
    /// that only wanted the body publishes none. <see cref="AttachmentSummary" /> still states how many there are, so a
    /// caller can tell that asking again would describe something.
    /// </para>
    /// <para>
    /// The descriptions are re-derived rather than stored, because file names are mail content that the row deliberately
    /// does not keep. Deriving them during the parse that produces the body costs nothing extra and guarantees they
    /// describe the message they were read from, and the octets come from that same parse for the same reason.
    /// </para>
    /// <para>
    /// It is empty when the message's raw MIME was never stored locally, which
    /// <see cref="EmailBodyAvailability.NotStoredExceededSizeLimit" /> on the body states. Inline resources and
    /// cryptographic parts never appear here; they are counted in <see cref="AttachmentSummary" /> instead.
    /// </para>
    /// </remarks>
    public IReadOnlyList<RenderedEmailAttachment>? Attachments { get; init; }

    /// <summary>Gets the flags a mail server last showed for the email, and when they were read.</summary>
    /// <remarks>Reading content never changes them: the whole operation is served from local storage and speaks to no mail server.</remarks>
    public required RemoteEmailFlagSnapshot RemoteFlags { get; init; }
}
