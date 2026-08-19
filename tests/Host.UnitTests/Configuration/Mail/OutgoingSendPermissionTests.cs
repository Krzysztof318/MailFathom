// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using MailFathom.Application.Mail.Delivery.Governance;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Transport;
using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Mail;
using MailFathom.Infrastructure.Secrets.Discovery;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Mail;

/// <summary>
/// Covers the two settings that decide whether this deployment may send as an account: the installation's own posture
/// and the account's switch, both off unless an operator wrote them.
/// </summary>
/// <remarks>
/// The answer is composed here rather than in the outbox, so this is where a deployment that never asked to send is
/// established as one that cannot — and where the order of the two settings is fixed, because which of them refused is
/// the difference between an edit to an account and how the installation was started.
/// </remarks>
public sealed class OutgoingSendPermissionTests
{
    private static readonly MailAccountId Primary = MailAccountId.Create("primary");

    /// <summary>An account nobody turned sending on for is every account of an unedited deployment.</summary>
    [Fact]
    public void FindRefusal_AccountWithSendingLeftAtItsDefault_RefusesAsNotEnabled()
    {
        // Arrange
        var account = CreateAccount();
        account.Delivery = CreateDelivery();
        var reader = CreateReader(CreateOptions(account), new DeploymentOptions());

        // Act
        var refusal = reader.FindRefusal(Primary);

        // Assert
        Assert.False(account.Delivery.Enabled);
        Assert.Equal(OutgoingSendRefusalReason.AccountNotEnabled, refusal);
    }

    /// <summary>An account an operator turned on, on a deployment that acts outward, is what a send is admitted by.</summary>
    [Fact]
    public void FindRefusal_EnabledAccountOnAnOrdinaryDeployment_RefusesNothing()
    {
        // Arrange
        var account = CreateAccount();
        account.Delivery = CreateDelivery();
        account.Delivery.Enabled = true;
        var reader = CreateReader(CreateOptions(account), new DeploymentOptions());

        // Act
        var refusal = reader.FindRefusal(Primary);

        // Assert
        Assert.Null(refusal);
    }

    /// <summary>
    /// The posture is read first, so a read-only deployment answers as itself rather than reporting an account that is
    /// in fact turned on. What resolves the two is different, and a refusal naming the wrong one sends an operator to
    /// edit a file that would change nothing.
    /// </summary>
    [Fact]
    public void FindRefusal_ReadOnlyDeployment_RefusesEvenAnEnabledAccountAsItsOwnPosture()
    {
        // Arrange
        var account = CreateAccount();
        account.Delivery = CreateDelivery();
        account.Delivery.Enabled = true;
        var deployment = new DeploymentOptions { ReadOnly = true };
        var reader = CreateReader(CreateOptions(account), deployment);

        // Act
        var refusal = reader.FindRefusal(Primary);

        // Assert
        Assert.Equal(OutgoingSendRefusalReason.DeploymentIsReadOnly, refusal);
    }

    /// <summary>
    /// The posture is read through the monitor rather than captured, so an operator turning the mode on reaches the
    /// next send rather than the next restart.
    /// </summary>
    [Fact]
    public void FindRefusal_DeploymentTurnedReadOnlyAfterComposition_RefusesTheNextSend()
    {
        // Arrange
        var account = CreateAccount();
        account.Delivery = CreateDelivery();
        account.Delivery.Enabled = true;
        var deployment = new TestOptionsMonitor<DeploymentOptions>(new DeploymentOptions());
        var reader = new ConfiguredOutgoingSendPermissionReader(CreateOptions(account), deployment);
        Assert.Null(reader.FindRefusal(Primary));

        // Act
        deployment.ReportReload(new DeploymentOptions { ReadOnly = true });

        // Assert
        Assert.Equal(OutgoingSendRefusalReason.DeploymentIsReadOnly, reader.FindRefusal(Primary));
    }

    /// <summary>
    /// An account this snapshot does not name is one the switch exists on nowhere, which is also what a reload that
    /// removed an account means for a send arriving a moment later.
    /// </summary>
    [Fact]
    public void FindRefusal_AccountThisDeploymentDoesNotServe_RefusesAsNotEnabled()
    {
        // Arrange
        var account = CreateAccount();
        account.Delivery = CreateDelivery();
        account.Delivery.Enabled = true;
        var reader = CreateReader(CreateOptions(account), new DeploymentOptions());

        // Act
        var refusal = reader.FindRefusal(MailAccountId.Create("archive"));

        // Assert
        Assert.Equal(OutgoingSendRefusalReason.AccountNotEnabled, refusal);
    }

    /// <summary>A deployment that declares nothing acts outward, so an installation upgrading into this release changes nothing.</summary>
    [Fact]
    public void ReadOnly_UnconfiguredDeployment_IsOff()
    {
        // Arrange, Act
        var deployment = new DeploymentOptions();

        // Assert
        Assert.False(deployment.ReadOnly);
    }

    /// <summary>Sending turned on with nowhere to submit is a permission nothing could act on, so it fails startup.</summary>
    [Fact]
    public void Validate_SendingEnabledOnAnAccountWithNoSubmissionHost_IsRefused()
    {
        // Arrange
        var account = CreateAccount();
        account.Delivery = new MailAccountDeliveryOptions { Enabled = true };

        // Act
        var results = Validate(CreateOptions(account));

        // Assert
        Assert.Contains(
            results,
            result => result.ErrorMessage!.Contains("no submission host", StringComparison.Ordinal)
                && result.MemberNames.Contains(
                    $"{nameof(MailSynchronizationAccountOptions.Delivery)}.{nameof(MailAccountDeliveryOptions.Enabled)}"));
    }

    /// <summary>An account with a submission endpoint it may use is an ordinary shape and reports nothing.</summary>
    [Fact]
    public void Validate_SendingEnabledOnAnAccountWithASubmissionHost_ReportsNoError()
    {
        // Arrange
        var account = CreateAccount();
        account.Delivery = CreateDelivery();
        account.Delivery.Enabled = true;

        // Act
        var results = Validate(CreateOptions(account));

        // Assert
        Assert.Empty(results);
    }

    private static ConfiguredOutgoingSendPermissionReader CreateReader(
        MailSynchronizationOptions accounts,
        DeploymentOptions deployment) =>
        new(accounts, new TestOptionsMonitor<DeploymentOptions>(deployment));

    private static IReadOnlyList<ValidationResult> Validate(MailSynchronizationOptions options) =>
        [.. options.Validate(new ValidationContext(options))];

    private static MailSynchronizationOptions CreateOptions(MailSynchronizationAccountOptions account) => new()
    {
        Enabled = true,
        Accounts = [account],
    };

    private static MailSynchronizationAccountOptions CreateAccount() => new()
    {
        AccountId = "primary",
        DisplayName = "The primary mailbox",
        Host = "imap.example.test",
        UserName = "mailfathom@example.test",
        Secrets = new MailAccountSecretOptions
        {
            Password = new ConfiguredSecret { SecretReference = "systemd-credential:imap-primary-password" },
        },
    };

    private static MailAccountDeliveryOptions CreateDelivery() => new()
    {
        Host = "smtp.example.test",
        Port = 587,
        ConnectionSecurity = MailConnectionSecurity.StartTlsRequired,
    };
}
