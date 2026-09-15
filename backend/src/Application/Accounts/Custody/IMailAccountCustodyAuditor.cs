// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Accounts.Custody;

/// <summary>Records who asked for a mail account's custody to change, and to what.</summary>
/// <remarks>
/// Switching an account into holding its mailbox is the one administrative act that destroys a copy of somebody's mail:
/// from then on the source is emptied of everything MailFathom durably holds. So the record is of the decision rather
/// than of its consequences — who asked, which account, from which value to which, and when — and it carries nothing
/// from any message. It is written for a refused switch as well as an accepted one, because an attempt to empty a
/// mailbox is as much of an event as the switch itself.
/// </remarks>
public interface IMailAccountCustodyAuditor
{
    /// <summary>Records one decision about an account's custody.</summary>
    /// <param name="decision">What was asked for and what became of it.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    Task RecordAsync(MailAccountCustodyDecision decision, CancellationToken cancellationToken);
}

/// <summary>One administrative decision about a mail account's custody.</summary>
/// <param name="Account">The account, by the deployment's own identifier for it.</param>
/// <param name="RequestedBy">The name of the credential that asked, which is MailFathom's own configured name for it.</param>
/// <param name="From">The custody the account was asked to leave.</param>
/// <param name="To">The custody it was asked to take.</param>
/// <param name="Refusals">Why the switch was not accepted, empty for one that was.</param>
/// <param name="DecidedAt">When the decision was reached.</param>
public sealed record MailAccountCustodyDecision(
    MailAccountId Account,
    string RequestedBy,
    MailAccountCustody From,
    MailAccountCustody To,
    IReadOnlyList<MailAccountCustodySwitchRefusal> Refusals,
    DateTimeOffset DecidedAt)
{
    /// <summary>Gets whether the switch was accepted.</summary>
    public bool WasAccepted => this.Refusals.Count == 0;
}
