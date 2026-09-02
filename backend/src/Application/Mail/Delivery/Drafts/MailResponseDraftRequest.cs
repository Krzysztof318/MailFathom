// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery.Addressing;
using MailFathom.Application.Mail.Delivery.Authoring;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Drafts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Mail.Delivery.Drafts;

/// <summary>States one answer to a stored email somebody asked this deployment to hold as a draft.</summary>
/// <remarks>
/// <para>
/// <b>There is no account here, and no subject, and no thread header</b>, for the reason there is none on the answer
/// that is sent: every one of them is read out of the stored email the answer is anchored to. A caller states which
/// message it is answering and what it wrote, and nothing else.
/// </para>
/// <para>
/// A revision re-derives all of that from the answered email rather than from what the earlier revision produced, which
/// is what keeps a draft of a reply a reply: the quotation and the threading identifiers are the stored copy's, so an
/// edit cannot silently detach the answer from the conversation it belongs to.
/// </para>
/// </remarks>
public sealed record MailResponseDraftRequest
{
    /// <summary>Gets the stored email being answered, named by its stable local identity.</summary>
    public required StoredEmailId AnsweredEmailId { get; init; }

    /// <summary>Gets which answer is being drafted, which decides who it would go to.</summary>
    public required AuthoredResponseAct Act { get; init; }

    /// <summary>Gets the plain-text the author wrote, which is placed above the quoted original.</summary>
    public required string PlainTextBody { get; init; }

    /// <summary>Gets the HTML alternative the author wrote, or <see langword="null" /> when they wrote none.</summary>
    public string? HtmlBody { get; init; }

    /// <summary>Gets the people the author named themselves, which is everybody a forward would go to.</summary>
    public IReadOnlyList<NamedRecipient> Recipients { get; init; } = [];

    /// <summary>Gets the authored act writing the draft down.</summary>
    public required OutgoingEmailRequester Author { get; init; }

    /// <summary>Gets the draft this request replaces, or <see langword="null" /> when it writes a new one.</summary>
    /// <remarks>
    /// A draft belonging to another account than the answered email was stored from is refused as a draft nobody
    /// holds, so revising is never a way to reach a mailbox the answered email does not already grant.
    /// </remarks>
    public MailDraftId? Revises { get; init; }
}
