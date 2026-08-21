// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.Mail.Delivery.Governance;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Governance;
using MailFathom.Domain.Emails;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

public sealed class OutgoingMailGovernorsTests
{
    /// <summary>The permissive posture is what a suite arranging an ordinary send needs, so it refuses nothing.</summary>
    [Fact]
    public async Task Permitting_OrdinarySend_IsAdmitted()
    {
        // Arrange
        var governor = OutgoingMailGovernors.Permitting();

        // Act
        var permitting = () => governor.RequirePermittedAsync(Request(), CancellationToken.None);

        // Assert
        await permitting();
    }

    /// <summary>A stated refusal is the one every account meets, which is how a suite arranges a deployment that may not send.</summary>
    [Fact]
    public async Task Governing_StatedRefusal_RefusesEverySend()
    {
        // Arrange
        var governor = OutgoingMailGovernors.Governing(refusal: OutgoingSendRefusalReason.DeploymentIsReadOnly);

        // Act
        var refusal = await Assert.ThrowsAsync<OutgoingMailRefusedException>(
            () => governor.RequirePermittedAsync(Request(), CancellationToken.None));

        // Assert
        Assert.Contains("read-only", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>A stated policy is the one every recipient is judged against.</summary>
    [Fact]
    public async Task Governing_StatedRecipientPolicy_JudgesTheRecipient()
    {
        // Arrange
        Assert.True(OutgoingRecipientRule.TryCreateForDomain("rival.test", out var denied));
        var governor = OutgoingMailGovernors.Governing(
            recipientPolicy: OutgoingRecipientPolicy.Create([], [denied]));

        // Act
        var refusal = await Assert.ThrowsAsync<OutgoingMailRefusedException>(
            () => governor.RequirePermittedAsync(Request("bruno@rival.test"), CancellationToken.None));

        // Assert
        Assert.Contains("never write to", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>The stated period reading is what every ceiling is weighed against, whichever period is asked about.</summary>
    [Fact]
    public async Task Governing_StatedCeilingAndUsage_WeighsTheSendAgainstThem()
    {
        // Arrange
        var governor = OutgoingMailGovernors.Governing(
            ceilings: OutgoingMailCeilings.Create(
                TimeSpan.FromDays(1),
                maxMessagesPerAccount: 1,
                maxRecipientsPerAccount: 0,
                maxMessagesPerDeployment: 0,
                maxRecipientsPerDeployment: 0),
            usage: new OutgoingMailUsage(
                AccountMessageCount: 1,
                AccountRecipientCount: 1,
                DeploymentMessageCount: 1,
                DeploymentRecipientCount: 1),
            timeProvider: new FakeTimeProvider(
                DateTimeOffset.Parse("2026-08-19T12:00:00Z", CultureInfo.InvariantCulture)));

        // Act
        var refusal = await Assert.ThrowsAsync<OutgoingMailRefusedException>(
            () => governor.RequirePermittedAsync(Request(), CancellationToken.None));

        // Assert
        Assert.Contains("in one period", refusal.Message, StringComparison.Ordinal);
    }

    private static OutgoingEmailRequest Request(string recipientAddress = "anna@example.test")
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, recipientAddress, out var address));

        return OutgoingEmailRequest.Create(
            MailAccountId.Create("work"),
            OutgoingEmailRequester.Command("mfctl-4f2a"),
            [OutgoingRecipient.Create(address, OutgoingRecipientRole.To)]);
    }
}
