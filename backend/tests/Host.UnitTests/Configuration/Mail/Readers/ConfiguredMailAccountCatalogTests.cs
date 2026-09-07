// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.Mail.Readers;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.TestSupport;
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
    private static readonly MailUserId Alex =
        MailUserId.Create(new Guid("1a7f6b1c-2d3e-4f50-8a91-b2c3d4e5f601"));

    private static readonly MailUserId Morgan =
        MailUserId.Create(new Guid("2b8f7c2d-3e4f-4a61-9b02-c3d4e5f6a712"));

    [Fact]
    public void ServedAccounts_ADeploymentDeclaringNoUser_PublishesItsOwnSectionUnderTheSoleUser()
    {
        // Arrange
        var settings = Synchronizing(Mailbox("primary", "The primary mailbox"));
        var catalog = new ConfiguredMailAccountCatalog(settings, ResolvedServedMailUsers.TheSoleUser());

        // Act
        var served = catalog.ServedAccounts;

        // Assert
        Assert.Equal([SyntheticMailUser.Deployment], served.Select(account => account.User));
        Assert.Equal(["primary"], served.Select(account => account.Id.Value));
    }

    [Fact]
    public void ServedAccounts_UsersDeclaringTheirOwnMailboxes_PublishesEachUnderTheUserWhoDeclaredIt()
    {
        // Arrange
        var settings = Synchronizing();
        var catalog = new ConfiguredMailAccountCatalog(
            settings,
            ResolvedServedMailUsers.Serving(
                Declaring(Alex, "alex", Mailbox("alex-work", "Alex at work")),
                Declaring(Morgan, "morgan", Mailbox("morgan-work", "Morgan at work"))));

        // Act
        var served = catalog.ServedAccounts;

        // Assert
        Assert.Equal(
            [(Alex, "alex-work"), (Morgan, "morgan-work")],
            served.Select(account => (account.User, account.Id.Value)));
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
            ResolvedServedMailUsers.Serving(
                Declaring(Morgan, "morgan", Mailbox("zeta", "Morgan at zeta"), Mailbox("beta", "Morgan at beta")),
                Declaring(Alex, "alex", Mailbox("alpha", "Alex at alpha"))));

        // Act
        var served = catalog.ServedAccounts;

        // Assert
        Assert.Equal(["alpha", "beta", "zeta"], served.Select(account => account.Id.Value));
    }

    /// <summary>The deployment's own section is refused beside declared users, so a user's declaration is the only source here.</summary>
    [Fact]
    public void ServedAccounts_ADeploymentWhoseOwnSectionIsEmpty_PublishesNothingUnderTheDeploymentUser()
    {
        // Arrange
        var settings = Synchronizing();
        var catalog = new ConfiguredMailAccountCatalog(
            settings,
            ResolvedServedMailUsers.Serving(
                new ServedMailUser(Alex, "alex", MailUserAccountSource.DeploymentSection, MailAccounts: []),
                Declaring(Morgan, "morgan", Mailbox("morgan-work", "Morgan at work"))));

        // Act
        var served = catalog.ServedAccounts;

        // Assert
        Assert.Equal([Morgan], served.Select(account => account.User));
    }

    /// <summary>A user who has taken their record over is served from it, and that is the source the roster carries.</summary>
    [Fact]
    public void ServedAccounts_AUserServedFromTheirOwnDocument_PublishesWhatTheDocumentHolds()
    {
        // Arrange
        var settings = Synchronizing();
        var catalog = new ConfiguredMailAccountCatalog(
            settings,
            ResolvedServedMailUsers.Serving(
                new ServedMailUser(
                    Alex,
                    "alex",
                    MailUserAccountSource.UserDocument,
                    [Mailbox("alex-adopted", "Alex, adopted")])));

        // Act
        var served = catalog.ServedAccounts;

        // Assert
        Assert.Equal([(Alex, "alex-adopted")], served.Select(account => (account.User, account.Id.Value)));
    }

    private static ServedMailUser Declaring(
        MailUserId user,
        string displayName,
        params MailSynchronizationAccountOptions[] mailAccounts) =>
        new(user, displayName, MailUserAccountSource.UserDeclaration, mailAccounts);

    private static MailSynchronizationAccountOptions Mailbox(string accountId, string displayName) => new()
    {
        AccountId = accountId,
        DisplayName = displayName,
    };

    private static MailSynchronizationOptions Synchronizing(params MailSynchronizationAccountOptions[] accounts) => new()
    {
        Enabled = true,
        Accounts = [.. accounts],
    };
}
