// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.Threads;

/// <summary>One message of a conversation, with where it sits in the one order that conversation has.</summary>
/// <param name="Email">The message itself.</param>
/// <param name="Position">The zero-based place the message holds in the conversation's order.</param>
/// <param name="AnsweredStoredEmailId">
/// The message this one answers among the messages the caller is shown, or <see langword="null" /> when it is a root of
/// what they are shown.
/// </param>
/// <remarks>
/// The answered message is the one the caller can see rather than the one the row records. A message whose parent sits
/// in a folder withheld from tools is published as a root naming no ancestor, so the withheld message is not disclosed
/// by the gap it would otherwise leave.
/// </remarks>
public sealed record PlacedThreadedEmail(
    ThreadedEmailSummary Email,
    int Position,
    StoredEmailId? AnsweredStoredEmailId);
