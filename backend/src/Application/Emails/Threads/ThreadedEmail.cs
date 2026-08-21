// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.Threads;

/// <summary>One stored email as assembly sees it: its identity, its message identifiers, and what it answers.</summary>
/// <remarks>
/// Everything assembly decides is decided from these values and from nothing else, which is what keeps the rule readable
/// in one place: no subject, no address, and no timestamp is here to be reached for.
/// </remarks>
public sealed record ThreadedEmail
{
    /// <summary>Gets the stored email's local identity.</summary>
    public required StoredEmailId StoredEmailId { get; init; }

    /// <summary>Gets the message's own identifier, or <see langword="null" /> when it carried none.</summary>
    public string? InternetMessageId { get; init; }

    /// <summary>Gets the identifier the message answers, or <see langword="null" /> when it answers none.</summary>
    public string? AnsweredInternetMessageId { get; init; }

    /// <summary>Gets the ancestors the message refers to, in header order.</summary>
    /// <remarks>
    /// They take part in membership and in nothing else. An ancestor this deployment never received still binds the
    /// message to the conversation that ancestor names, which is what puts two replies to a message nobody here stored
    /// into one conversation rather than two — and none of them can be resolved to a row, so none of them decides the
    /// reply relation.
    /// </remarks>
    public IReadOnlyList<string> ReferencedInternetMessageIds { get; init; } = [];

    /// <summary>Gets the stored email it answers, or <see langword="null" /> while nothing has resolved one.</summary>
    public StoredEmailId? AnsweredStoredEmailId { get; init; }
}
