// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery.Addressing;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Scheduling;

namespace MailFathom.Application.Mail.Delivery.Submission;

/// <summary>States one message somebody asked this deployment to send, in the terms they wrote it in.</summary>
/// <remarks>
/// <para>
/// <b>There is no sending address here.</b> The account is named and the address it writes as comes from that account's
/// own configuration, so nothing a caller can send makes a message claim to be from somebody else. It is the same
/// absence <see cref="Composition.AuthoredEmail" /> is built around, restated at the boundary a caller actually reaches
/// so that no entrypoint has to remember to leave the field out.
/// </para>
/// <para>
/// The account is named the way every other request names one — by the identifier or the display name a deployment
/// publishes — rather than by the internal identity, so a caller composes this request from what
/// <c>list_accounts</c> told it.
/// </para>
/// <para>
/// The requester is the whole of the idempotency identity a caller supplies, and it is required rather than derived. A
/// submission this type could mint a key for would be a submission whose retry is a second message: only the caller
/// knows whether it is asking again or asking anew, and a duplicated delivery cannot be withdrawn.
/// </para>
/// </remarks>
public sealed record MailSubmissionRequest
{
    /// <summary>Gets the account the message is sent as, named as a caller names one.</summary>
    public required MailAccountSelector Account { get; init; }

    /// <summary>Gets the people the message is addressed to, in the headers the author named them in.</summary>
    public required IReadOnlyList<NamedRecipient> Recipients { get; init; }

    /// <summary>Gets the subject line the author wrote.</summary>
    public required string Subject { get; init; }

    /// <summary>Gets the plain-text body the author wrote, which every sent message carries.</summary>
    public required string PlainTextBody { get; init; }

    /// <summary>Gets the HTML alternative the author wrote, or <see langword="null" /> when they wrote none.</summary>
    public string? HtmlBody { get; init; }

    /// <summary>Gets the authored act asking, which is what makes the same submission twice one delivery.</summary>
    public required OutgoingEmailRequester Requester { get; init; }

    /// <summary>Gets the time the author asked the message to leave at, or <see langword="null" /> when they asked for it to leave at once.</summary>
    /// <remarks>
    /// It arrives resolved, as an instant together with the zone it was named in, because a wall-clock time and a zone
    /// have to become an instant exactly once and the boundary that took them from a person is where the answer to
    /// "which nine in the morning" is still available. What the use case decides is only whether the instant is one it
    /// may still hold a message for.
    /// </remarks>
    public ZonedInstant? DueAt { get; init; }
}
