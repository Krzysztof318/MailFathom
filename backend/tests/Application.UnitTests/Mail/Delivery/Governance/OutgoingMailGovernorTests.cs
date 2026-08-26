// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.Mail.Delivery.Governance;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Governance;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery.Governance;

public sealed class OutgoingMailGovernorTests
{
    private static readonly MailAccountIdentity Account =
        MailAccountIdentity.Create(SyntheticMailOwner.Deployment, MailAccountId.Create("work"));

    /// <summary>A deployment that declared no ceiling counts nothing, so the ordinary posture costs no read at all.</summary>
    [Fact]
    public async Task RequirePermittedAsync_NoCeilingDeclared_ReadsNoPeriod()
    {
        // Arrange
        var usage = Substitute.For<IOutgoingMailUsageReader>();
        var governor = CreateGovernor(usage);

        // Act
        await governor.RequirePermittedAsync(CreateRequest("anna@example.test"), CancellationToken.None);

        // Assert
        await usage.DidNotReceive().ReadUsageSinceAsync(
            Arg.Any<MailAccountIdentity>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A deployment that may not send is answered before anything is read, so a refusal costs one question.</summary>
    [Fact]
    public async Task RequirePermittedAsync_SendingNotEnabled_RefusesWithoutReadingThePeriod()
    {
        // Arrange
        var usage = Substitute.For<IOutgoingMailUsageReader>();
        var governor = CreateGovernor(
            usage,
            refusal: OutgoingSendRefusalReason.AccountNotEnabled,
            ceilings: OutgoingMailCeilings.Create(
                TimeSpan.FromDays(1),
                maxMessagesPerAccount: 1,
                maxRecipientsPerAccount: 0,
                maxMessagesPerDeployment: 0,
                maxRecipientsPerDeployment: 0));

        // Act
        await Assert.ThrowsAsync<OutgoingMailRefusedException>(
            () => governor.RequirePermittedAsync(CreateRequest("anna@example.test"), CancellationToken.None));

        // Assert
        await usage.DidNotReceive().ReadUsageSinceAsync(
            Arg.Any<MailAccountIdentity>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>The period a send is weighed against is the one the clock places it in, anchored at the epoch.</summary>
    [Fact]
    public async Task RequirePermittedAsync_CeilingDeclared_CountsTheAccountAndThePeriodTheClockNames()
    {
        // Arrange
        var usage = Substitute.For<IOutgoingMailUsageReader>();
        usage.ReadUsageSinceAsync(Arg.Any<MailAccountIdentity>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(OutgoingMailUsage.None);
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-08-19T16:30:00Z", CultureInfo.InvariantCulture));
        var governor = CreateGovernor(
            usage,
            ceilings: OutgoingMailCeilings.Create(
                TimeSpan.FromDays(1),
                maxMessagesPerAccount: 10,
                maxRecipientsPerAccount: 0,
                maxMessagesPerDeployment: 0,
                maxRecipientsPerDeployment: 0),
            timeProvider: clock);

        // Act
        await governor.RequirePermittedAsync(CreateRequest("anna@example.test"), CancellationToken.None);

        // Assert
        await usage.Received(1).ReadUsageSinceAsync(
            Account,
            DateTimeOffset.Parse("2026-08-19T00:00:00Z", CultureInfo.InvariantCulture),
            Arg.Any<CancellationToken>());
    }

    /// <summary>Every recipient is judged rather than the first, so a refused address anywhere in the list refuses the message.</summary>
    [Fact]
    public async Task RequirePermittedAsync_RefusedRecipientAfterAdmittedOnes_RefusesTheMessage()
    {
        // Arrange
        Assert.True(OutgoingRecipientRule.TryCreateForDomain("example.test", out var allowed));
        var governor = CreateGovernor(
            Substitute.For<IOutgoingMailUsageReader>(),
            recipientPolicy: OutgoingRecipientPolicy.Create([allowed], []));

        // Act
        var refusal = await Assert.ThrowsAsync<OutgoingMailRefusedException>(
            () => governor.RequirePermittedAsync(
                CreateRequest("anna@example.test", "bruno@elsewhere.test"),
                CancellationToken.None));

        // Assert
        Assert.Equal(MailFathomErrorCode.OutgoingRecipientRefusedByPolicy, refusal.ErrorCode);
    }

    private static OutgoingMailGovernor CreateGovernor(
        IOutgoingMailUsageReader usage,
        OutgoingSendRefusalReason? refusal = null,
        OutgoingRecipientPolicy? recipientPolicy = null,
        OutgoingMailCeilings? ceilings = null,
        TimeProvider? timeProvider = null)
    {
        var permissions = Substitute.For<IOutgoingSendPermissionReader>();
        permissions.FindRefusal(Arg.Any<MailAccountId>()).Returns(refusal);

        return new OutgoingMailGovernor(
            permissions,
            recipientPolicy ?? OutgoingRecipientPolicy.Unrestricted,
            ceilings ?? OutgoingMailCeilings.Unbounded,
            usage,
            timeProvider ?? TimeProvider.System);
    }

    private static OutgoingEmailRequest CreateRequest(params string[] recipientAddresses)
    {
        var recipients = recipientAddresses
            .Select(candidate =>
            {
                Assert.True(EmailAddress.TryCreate(displayName: null, candidate, out var address));

                return OutgoingRecipient.Create(address, OutgoingRecipientRole.To);
            })
            .ToArray();

        return OutgoingEmailRequest.Create(
            Account,
            OutgoingEmailRequester.Command("mfctl-4f2a"),
            recipients);
    }
}
