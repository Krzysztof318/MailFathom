// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Infrastructure.Mail;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Mail;

/// <summary>
/// Covers who an account's outgoing mail is written from: that it is configuration rather than anything a request can
/// reach, that a login which is already an address needs nothing said twice, and that an endpoint naming no sender at
/// all is refused at startup rather than at the first send.
/// </summary>
public sealed class MailDeliverySenderIdentityTests
{
    private static readonly MailAccountId Primary = MailAccountId.Create("primary");

    /// <summary>A provider that authenticates the mailbox by its address states the sending address once, as the login.</summary>
    [Fact]
    public void FindSenderIdentity_AccountWhoseUserNameIsItsAddress_SendsAsThatAddress()
    {
        // Arrange
        var account = ConfiguredMailAccounts.Primary();
        account.Delivery = ConfiguredMailAccounts.Delivery();
        var options = ConfiguredMailAccounts.Holding(account);

        // Act
        var identity = options.Readers.OutgoingSenderIdentities.FindSenderIdentity(Primary);

        // Assert
        Assert.Equal("mailfathom@example.test", identity?.Address.Address);
        Assert.Equal("example.test", identity?.Domain);
        Assert.Null(identity?.Address.DisplayName);
    }

    /// <summary>A mailbox that sends under an address it is not reached at states that address, and its name beside it.</summary>
    [Fact]
    public void FindSenderIdentity_EndpointNamingItsOwnSender_SendsAsThatAddressAndName()
    {
        // Arrange
        var account = ConfiguredMailAccounts.Primary();
        account.UserName = "relay-login";
        account.Delivery = ConfiguredMailAccounts.Delivery();
        account.Delivery.FromAddress = " office@example.test ";
        account.Delivery.FromDisplayName = "The office";
        var options = ConfiguredMailAccounts.Holding(account);

        // Act
        var identity = options.Readers.OutgoingSenderIdentities.FindSenderIdentity(Primary);

        // Assert
        Assert.Equal("office@example.test", identity?.Address.Address);
        Assert.Equal("The office", identity?.Address.DisplayName);
    }

    /// <summary>An account that configures no submission endpoint sends nothing, so there is no identity to read.</summary>
    [Fact]
    public void FindSenderIdentity_AccountConfiguringNoSubmissionEndpoint_ReadsNothing()
    {
        // Arrange
        var options = ConfiguredMailAccounts.Holding(ConfiguredMailAccounts.Primary());

        // Act and assert
        Assert.Null(options.Readers.OutgoingSenderIdentities.FindSenderIdentity(Primary));
    }

    /// <summary>An account a reload removed answers with nothing, as every per-account reader here does.</summary>
    [Fact]
    public void FindSenderIdentity_AccountThisDeploymentNoLongerConfigures_ReadsNothing()
    {
        // Arrange
        var account = ConfiguredMailAccounts.Primary();
        account.Delivery = ConfiguredMailAccounts.Delivery();
        var options = ConfiguredMailAccounts.Holding(account);

        // Act and assert
        Assert.Null(options.Readers.OutgoingSenderIdentities.FindSenderIdentity(MailAccountId.Create("secondary")));
    }

    /// <summary>
    /// A submission endpoint whose account logs in with a bare name and which states no sending address would compose
    /// nothing, so it is refused where an operator reads it rather than at the first send.
    /// </summary>
    [Fact]
    public void Validate_SubmissionEndpointNamingNoSendingAddress_IsRefused()
    {
        // Arrange
        var account = ConfiguredMailAccounts.Primary();
        account.UserName = "relay-login";
        account.Delivery = ConfiguredMailAccounts.Delivery();
        var options = ConfiguredMailAccounts.Holding(account);

        // Act
        var results = ConfiguredMailAccounts.Validate(options);

        // Assert
        var result = Assert.Single(results);
        Assert.Contains("no address to send from", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains(
            $"{nameof(MailSynchronizationAccountOptions.Delivery)}.{nameof(MailAccountDeliveryOptions.FromAddress)}",
            result.MemberNames);
    }

    /// <summary>The same account with a sending address stated is accepted.</summary>
    [Fact]
    public void Validate_SubmissionEndpointStatingItsSendingAddress_ReportsNoError()
    {
        // Arrange
        var account = ConfiguredMailAccounts.Primary();
        account.UserName = "relay-login";
        account.Delivery = ConfiguredMailAccounts.Delivery();
        account.Delivery.FromAddress = "office@example.test";
        var options = ConfiguredMailAccounts.Holding(account);

        // Act
        var results = ConfiguredMailAccounts.Validate(options);

        // Assert
        Assert.Empty(results);
    }

    /// <summary>A sending address that names no mailbox is refused rather than written into a <c>From</c> header.</summary>
    [Fact]
    public void Validate_SendingAddressNamingNoMailbox_IsRefused()
    {
        // Arrange
        var account = ConfiguredMailAccounts.Primary();
        account.Delivery = ConfiguredMailAccounts.Delivery();
        account.Delivery.FromAddress = "not-a-mailbox";
        var options = ConfiguredMailAccounts.Holding(account);

        // Act
        var results = ConfiguredMailAccounts.Validate(options);

        // Assert
        Assert.Single(results);
        Assert.Null(options.Readers.OutgoingSenderIdentities.FindSenderIdentity(Primary));
    }
}
