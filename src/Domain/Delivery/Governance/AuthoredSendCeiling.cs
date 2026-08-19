// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Delivery.Governance;

/// <summary>Names which bound on one caller's own sending a period has reached.</summary>
/// <remarks>
/// <para>
/// The first two are kept apart for the reason the deployment's four are: a caller sending one message to two hundred
/// people and a caller sending two hundred messages are different faults, and an operator reading which one was reached
/// learns which of the two numbers they wrote is the one doing the work.
/// </para>
/// <para>
/// The third is not one of the operator's numbers at all, and is separate for exactly that reason. It says the period
/// is counting as many distinct callers as it can hold, which is a bound this system carries rather than one anybody
/// configured — an operator told the message ceiling was reached would go looking at a setting that may not even be
/// declared.
/// </para>
/// </remarks>
public enum AuthoredSendCeiling
{
    /// <summary>The caller has asked this deployment for as many messages as one period admits from it.</summary>
    CallerMessages = 0,

    /// <summary>The caller has asked this deployment to write to as many people as one period admits from it.</summary>
    CallerRecipients = 1,

    /// <summary>The period is already counting as many distinct callers as this deployment holds counts for.</summary>
    CallerCount = 2,
}
