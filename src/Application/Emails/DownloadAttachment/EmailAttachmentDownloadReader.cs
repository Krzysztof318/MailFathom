// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Attachments;
using MailFathom.Application.EmailContent.Repair;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Summaries;

namespace MailFathom.Application.Emails.DownloadAttachment;

/// <summary>Opens the one attachment a redeemed capability names, from the local mailbox copy.</summary>
/// <remarks>
/// <para>
/// The capability is verified before this use case is reached, so what arrives here is this deployment's own statement
/// about which attachment of which email was authorized. Everything the use case still does is what a signature cannot
/// establish: that the email exists, that it belongs to an account this deployment currently serves, that the stored
/// copy is what was written, and that the message really carries a part at that position.
/// </para>
/// <para>
/// It resolves through the live store on every redemption rather than against anything staged when the link was minted.
/// An attachment is mail content in full and inherits every retention, access, and erasure constraint of the message it
/// belongs to, so a link must not be able to outlive the deletion of its own message — which is a property of reading it
/// afresh rather than a rule anybody has to remember.
/// </para>
/// <para>
/// Every refusal is the same refusal. A message this deployment no longer serves, a stored copy that has gone missing, a
/// damaged one, and a position the message does not have all answer <see langword="null" />, because telling them apart
/// would let whoever holds a capability learn what became of mail they can no longer read.
/// </para>
/// <para>
/// It reaches no mail server, for the reason the content read does not: the use case holds no mailbox port, so a
/// download cannot fetch a message and cannot set the remote <c>\Seen</c> flag.
/// </para>
/// </remarks>
public sealed class EmailAttachmentDownloadReader
{
    private readonly IStoredEmailSummaryReader summaryReader;
    private readonly IEmailContentStore contentStore;
    private readonly IEmailAttachmentContentReader attachmentContentReader;
    private readonly IEmailContentRepairRequestStore repairRequestStore;
    private readonly MailboxScopeResolver scopeResolver;

    /// <summary>Initializes the use case.</summary>
    /// <param name="summaryReader">Reads one stored email's summary by its identity.</param>
    /// <param name="contentStore">Reads the raw MIME stored for an email, with what was recorded about it.</param>
    /// <param name="attachmentContentReader">Opens one attachment of that stored MIME by its position.</param>
    /// <param name="repairRequestStore">Records durably that a local copy has to be fetched or read again.</param>
    /// <param name="scopeResolver">Answers whether a tool may read the mailbox an email was stored from.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public EmailAttachmentDownloadReader(
        IStoredEmailSummaryReader summaryReader,
        IEmailContentStore contentStore,
        IEmailAttachmentContentReader attachmentContentReader,
        IEmailContentRepairRequestStore repairRequestStore,
        MailboxScopeResolver scopeResolver)
    {
        ArgumentNullException.ThrowIfNull(summaryReader);
        ArgumentNullException.ThrowIfNull(contentStore);
        ArgumentNullException.ThrowIfNull(attachmentContentReader);
        ArgumentNullException.ThrowIfNull(repairRequestStore);
        ArgumentNullException.ThrowIfNull(scopeResolver);

        this.summaryReader = summaryReader;
        this.contentStore = contentStore;
        this.attachmentContentReader = attachmentContentReader;
        this.repairRequestStore = repairRequestStore;
        this.scopeResolver = scopeResolver;
    }

    /// <summary>Opens the attachment the ticket authorizes.</summary>
    /// <param name="ticket">What the redeemed capability authorizes.</param>
    /// <param name="cancellationToken">Cancels the read when the reader disconnects.</param>
    /// <returns>The opened attachment, which the caller owns and must dispose, or <see langword="null" /> when there is nothing to serve.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="ticket" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A damaged or missing local copy records a repair request before the refusal, exactly as reading the message's
    /// content does. The finding is about the stored copy rather than about who asked for it, so discarding it because
    /// the request happened to arrive through a link would leave a defect discovered and unrecorded.
    /// </remarks>
    public async Task<IOpenedEmailAttachment?> OpenAsync(
        AttachmentDownloadTicket ticket,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        var summary = await this.summaryReader.FindAsync(ticket.StoredEmailId, cancellationToken);

        // A ticket outlives the configuration it was minted under, so what it authorizes is re-decided here: an account
        // the deployment stopped serving and a folder an operator withheld from tools both answer with nothing, exactly
        // as an email that is no longer stored does.
        if (summary is null || !this.scopeResolver.IsReadableByTools(summary.AccountId, summary.FolderAlias))
        {
            return null;
        }

        var content = await this.contentStore.FindStoredContentAsync(ticket.StoredEmailId, cancellationToken);
        if (content is null)
        {
            return await this.RefuseAndRequestRepairAsync(summary, EmailContentDefect.Missing, cancellationToken);
        }

        if (content.FindIntegrityDefect() is { } integrityDefect)
        {
            return await this.RefuseAndRequestRepairAsync(summary, integrityDefect, cancellationToken);
        }

        var opened = await this.attachmentContentReader.OpenAsync(
            content,
            ticket.AttachmentPosition,
            cancellationToken);

        if (opened.ContentIsUnreadable)
        {
            return await this.RefuseAndRequestRepairAsync(summary, EmailContentDefect.Unreadable, cancellationToken);
        }

        return opened.Attachment;
    }

    /// <summary>Records the defect durably and refuses the download.</summary>
    private async Task<IOpenedEmailAttachment?> RefuseAndRequestRepairAsync(
        EmailSummary summary,
        EmailContentDefect defect,
        CancellationToken cancellationToken)
    {
        await this.repairRequestStore.RecordAsync(
            new EmailContentRepairRequest(summary.StoredEmailId, defect),
            cancellationToken);

        return null;
    }
}
