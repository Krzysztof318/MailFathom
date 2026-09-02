// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Delivery.Governance;

/// <summary>Names the ceiling a period reached, which is the whole of what a refused send is told.</summary>
/// <remarks>
/// Four rather than two, because a deployment and one of its accounts are different bounds with different remedies: an
/// account that has spent its own is one mailbox to look at, and a deployment that has spent its own says the instance
/// as a whole sent more than an operator agreed to. Counting messages and counting recipients are likewise two answers,
/// since a hundred messages to one person each and one message to a hundred people are different faults above.
/// </remarks>
public enum OutgoingMailCeiling
{
    /// <summary>The account has been asked for as many messages in this period as it may send.</summary>
    AccountMessages = 0,

    /// <summary>The account has been asked to write to as many recipients in this period as it may.</summary>
    AccountRecipients = 1,

    /// <summary>This deployment has been asked for as many messages in this period as it may send.</summary>
    DeploymentMessages = 2,

    /// <summary>This deployment has been asked to write to as many recipients in this period as it may.</summary>
    DeploymentRecipients = 3,
}
