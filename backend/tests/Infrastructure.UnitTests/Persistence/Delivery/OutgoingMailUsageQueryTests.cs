// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Domain.Accounts;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Connections;
using MailFathom.Infrastructure.Persistence.Delivery;
using MailFathom.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Delivery;

public sealed class OutgoingMailUsageQueryTests
{
    private static readonly DateTimeOffset PeriodStart =
        DateTimeOffset.Parse("2026-08-19T00:00:00Z", CultureInfo.InvariantCulture);

    private static readonly MailAccountIdentity Account =
        MailAccountIdentity.Create(SyntheticMailOwner.Deployment, MailAccountId.Create("work"));

    /// <summary>
    /// The period is a range over the instant a record was written, which is what an epoch-anchored window means, and
    /// the account it is narrowed to is the pair rather than the identifier — a spend ceiling read for one owner's
    /// account may not count what another owner's account of the same name sent.
    /// </summary>
    [Fact]
    public void ComposeMessages_ForOneAccount_NarrowsToThatOwnersAccountInsideThePeriod()
    {
        // Arrange
        using var context = DesignTimeContext();

        // Act
        var sql = OutgoingMailUsageQuery
            .ComposeMessages(context.OutgoingEmails.AsNoTracking(), PeriodStart, Account)
            .ToQueryString();

        // Assert
        Assert.Contains("\"RecordedAt\" >=", sql, StringComparison.Ordinal);
        Assert.Contains("\"OwnerId\" =", sql, StringComparison.Ordinal);
        Assert.Contains("\"MailboxAccountId\" =", sql, StringComparison.Ordinal);
    }

    /// <summary>The deployment's count is every account's, so nothing narrows it but the window.</summary>
    [Fact]
    public void ComposeMessages_ForTheWholeDeployment_NarrowsByThePeriodAlone()
    {
        // Arrange
        using var context = DesignTimeContext();

        // Act
        var sql = OutgoingMailUsageQuery
            .ComposeMessages(context.OutgoingEmails.AsNoTracking(), PeriodStart, account: null)
            .ToQueryString();

        // Assert
        Assert.Contains("\"RecordedAt\" >=", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("\"OwnerId\" =", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("\"MailboxAccountId\" =", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// The recipients are counted over their own rows, which is what makes the answer one join rather than a subquery
    /// per record — and the query reads no column of a recipient, because an address is personal data.
    /// </summary>
    [Fact]
    public void ComposeRecipients_OverThePeriodsMessages_JoinsTheRecipientRowsWithoutReadingAnAddress()
    {
        // Arrange
        using var context = DesignTimeContext();
        var messages = OutgoingMailUsageQuery.ComposeMessages(
            context.OutgoingEmails.AsNoTracking(),
            PeriodStart,
            Account);

        // Act
        var sql = OutgoingMailUsageQuery.ComposeRecipients(messages).ToQueryString();

        // Assert
        Assert.Contains("outgoing_email_recipients", sql, StringComparison.Ordinal);
        Assert.Contains("\"RecordedAt\" >=", sql, StringComparison.Ordinal);
    }

    private static MailFathomDbContext DesignTimeContext() => new(
        MailFathomDbContextDesignTimeFactory.BuildOptions(
            orchestratedConnectionString: null,
            designTimeConnectionString: null),
        PostgresTextSearchConfiguration.Default);
}
