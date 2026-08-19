// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Delivery.Governance;

/// <summary>Counts what one caller has already asked one period to send.</summary>
/// <param name="MessageCount">The messages the caller has been admitted for in the period.</param>
/// <param name="RecipientCount">The people those messages were addressed to.</param>
/// <remarks>
/// Both are counted per distinct send rather than per call, so a caller retrying one message under the key it first
/// asked under spends its allowance once. That is the same guarantee the outgoing record gives — one identity, one
/// delivery — read here as one identity, one charge.
/// </remarks>
public readonly record struct AuthoredSendUsage(long MessageCount, long RecipientCount)
{
    /// <summary>Gets the usage of a caller that has asked this period for nothing.</summary>
    public static AuthoredSendUsage None { get; } = new(MessageCount: 0, RecipientCount: 0);
}
