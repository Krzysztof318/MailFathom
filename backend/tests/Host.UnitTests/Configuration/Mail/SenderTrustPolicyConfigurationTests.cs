// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Infrastructure.Mail;
using MailFathom.Infrastructure.Secrets.Discovery;
using MailFathom.TestSupport;
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
            AccountAt("work", "user@work.example"),
            AccountAt("personal", "user@personal.example"));

        // Act
        var trust = options
            .Readers.SenderTrustPolicies.GetTrustPolicy(MailAccountId.Create("personal"))
            .Evaluate(WrittenBy("work.example"), AddressOf("user@work.example"));

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
            AccountAt("work", "user@work.example"),
            AccountAt("personal", "user@personal.example"));
        options.TrustOwnAccountDomains = false;

        // Act
        var trust = options
            .Readers.SenderTrustPolicies.GetTrustPolicy(MailAccountId.Create("personal"))
            .Evaluate(WrittenBy("work.example"), AddressOf("user@work.example"));

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
            .Readers.SenderTrustPolicies.GetTrustPolicy(MailAccountId.Create("work"))
            .Evaluate(WrittenBy("work.example"), AddressOf("user@work.example"));

        // Assert
        Assert.Equal(SenderTrustLevel.Unknown, trust.Level);
    }

    /// <summary>The list is per account, so recognizing a counterparty on one mailbox recognizes them on that one alone.</summary>
    [Fact]
    public void GetTrustPolicy_AnEntryOnOneAccount_DoesNotReachAnother()
    {
        // Arrange
        var work = AccountAt("work", "user@work.example");
        work.TrustedSenders = [new TrustedSenderOptions { Domain = "partner.example" }];
        var options = OptionsFor(work, AccountAt("personal", "user@personal.example"));

        // Act
        var onWork = options
            .Readers.SenderTrustPolicies.GetTrustPolicy(MailAccountId.Create("work"))
            .Evaluate(WrittenBy("partner.example"), displayedSender: null);
        var onPersonal = options
            .Readers.SenderTrustPolicies.GetTrustPolicy(MailAccountId.Create("personal"))
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
        var account = AccountAt("work", "user@work.example");
        account.TrustedSenders =
            [new TrustedSenderOptions { Domain = "partner.example", IncludeSubdomains = includeSubdomains }];

        // Act
        var trust = OptionsFor(account)
            .Readers.SenderTrustPolicies.GetTrustPolicy(MailAccountId.Create("work"))
            .Evaluate(WrittenBy("mail.partner.example"), displayedSender: null);

        // Assert
        Assert.Equal(expected, trust.Level);
    }

    /// <summary>An extraction may run over an account a reload removed, and recognizing nobody is the answer.</summary>
    [Fact]
    public void GetTrustPolicy_AccountThisSnapshotNoLongerNames_RecognizesNobody()
    {
        // Act
        var policy = OptionsFor(AccountAt("work", "user@work.example"))
            .Readers.SenderTrustPolicies.GetTrustPolicy(MailAccountId.Create("removed"));

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
        var account = AccountAt("work", "user@work.example");
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
        var account = AccountAt("work", "user@work.example");
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
        var account = AccountAt("work", "user@work.example");
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

    /// <summary>A mailbox declared under a served user is the whole of what such a deployment configures, so its list has to be read.</summary>
    [Fact]
    public void GetTrustPolicy_AnAccountDeclaredUnderAServedUser_RecognizesTheSendersItConfigured()
    {
        // Arrange
        var account = AccountAt("work", "user@work.example");
        account.TrustedSenders = [new TrustedSenderOptions { Domain = "partner.example" }];

        // Act
        var trust = UserDeclaring(account)
            .Readers.SenderTrustPolicies.GetTrustPolicy(MailAccountId.Create("work"))
            .Evaluate(WrittenBy("partner.example"), displayedSender: null);

        // Assert
        Assert.Equal(SenderTrustLevel.Trusted, trust.Level);
    }

    /// <summary>Two served users are two people, so one person's mail domain is not correspondence the other recognizes.</summary>
    [Fact]
    public void GetTrustPolicy_AnotherUsersAccountDomain_IsNotRecognizedInsideThisUsersMailbox()
    {
        // Arrange
        var options = new MailSynchronizationOptions().WithServedUsers(
        [
            User(SyntheticMailUser.Deployment, AccountAt("work", "user@work.example")),
            User(SyntheticMailUser.Another, AccountAt("theirs", "other@elsewhere.example")),
        ]);

        // Act
        var trust = options
            .Readers.SenderTrustPolicies.GetTrustPolicy(MailAccountId.Create("work"))
            .Evaluate(WrittenBy("elsewhere.example"), AddressOf("other@elsewhere.example"));

        // Assert
        Assert.Equal(SenderTrustLevel.Unknown, trust.Level);
    }

    /// <summary>Builds the verdict of a message whose displayed author the receiving server established.</summary>
    private static SenderAuthentication WrittenBy(string domain)
    {
        Assert.True(SenderDomain.TryCreate(domain, out var author));

        return SenderAuthentication.Authenticated([author], spfDomains: [], author, DmarcOutcome.Pass);
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

    private static MailSynchronizationOptions UserDeclaring(params MailSynchronizationAccountOptions[] accounts) =>
        new MailSynchronizationOptions().WithServedUsers([User(SyntheticMailUser.Deployment, accounts)]);

    private static ServedMailUser User(MailUserId user, params MailSynchronizationAccountOptions[] accounts) =>
        new(user, "a user this deployment serves", MailUserAccountSource.UserDocument, accounts);

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
