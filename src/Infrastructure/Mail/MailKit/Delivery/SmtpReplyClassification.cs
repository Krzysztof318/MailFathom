// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Mail.MailKit.Delivery;

/// <summary>What one submission server's refusal was, reduced to the parts that are safe to keep.</summary>
/// <param name="ReplyCode">The three-digit reply code the server answered with.</param>
/// <param name="EnhancedStatusCode">The RFC 3463 code the reply opened with, or <see langword="null" /> when it opened with none.</param>
/// <param name="Disposition">Whether the refusal can clear on its own.</param>
/// <remarks>
/// Every member is a number MailFathom derived, so the whole value may be logged, recorded, and reported. The server's
/// own sentence is deliberately not among them: it routinely names the recipient the refusal is about, and a refusal
/// is exactly the moment an operator's log would otherwise acquire an address.
/// </remarks>
internal sealed record SmtpReplyClassification(
    int ReplyCode,
    SmtpEnhancedStatusCode? EnhancedStatusCode,
    SmtpRejectionDisposition Disposition);
