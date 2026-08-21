// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Drafts;

namespace MailFathom.Application.Mail.Delivery.Filing;

/// <summary>Names the stored message a filed copy is appended from.</summary>
/// <remarks>
/// <para>
/// Both messages this system appends are raw MIME held behind <see cref="IEmailContentStore" />, and they are keyed
/// differently because they belong to different things: a send's payload belongs to the record of the send, a draft's
/// to the draft its author is still editing. What reads them is one append, so which of the two it is travels as a
/// value rather than as a second appender.
/// </para>
/// <para>
/// It is not a general handle on stored content. Nothing here reaches a synchronized message or a recurring send's
/// draft, because neither is a copy MailFathom files into a folder of its own.
/// </para>
/// </remarks>
public sealed record MailboxCopySource
{
    private readonly OutgoingEmailId? outgoingEmailId;
    private readonly MailDraftId? mailDraftId;

    private MailboxCopySource(OutgoingEmailId? outgoingEmailId, MailDraftId? mailDraftId)
    {
        this.outgoingEmailId = outgoingEmailId;
        this.mailDraftId = mailDraftId;
    }

    /// <summary>Names the message one outgoing record will be, or already was, transmitted as.</summary>
    /// <param name="outgoingEmailId">The record of the send.</param>
    /// <returns>A source that reads the send's stored payload.</returns>
    public static MailboxCopySource OutgoingEmail(OutgoingEmailId outgoingEmailId) =>
        new(outgoingEmailId, mailDraftId: null);

    /// <summary>Names the message the current revision of one draft is held as.</summary>
    /// <param name="mailDraftId">The draft to append.</param>
    /// <returns>A source that reads the draft's stored payload.</returns>
    public static MailboxCopySource MailDraft(MailDraftId mailDraftId) =>
        new(outgoingEmailId: null, mailDraftId);

    /// <summary>Reads the bytes this source names.</summary>
    /// <param name="contentStore">The port every piece of raw MIME is held behind.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The stored message, or <see langword="null" /> when none is stored under this source.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="contentStore" /> is <see langword="null" />.</exception>
    public Task<StoredEmailContent?> FindContentAsync(
        IEmailContentStore contentStore,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contentStore);

        return this.outgoingEmailId is { } outgoingEmail
            ? contentStore.FindOutgoingContentAsync(outgoingEmail, cancellationToken)
            : contentStore.FindMailDraftContentAsync(this.mailDraftId!.Value, cancellationToken);
    }
}
