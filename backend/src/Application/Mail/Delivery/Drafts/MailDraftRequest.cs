// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery.Addressing;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Drafts;

namespace MailFathom.Application.Mail.Delivery.Drafts;

/// <summary>States one draft somebody asked this deployment to hold, in the terms they wrote it in.</summary>
/// <remarks>
/// <para>
/// It is <see cref="Submission.MailSubmissionRequest" /> with two differences and no others, which is what keeps a
/// draft a message rather than a second kind of thing. The recipients may be nobody, because writing the message before
/// deciding who reads it is what a draft is for; and it may name the draft it replaces, because editing is the whole of
/// what a draft has that a send does not.
/// </para>
/// <para>
/// <b>There is no sending address here</b>, for the reason there is none on a submission: the account is named and the
/// address it writes as comes from that account's own configuration.
/// </para>
/// <para>
/// The author is provenance rather than an idempotency identity. Asking twice for a draft leaves two drafts, which
/// costs an owner a deletion rather than a recipient a second message, so nothing here is enforced by a unique index.
/// </para>
/// </remarks>
public sealed record MailDraftRequest
{
    /// <summary>Gets the account the draft belongs to, named as a caller names one.</summary>
    /// <remarks>
    /// It is required even when the request revises an existing draft, and the two have to agree: a revision naming
    /// another account's draft is refused as a draft nobody holds, so naming a draft is never a way to reach a mailbox
    /// the caller did not name.
    /// </remarks>
    public required MailAccountSelector Account { get; init; }

    /// <summary>Gets the people the draft is addressed to, which may be nobody.</summary>
    public IReadOnlyList<NamedRecipient> Recipients { get; init; } = [];

    /// <summary>Gets the subject line the author wrote.</summary>
    public required string Subject { get; init; }

    /// <summary>Gets the plain-text body the author wrote, which every stored draft carries.</summary>
    public required string PlainTextBody { get; init; }

    /// <summary>Gets the HTML alternative the author wrote, or <see langword="null" /> when they wrote none.</summary>
    public string? HtmlBody { get; init; }

    /// <summary>Gets the authored act writing the draft down.</summary>
    public required OutgoingEmailRequester Author { get; init; }

    /// <summary>Gets the draft this request replaces, or <see langword="null" /> when it writes a new one.</summary>
    public MailDraftId? Revises { get; init; }
}
