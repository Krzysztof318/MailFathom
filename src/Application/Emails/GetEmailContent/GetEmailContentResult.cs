// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.GetEmailContent;

/// <summary>What one content read produced for every email it named.</summary>
/// <param name="Emails">One outcome per named email, in the order the request named them.</param>
/// <remarks>
/// <para>
/// The order is the contract twice over: it is how a caller pairs an outcome with what it asked for, and it is the order
/// the read's character budget was spent in, so a body cut by that budget is one an earlier email in the same list drew
/// on.
/// </para>
/// <para>
/// This is the most sensitive projection MailFathom publishes. Everything reachable from it is message content and
/// inherits every classification, retention, access, and erasure constraint of the mail it was read from. Nothing in it
/// may be logged.
/// </para>
/// </remarks>
public sealed record GetEmailContentResult(IReadOnlyList<EmailContentReadOutcome> Emails);
