// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery.Governance;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery.Governance;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Delivery;

/// <summary>Counts what one period has been asked to send, from the outgoing records themselves.</summary>
/// <remarks>
/// <para>
/// Four counts over two sets of rows, each of them a count and never a materialization: what a ceiling needs is how
/// many, so nothing about any message crosses this boundary and no address is read at all.
/// </para>
/// <para>
/// The deployment's count is taken over every account rather than summed from the accounts a caller happens to know
/// about, because an account removed from the configuration keeps the records it was asked for, and those messages were
/// still asked of this deployment inside this period.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class OutgoingMailUsageReader(MailFathomDbContext dbContext) : IOutgoingMailUsageReader
{
    /// <inheritdoc />
    public async Task<OutgoingMailUsage> ReadUsageSinceAsync(
        MailAccountIdentity account,
        DateTimeOffset periodStart,
        CancellationToken cancellationToken)
    {
        var records = dbContext.OutgoingEmails.AsNoTracking();

        var accountMessages = OutgoingMailUsageQuery.ComposeMessages(records, periodStart, account);
        var deploymentMessages = OutgoingMailUsageQuery.ComposeMessages(records, periodStart, account: null);

        return new OutgoingMailUsage(
            await accountMessages.LongCountAsync(cancellationToken),
            await OutgoingMailUsageQuery.ComposeRecipients(accountMessages).LongCountAsync(cancellationToken),
            await deploymentMessages.LongCountAsync(cancellationToken),
            await OutgoingMailUsageQuery.ComposeRecipients(deploymentMessages).LongCountAsync(cancellationToken));
    }
}
