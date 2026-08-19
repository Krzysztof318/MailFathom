// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery.Addressing;
using MailFathom.Application.Mail.Delivery.Authoring;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Mail.Delivery.Submission;

/// <summary>States one answer to a stored email somebody asked this deployment to send.</summary>
/// <remarks>
/// <para>
/// <b>There is no account here, and no subject, and no thread header.</b> Every one of them is read out of the stored
/// email the answer is anchored to: the account is the one that email was stored from, the subject is the original's
/// with the conventional prefix, and the identifiers the answer threads by are the original's own. A caller states
/// which message it is answering and what it wrote, and nothing else — which is what keeps a reply from being a
/// message a caller happened to address the same way.
/// </para>
/// <para>
/// It is the sibling of <see cref="MailSubmissionRequest" /> rather than a variant of it. The two share the requester
/// and the bodies and agree on nothing else, because a message answering nothing names everybody it goes to and an
/// answer derives most of them; folding the two into one type would give every field of each a meaning that depended
/// on the act.
/// </para>
/// </remarks>
public sealed record MailResponseSubmissionRequest
{
    /// <summary>Gets the stored email being answered, named by its stable local identity.</summary>
    public required StoredEmailId AnsweredEmailId { get; init; }

    /// <summary>Gets which answer is being sent, which decides who receives it.</summary>
    public required AuthoredResponseAct Act { get; init; }

    /// <summary>Gets the plain-text the author wrote, which is placed above the quoted original.</summary>
    public required string PlainTextBody { get; init; }

    /// <summary>Gets the HTML alternative the author wrote, or <see langword="null" /> when they wrote none.</summary>
    public string? HtmlBody { get; init; }

    /// <summary>Gets the people the author named themselves, which is everybody a forward goes to.</summary>
    /// <remarks>They are added to whoever the act itself addresses rather than replacing them, so a reply that copies somebody in still reaches the person being answered.</remarks>
    public IReadOnlyList<NamedRecipient> Recipients { get; init; } = [];

    /// <summary>Gets the authored act asking, which is what makes the same submission twice one delivery.</summary>
    public required OutgoingEmailRequester Requester { get; init; }
}
