// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Application.Mail.Delivery;
using MailFathom.Application.Mail.Delivery.Governance;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Governance;
using MailFathom.Domain.Emails;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Delivery;

/// <summary>
/// Proves what a send ceiling is counted from: the outgoing records themselves, read back for one account and for the
/// whole deployment over the window a period names.
/// </summary>
/// <remarks>
/// Unreachable from a unit test, because the claim is about a query. What the counts have to answer is a range over the
/// instant a record was written and a join onto the recipients that record names, and only a real database decides
/// whether that translates at all and whether it counts what a ceiling means by a message and by a recipient.
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedOutgoingMailUsageTests(MailFathomOrchestrationFixture orchestration)
{
    private static readonly MailAccountId Account = SyntheticMailAccount.AccountId;

    /// <summary>
    /// Two sends naming three people between them move both counts by exactly that much. Asserted as a difference
    /// rather than as a total, because every class in this collection shares one account and one database: another
    /// test's queued send is a record this period legitimately holds.
    /// </summary>
    [Fact]
    public async Task ReadUsageSinceAsync_AfterTwoSendsInThePeriod_CountsBothMessagesAndEveryRecipient()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var periodStart = DateTimeOffset.UnixEpoch;
        var before = await ReadUsageAsync(services, periodStart, cancellationToken);

        // Act
        await EnqueueAsync(services, "usage-first", ["anna@example.test"], cancellationToken);
        await EnqueueAsync(
            services,
            "usage-second",
            ["bruno@example.test", "clara@example.test"],
            cancellationToken);

        // Assert
        var after = await ReadUsageAsync(services, periodStart, cancellationToken);

        Assert.Equal(2, after.AccountMessageCount - before.AccountMessageCount);
        Assert.Equal(3, after.AccountRecipientCount - before.AccountRecipientCount);
        Assert.Equal(2, after.DeploymentMessageCount - before.DeploymentMessageCount);
        Assert.Equal(3, after.DeploymentRecipientCount - before.DeploymentRecipientCount);
    }

    /// <summary>
    /// A period is a window rather than a total, so a period that begins after everything was written counts nothing —
    /// which is what a roll-over gives a deployment that reached its ceiling.
    /// </summary>
    [Fact]
    public async Task ReadUsageSinceAsync_PeriodBeginningAfterTheSend_CountsNothing()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await EnqueueAsync(services, "usage-before-the-period", ["dana@example.test"], cancellationToken);

        // Act
        var usage = await ReadUsageAsync(
            services,
            TimeProvider.System.GetUtcNow().AddDays(1),
            cancellationToken);

        // Assert
        Assert.Equal(OutgoingMailUsage.None, usage);
    }

    private static Task<OutgoingMailUsage> ReadUsageAsync(
        OrchestratedMailFathomServices services,
        DateTimeOffset periodStart,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IOutgoingMailUsageReader>()
                .ReadUsageSinceAsync(Account, periodStart, token),
            cancellationToken);

    /// <summary>Writes a send down as the agent that asked for it, which is the principal the outbox admits a command from.</summary>
    private static Task<OutgoingEmailRecord> EnqueueAsync(
        OrchestratedMailFathomServices services,
        string invocationIdentity,
        IReadOnlyList<string> addresses,
        CancellationToken cancellationToken)
    {
        var request = OutgoingEmailRequest.Create(
            Account,
            OutgoingEmailRequester.Command(invocationIdentity),
            [.. addresses.Select(RecipientOf)]);

        return services.AsCallerInScopeAsync(
            (scope, token) => scope.GetRequiredService<MailOutbox>().EnqueueAsync(
                request,
                MimeOf(invocationIdentity),
                token),
            [MailFathomPermission.MailSend],
            cancellationToken);
    }

    private static OutgoingRecipient RecipientOf(string address)
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, address, out var emailAddress));

        return OutgoingRecipient.Create(emailAddress, OutgoingRecipientRole.To);
    }

    private static ReadOnlyMemory<byte> MimeOf(string discriminator) => Encoding.ASCII.GetBytes(
        $"Message-ID: <{discriminator}@example.test>\r\nSubject: {discriminator}\r\n\r\nSynthetic body.\r\n")
        .AsMemory();
}
