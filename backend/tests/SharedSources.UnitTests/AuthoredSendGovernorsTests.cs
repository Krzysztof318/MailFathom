// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Mail.Delivery.Governance;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Governance;
using MailFathom.Domain.Emails;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

public sealed class AuthoredSendGovernorsTests
{
    /// <summary>The permissive posture is what a suite arranging an ordinary send needs, so it refuses nothing.</summary>
    [Fact]
    public async Task Permitting_OrdinarySend_IsAdmitted()
    {
        // Arrange
        var governor = AuthoredSendGovernors.Permitting();

        // Act
        var permit = await governor.RequirePermittedAsync(Authored(), Request(), CancellationToken.None);

        // Assert
        Assert.Equal("test-caller", permit.Caller);
    }

    /// <summary>The empty book the helper defaults to vouches for nobody, which the permit reports without refusing.</summary>
    [Fact]
    public async Task Permitting_RecipientNobodyIsHeldFor_IsReportedRatherThanRefused()
    {
        // Arrange
        var governor = AuthoredSendGovernors.Permitting();

        // Act
        var permit = await governor.RequirePermittedAsync(Authored(), Request(), CancellationToken.None);

        // Assert
        Assert.Equal(1, permit.UnvouchedRecipientCount);
    }

    /// <summary>A stated posture is the one an unvouched recipient meets, which is how a suite arranges the strict deployment.</summary>
    [Fact]
    public async Task Governing_StatedRefusingPosture_RefusesTheUnvouchedRecipient()
    {
        // Arrange
        var governor = AuthoredSendGovernors.Governing(
            settings: new AuthoredSendSettings(UnvouchedRecipientPosture.Refuse));

        // Act
        var refusal = await Assert.ThrowsAsync<OutgoingMailRefusedException>(
            () => governor.RequirePermittedAsync(Authored(), Request(), CancellationToken.None));

        // Assert
        Assert.Contains("people it holds a record of", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>A stated policy is the one every recipient is judged against on this surface as well as beneath it.</summary>
    [Fact]
    public async Task Governing_StatedRecipientPolicy_JudgesTheRecipient()
    {
        // Arrange
        Assert.True(OutgoingRecipientRule.TryCreateForDomain("rival.test", out var denied));
        var governor = AuthoredSendGovernors.Governing(
            recipientPolicy: OutgoingRecipientPolicy.Create([], [denied]));

        // Act
        var refusal = await Assert.ThrowsAsync<OutgoingMailRefusedException>(
            () => governor.RequirePermittedAsync(
                Authored("bruno@rival.test"),
                Request("bruno@rival.test"),
                CancellationToken.None));

        // Assert
        Assert.Contains("never write to", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>A stated ledger is what a caller's own sends are counted against, whichever period the clock names.</summary>
    [Fact]
    public async Task Governing_StatedLedger_WeighsTheSendAgainstTheCallersOwnCeiling()
    {
        // Arrange
        var clock = new FakeTimeProvider(
            DateTimeOffset.Parse("2026-08-19T12:00:00Z", CultureInfo.InvariantCulture));
        var ledger = new AuthoredSendUsageLedger(
            AuthoredSendCeilings.Create(TimeSpan.FromDays(1), maxMessagesPerCaller: 1, maxRecipientsPerCaller: 0),
            clock);
        var governor = AuthoredSendGovernors.Governing(ledger: ledger, timeProvider: clock);
        await governor.RequirePermittedAsync(Authored(), Request(), CancellationToken.None);

        // Act
        var refusal = await Assert.ThrowsAsync<OutgoingMailRefusedException>(
            () => governor.RequirePermittedAsync(
                Authored("bruno@example.test"),
                Request("bruno@example.test", requesterIdentity: "mfctl-9c31"),
                CancellationToken.None));

        // Assert
        Assert.Contains("one caller in a period", refusal.Message, StringComparison.Ordinal);
    }

    private static IReadOnlyList<AuthoredEmailRecipient> Authored(string recipientAddress = "anna@example.test") =>
        [new AuthoredEmailRecipient(OutgoingRecipientRole.To, recipientAddress)];

    private static OutgoingEmailRequest Request(
        string recipientAddress = "anna@example.test",
        string requesterIdentity = "mfctl-4f2a")
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, recipientAddress, out var address));

        return OutgoingEmailRequest.Create(
            MailAccountIdentity.Create(SyntheticMailOwner.Deployment, MailAccountId.Create("work")),
            OutgoingEmailRequester.Command(requesterIdentity),
            [OutgoingRecipient.Create(address, OutgoingRecipientRole.To)]);
    }
}
