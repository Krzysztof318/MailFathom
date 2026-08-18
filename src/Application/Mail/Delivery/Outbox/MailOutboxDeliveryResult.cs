// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Delivery;
using MailFathom.Domain.Failures;

namespace MailFathom.Application.Mail.Delivery.Outbox;

/// <summary>Reports what one delivery attempt did, in the terms a pass tallies and a log line states.</summary>
/// <remarks>
/// Nothing here is mail. The identifier is MailFathom's own, the failure is a code, and the reply code is three digits
/// the server stated — no address, no subject, and nothing a remote server wrote.
/// </remarks>
/// <param name="OutgoingEmailId">The send this attempt held.</param>
/// <param name="Outcome">How the attempt ended.</param>
/// <param name="Failure">The code recorded against the record, or <see langword="null" /> when the attempt recorded none.</param>
/// <param name="ReplyCode">The reply code the server answered the message with, or <see langword="null" /> when it stated none this attempt could read.</param>
/// <param name="AttemptCount">Which attempt this was, counting from one.</param>
public sealed record MailOutboxDeliveryResult(
    OutgoingEmailId OutgoingEmailId,
    MailOutboxDeliveryOutcome Outcome,
    MailFathomErrorCode? Failure,
    int? ReplyCode,
    int AttemptCount);
