// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Synchronization;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.Mail.Readers;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Persistence.Users.AccountSettings;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Mail.Readers;

/// <summary>Covers which user each configured mailbox is published under, over the two places one is declared in.</summary>
/// <remarks>
/// Only one of those two places can say whose a mailbox is. A user's own section names them; the deployment's own
/// section names nobody and belongs to whichever sole user the start established. Publishing an account under the
/// wrong one is how a query answers with another person's mail, so it is asserted rather than left to the roster.
/// </remarks>
public sealed class ConfiguredMailAccountCatalogTests
{
    private static readonly UserId Alex =
        UserId.Create(new Guid("1a7f6b1c-2d3e-4f50-8a91-b2c3d4e5f601"));

    private static readonly UserId Morgan =
        UserId.Create(new Guid("2b8f7c2d-3e4f-4a61-9b02-c3d4e5f6a712"));

    /// <summary>Every mailbox a served user's record declares is published, under the identifier it carries.</summary>
    /// <remarks>
    /// The user who declared it is not part of the published account: an account names one mailbox across the
    /// deployment, and which users reach it is the assignment relation rather than a field on the account.
    /// </remarks>
    [Fact]
    public void ServedAccounts_UsersDeclaringTheirOwnMailboxes_PublishesEveryOneOfThem()
    {
        // Arrange
        var settings = Synchronizing();
        var catalog = new ConfiguredMailAccountCatalog(
            settings,
            ResolvedServedUsers.Serving(
                Declaring(Alex, "alex", Mailbox("alex-work", "Alex at work")),
                Declaring(Morgan, "morgan", Mailbox("morgan-work", "Morgan at work"))),
            Substitute.For<IServedMailAccountReader>());

        // Act
        var served = catalog.ServedAccounts;

        // Assert
        Assert.Equal(["alex-work", "morgan-work"], served.Select(account => account.Id.Value));
    }

    /// <summary>
    /// A scope resolved from this set is the deployment's own, and a continuation cursor issued over it stays valid
    /// while the configuration does not change, so the order is one ordinal sequence across every user rather than
    /// each user's own.
    /// </summary>
    [Fact]
    public void ServedAccounts_MailboxesOfSeveralUsers_OrdersThemOrdinallyAcrossTheWholeDeployment()
    {
        // Arrange
        var settings = Synchronizing();
        var catalog = new ConfiguredMailAccountCatalog(
            settings,
            ResolvedServedUsers.Serving(
                Declaring(Morgan, "morgan", Mailbox("zeta", "Morgan at zeta"), Mailbox("beta", "Morgan at beta")),
                Declaring(Alex, "alex", Mailbox("alpha", "Alex at alpha"))),
            Substitute.For<IServedMailAccountReader>());

        // Act
        var served = catalog.ServedAccounts;

        // Assert
        Assert.Equal(["alpha", "beta", "zeta"], served.Select(account => account.Id.Value));
    }

    /// <summary>A user whose record holds no mailbox contributes none, rather than inheriting anybody else's.</summary>
    [Fact]
    public void ServedAccounts_AUserRecordingNoMailbox_ContributesNothing()
    {
        // Arrange
        var settings = Synchronizing();
        var catalog = new ConfiguredMailAccountCatalog(
            settings,
            ResolvedServedUsers.Serving(
                new ServedUser(Alex, "alex", MailAccounts: []),
                Declaring(Morgan, "morgan", Mailbox("morgan-work", "Morgan at work"))),
            Substitute.For<IServedMailAccountReader>());

        // Act
        var served = catalog.ServedAccounts;

        // Assert
        Assert.Equal(["morgan-work"], served.Select(account => account.Id.Value));
    }

    /// <summary>A user who has taken their record over is served from it, and that is the source the roster carries.</summary>
    [Fact]
    public void ServedAccounts_AUserServedFromTheirOwnDocument_PublishesWhatTheDocumentHolds()
    {
        // Arrange
        var settings = Synchronizing();
        var catalog = new ConfiguredMailAccountCatalog(
            settings,
            ResolvedServedUsers.Serving(
                new ServedUser(
                    Alex,
                    "alex",
                    [Mailbox("alex-adopted", "Alex, adopted")])),
            Substitute.For<IServedMailAccountReader>());

        // Act
        var served = catalog.ServedAccounts;

        // Assert
        Assert.Equal(["alex-adopted"], served.Select(account => account.Id.Value));
    }

    /// <summary>The records are the source a reader of the whole set is answered from, so each row becomes the account it names.</summary>
    [Fact]
    public async Task ReadServedAccountsAsync_ServedRows_AnswersEachAsTheAccountItNamesInOrdinalOrder()
    {
        // Arrange
        var later = new ServedMailAccountRow(
            new Guid("0199a0c0-0000-7000-8000-00000000000b"),
            "Morgan at work",
            MailSynchronizationMode.Push);
        var earlier = new ServedMailAccountRow(
            new Guid("0199a0c0-0000-7000-8000-00000000000a"),
            "Alex at work",
            MailSynchronizationMode.Polling);
        var catalog = CatalogReading(later, earlier);

        // Act
        var served = await catalog.ReadServedAccountsAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [
                ("0199a0c0-0000-7000-8000-00000000000a", "Alex at work", MailSynchronizationMode.Polling),
                ("0199a0c0-0000-7000-8000-00000000000b", "Morgan at work", MailSynchronizationMode.Push),
            ],
            served.Select(account => (account.Id.Value, account.DisplayName.Value, account.SynchronizationMode)));
    }

    /// <summary>A row whose name cannot be shown is left out of the set, as the composed roster leaves it out.</summary>
    [Fact]
    public async Task ReadServedAccountsAsync_ARowWhoseDisplayNameIsUnusable_IsLeftOut()
    {
        // Arrange
        var catalog = CatalogReading(
            new ServedMailAccountRow(
                new Guid("0199a0c0-0000-7000-8000-00000000000a"),
                "Alex at work",
                MailSynchronizationMode.Polling),
            new ServedMailAccountRow(
                new Guid("0199a0c0-0000-7000-8000-00000000000b"),
                "Morgan\u0007at work",
                MailSynchronizationMode.Polling));

        // Act
        var served = await catalog.ReadServedAccountsAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["0199a0c0-0000-7000-8000-00000000000a"], served.Select(account => account.Id.Value));
    }

    private static ConfiguredMailAccountCatalog CatalogReading(params ServedMailAccountRow[] rows)
    {
        var reader = Substitute.For<IServedMailAccountReader>();
        reader.ReadServedAsync(Arg.Any<CancellationToken>()).Returns(rows);

        return new ConfiguredMailAccountCatalog(Synchronizing(), ResolvedServedUsers.Serving(), reader);
    }

    private static ServedUser Declaring(
        UserId user,
        string displayName,
        params MailSynchronizationAccountOptions[] mailAccounts) =>
        new(user, displayName, mailAccounts);

    private static MailSynchronizationAccountOptions Mailbox(string accountId, string displayName) => new()
    {
        AccountId = accountId,
        DisplayName = displayName,
    };

    private static MailSynchronizationOptions Synchronizing(params MailSynchronizationAccountOptions[] accounts) =>
        new MailSynchronizationOptions { Enabled = true }.Serving(accounts);
}
