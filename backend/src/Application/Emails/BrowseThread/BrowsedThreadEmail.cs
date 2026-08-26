// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Summaries;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.BrowseThread;

/// <summary>One message of a conversation, as a page of that conversation returns it.</summary>
/// <param name="Email">The message itself, in the same projection a list row is drawn from.</param>
/// <param name="Position">The zero-based place the message holds in the conversation's order.</param>
/// <param name="AnsweredStoredEmailId">The message this one answers among the ones the caller is shown, or <see langword="null" /> when it is a root of what they are shown.</param>
/// <param name="Contribution">The bounded opening of what this message added, or <see langword="null" /> where nothing has extracted it yet.</param>
/// <remarks>
/// <para>
/// The summary is the listing's own projection rather than a shape of this reading's own, so a client parses one message
/// across the surface and a list row and a thread row can never disagree about one message.
/// </para>
/// <para>
/// The contribution is the message's own text as extraction trimmed it — no quoted history and no signature block —
/// which is what keeps the eighth reply of a thread from redrawing the seven above it. It is what a collapsed row shows;
/// the whole message, quoted history included, is reached by the identity the summary carries.
/// </para>
/// </remarks>
public sealed record BrowsedThreadEmail(
    EmailSummary Email,
    int Position,
    StoredEmailId? AnsweredStoredEmailId,
    string? Contribution);
