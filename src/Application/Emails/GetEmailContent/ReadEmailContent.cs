// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Emails.Threads;
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
/// It describes every attachment a message carries and adds a short-lived link to fetch one only where a request asked
/// for it. No read model anywhere carries a file's octets, and the summary beside the list still counts what the
/// message holds without describing any of it.
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

    /// <summary>Gets one entry per attachment, described always and carrying a link only where the request asked for one.</summary>
    /// <remarks>
    /// <para>
    /// Every read describes what a message carries — the file name, the media type, and the decoded size — because a
    /// caller deciding whether a file is worth fetching needs all three, and a read that answered with a count alone
    /// would leave it nothing to decide on. Whether a link is minted is what
    /// <see cref="GetEmailContentRequest.IncludeAttachmentDownloadLinks" /> asks, and each entry says which of the
    /// answers it got.
    /// </para>
    /// <para>
    /// The descriptions are re-derived rather than stored, because file names are mail content that the row deliberately
    /// does not keep. Deriving them during the parse that produces the body costs nothing extra and guarantees they
    /// describe the message they were read from, and it is that same parse whose walk order a link names.
    /// </para>
    /// <para>
    /// It is empty when the message's raw MIME was never stored locally, which
    /// <see cref="EmailBodyAvailability.NotStoredExceededSizeLimit" /> on the body states — an emptiness about this
    /// message's parts never having been read rather than about it carrying no files, which the absent
    /// <see cref="AttachmentSummary" /> beside it states. Inline resources and cryptographic parts never appear here;
    /// they are counted in the summary instead.
    /// </para>
    /// </remarks>
    public required IReadOnlyList<ReadEmailAttachment> Attachments { get; init; }

    /// <summary>Gets the flags a mail server last showed for the email, and when they were read.</summary>
    /// <remarks>Reading content never changes them: the whole operation is served from local storage and speaks to no mail server.</remarks>
    public required RemoteEmailFlagSnapshot RemoteFlags { get; init; }

    /// <summary>Gets what was established about the author the message displays, and what this deployment made of it.</summary>
    /// <remarks>
    /// The same pair a listing carries, taken from the same summary, so the two reads cannot disagree about one message.
    /// </remarks>
    public required SenderVerification SenderVerification { get; init; }

    /// <summary>Gets what the author conclusion above was reached from.</summary>
    /// <remarks>
    /// A single-email read is where the evidence belongs: it is how a reader judges the verdict rather than what they
    /// act on, and it is read from the stored columns rather than by re-reading the message's headers.
    /// </remarks>
    public required SenderAuthenticationEvidence SenderAuthenticationEvidence { get; init; }

    /// <summary>
    /// Gets the conversation this email belongs to and the other messages in it, or <see langword="null" /> when nothing
    /// has placed the email in one.
    /// </summary>
    /// <remarks>
    /// It answers the question a reader asks straight after reading a message: what else is in this exchange, and where
    /// does what I am reading sit in it. Nothing here is body text — the other messages are named rather than
    /// reproduced — so a conversation is recognized here and read deliberately with a second call.
    /// </remarks>
    public ReadEmailThread? Thread { get; init; }
}
