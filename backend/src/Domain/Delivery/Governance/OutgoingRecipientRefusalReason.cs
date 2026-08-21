// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Delivery.Governance;

/// <summary>States which half of a recipient policy turned a message away.</summary>
/// <remarks>
/// Both are about the deployment rather than about the message, which is what separates them from every other refusal a
/// send meets: no rewriting of the text reaches the address, and the remedy is either to write to somebody else or for
/// an operator to widen what this deployment may write to.
/// </remarks>
public enum OutgoingRecipientRefusalReason
{
    /// <summary>A denied entry names the recipient, which no allowed entry undoes.</summary>
    /// <remarks>
    /// Denial is read first and wins outright, because a deployment naming both lists has described the narrower intent
    /// twice, and the stricter reading is the one an operator cannot be harmed by.
    /// </remarks>
    DeniedByPolicy = 0,

    /// <summary>The policy names who this deployment may write to, and the recipient is not among them.</summary>
    /// <remarks>
    /// It is reachable only where an allowed entry was written at all: a policy naming none restricts nobody, so an
    /// operator who wrote a denied list alone never meets this refusal.
    /// </remarks>
    OutsideAllowedRecipients = 1,
}
