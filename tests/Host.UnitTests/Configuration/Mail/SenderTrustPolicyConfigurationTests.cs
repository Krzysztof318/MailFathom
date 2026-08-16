// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Infrastructure.Mail;
using MailFathom.Infrastructure.Secrets.Discovery;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Mail;

/// <summary>Covers which authors configuration says an account recognizes, and what an unusable entry costs.</summary>
public sealed class SenderTrustPolicyConfigurationTests
{
    /// <summary>An instance synchronizing two mailboxes is synchronizing one person's correspondence.</summary>
    [Fact]
    public void GetTrustPolicy_MailFromOneConfiguredAccountToAnother_IsRecognized()
    {
        // Arrange
        var options = OptionsFor(
            AccountAt("work", "owner@work.example"),
            AccountAt("personal", "owner@personal.example"));

        // Act
        var trust = options
            .GetTrustPolicy(MailAccountId.Create("personal"))
            .Evaluate(WrittenBy("work.example"), AddressOf("owner@work.example"));

        // Assert
        Assert.Equal(SenderTrustLevel.Trusted, trust.Level);
        Assert.Equal(SenderTrustSource.OwnAccountDomain, trust.GrantedBy);
    }

    /// <summary>A deployment whose accounts sit on a shared provider turns the set off, and the same mail is unrecognized.</summary>
    [Fact]
    public void GetTrustPolicy_OwnAccountDomainsTurnedOff_LeavesTheSameMailUnrecognized()
    {
        // Arrange
        var options = OptionsFor(
            AccountAt("work", "owner@work.example"),
            AccountAt("personal", "owner@personal.example"));
        options.TrustOwnAccountDomains = false;

        // Act
        var trust = options
            .GetTrustPolicy(MailAccountId.Create("personal"))
            .Evaluate(WrittenBy("work.example"), AddressOf("owner@work.example"));

        // Assert
        Assert.Equal(SenderTrustLevel.Unknown, trust.Level);
    }

    /// <summary>An IMAP user name that is a bare login names no mail domain, so the account contributes none.</summary>
    [Fact]
    public void GetTrustPolicy_AccountWhoseUserNameIsALogin_ContributesNoOwnDomain()
    {
        // Arrange
        var options = OptionsFor(AccountAt("work", "mailfathom"));

        // Act
        var trust = options
            .GetTrustPolicy(MailAccountId.Create("work"))
            .Evaluate(WrittenBy("work.example"), AddressOf("owner@work.example"));

        // Assert
        Assert.Equal(SenderTrustLevel.Unknown, trust.Level);
    }

    /// <summary>The list is per account, so recognizing a counterparty on one mailbox recognizes them on that one alone.</summary>
    [Fact]
    public void GetTrustPolicy_AnEntryOnOneAccount_DoesNotReachAnother()
    {
        // Arrange
        var work = AccountAt("work", "owner@work.example");
        work.TrustedSenders = [new TrustedSenderOptions { Domain = "partner.example" }];
        var options = OptionsFor(work, AccountAt("personal", "owner@personal.example"));

        // Act
        var onWork = options
            .GetTrustPolicy(MailAccountId.Create("work"))
            .Evaluate(WrittenBy("partner.example"), displayedSender: null);
        var onPersonal = options
            .GetTrustPolicy(MailAccountId.Create("personal"))
            .Evaluate(WrittenBy("partner.example"), displayedSender: null);

        // Assert
        Assert.Equal(SenderTrustLevel.Trusted, onWork.Level);
        Assert.Equal(SenderTrustSource.ConfiguredTrustedSender, onWork.GrantedBy);
        Assert.Equal(SenderTrustLevel.Unknown, onPersonal.Level);
    }

    /// <summary>Reaching under a domain is asked for per entry, so an entry that did not ask does not reach.</summary>
    [Theory]
    [InlineData(false, SenderTrustLevel.Unknown)]
    [InlineData(true, SenderTrustLevel.Trusted)]
    public void GetTrustPolicy_SubdomainOfAConfiguredDomain_FollowsWhatTheEntryAskedFor(
        bool includeSubdomains,
        SenderTrustLevel expected)
    {
        // Arrange
        var account = AccountAt("work", "owner@work.example");
        account.TrustedSenders =
            [new TrustedSenderOptions { Domain = "partner.example", IncludeSubdomains = includeSubdomains }];

        // Act
        var trust = OptionsFor(account)
            .GetTrustPolicy(MailAccountId.Create("work"))
            .Evaluate(WrittenBy("mail.partner.example"), displayedSender: null);

        // Assert
        Assert.Equal(expected, trust.Level);
    }

    /// <summary>An extraction may run over an account a reload removed, and recognizing nobody is the answer.</summary>
    [Fact]
    public void GetTrustPolicy_AccountThisSnapshotNoLongerNames_RecognizesNobody()
    {
        // Act
        var policy = OptionsFor(AccountAt("work", "owner@work.example"))
            .GetTrustPolicy(MailAccountId.Create("removed"));

        // Assert
        Assert.Same(SenderTrustPolicy.RecognizingNobody, policy);
    }

    /// <summary>An entry nothing could read fails startup, because it is indistinguishable from a list nobody wrote.</summary>
    [Theory]
    [InlineData(null, null, false)]
    [InlineData("partner.example", "alice@partner.example", false)]
    [InlineData("part ner.example", null, false)]
    [InlineData(null, "not-an-address", false)]
    [InlineData(null, "alice@partner.example", true)]
    public void ValidateForSynchronization_TrustedSenderEntry_IsRefusedWhenItDoesNotNameExactlyOneSender(
        string? domain,
        string? address,
        bool includeSubdomains)
    {
        // Arrange
        var account = AccountAt("work", "owner@work.example");
        account.TrustedSenders =
            [new TrustedSenderOptions { Domain = domain, Address = address, IncludeSubdomains = includeSubdomains }];

        // Act
        var messages = OptionsFor(account)
            .ValidateForSynchronization()
            .Select(result => result.ErrorMessage)
            .ToArray();

        // Assert
        Assert.Contains(messages, message => message!.Contains("trusted sender 0", StringComparison.Ordinal));
    }

    /// <summary>The refusal names the account and the entry's position, never the domain or the address it holds.</summary>
    [Fact]
    public void ValidateForSynchronization_UnusableTrustedSender_DoesNotEchoTheValue()
    {
        // Arrange
        var account = AccountAt("work", "owner@work.example");
        account.TrustedSenders =
        [
            new TrustedSenderOptions { Domain = "partner.example" },
            new TrustedSenderOptions { Domain = "secret partner.example" },
        ];

        // Act
        var refusal = Assert.Single(
            OptionsFor(account).ValidateForSynchronization().Select(result => result.ErrorMessage),
            message => message!.Contains("trusted sender", StringComparison.Ordinal));

        // Assert
        Assert.Contains("trusted sender 1", refusal, StringComparison.Ordinal);
        Assert.Contains("work", refusal, StringComparison.Ordinal);
        Assert.DoesNotContain("partner.example", refusal, StringComparison.Ordinal);
    }

    /// <summary>A usable list is not a mistake, so it produces no startup refusal.</summary>
    [Fact]
    public void ValidateForSynchronization_UsableTrustedSenders_AreAccepted()
    {
        // Arrange
        var account = AccountAt("work", "owner@work.example");
        account.TrustedSenders =
        [
            new TrustedSenderOptions { Domain = "partner.example", IncludeSubdomains = true },
            new TrustedSenderOptions { Address = "alice@elsewhere.example" },
        ];

        // Act
        var messages = OptionsFor(account)
            .ValidateForSynchronization()
            .Select(result => result.ErrorMessage)
            .ToArray();

        // Assert
        Assert.DoesNotContain(messages, message => message!.Contains("trusted sender", StringComparison.Ordinal));
    }

    /// <summary>Builds the verdict of a message whose displayed author the receiving server established.</summary>
    private static SenderAuthentication WrittenBy(string domain)
    {
        Assert.True(SenderDomain.TryCreate(domain, out var author));

        return SenderAuthentication.Authenticated(author, spfDomain: null, author, DmarcOutcome.Pass);
    }

    private static EmailAddress AddressOf(string written)
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, written, out var address));

        return address;
    }

    private static MailSynchronizationOptions OptionsFor(params MailSynchronizationAccountOptions[] accounts) => new()
    {
        Accounts = [.. accounts],
    };

    private static MailSynchronizationAccountOptions AccountAt(string accountId, string userName) => new()
    {
        AccountId = accountId,
        DisplayName = $"Account {accountId}",
        Host = "imap.example.test",
        UserName = userName,
        Secrets = new MailAccountSecretOptions
        {
            Password = new ConfiguredSecret { SecretReference = $"systemd-credential:imap-{accountId}-password" },
        },
    };
}
