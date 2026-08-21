// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Mail.Delivery.Governance;

/// <summary>States what one send was admitted as, so the record written afterwards states the same thing.</summary>
/// <param name="Caller">The identity the calling principal was admitted under.</param>
/// <param name="UnvouchedRecipientCount">How many of the addresses the caller wrote down nothing this deployment vouches for.</param>
/// <remarks>
/// It travels from the judgement to the record because both are about one send and only the first of them can read the
/// contact book: asking again after the message is durable would be a second answer to a question already settled, and
/// a different one if the book changed in between.
/// </remarks>
public sealed record AuthoredSendPermit(string Caller, int UnvouchedRecipientCount);
