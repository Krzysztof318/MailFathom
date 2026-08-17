// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.Threads;

/// <summary>One email of a conversation, with where it sits in the one order that conversation has.</summary>
/// <param name="Email">The email itself.</param>
/// <param name="Position">The zero-based place the email holds in the conversation's order.</param>
/// <param name="AnsweredStoredEmailId">
/// The email this one answers among the emails the caller is shown, or <see langword="null" /> when it is a root of what
/// they are shown.
/// </param>
/// <remarks>
/// The answered email is the one the caller can see rather than the one the row records. An email whose parent sits in a
/// folder withheld from tools is published as a root naming no ancestor, so the withheld email is not disclosed by the
/// gap it would otherwise leave.
/// </remarks>
public sealed record PlacedThreadedEmail(
    ThreadedEmailSummary Email,
    int Position,
    StoredEmailId? AnsweredStoredEmailId);
