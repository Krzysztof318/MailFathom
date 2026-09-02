// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.Mail.Readers;
using MailFathom.Host.Configuration.OwnerSettings;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Mail.Readers;

/// <summary>Covers which owner each configured mailbox is published under, over the two places one is declared in.</summary>
/// <remarks>
/// Only one of those two places can say whose a mailbox is. An owner's own section names them; the deployment's own
/// section names nobody and belongs to whichever sole owner the start established. Publishing an account under the
/// wrong one is how a query answers with another person's mail, so it is asserted rather than left to the roster.
/// </remarks>
public sealed class ConfiguredMailAccountCatalogTests
{
    private static readonly MailOwnerId Alex =
        MailOwnerId.Create(new Guid("1a7f6b1c-2d3e-4f50-8a91-b2c3d4e5f601"));

    private static readonly MailOwnerId Morgan =
        MailOwnerId.Create(new Guid("2b8f7c2d-3e4f-4a61-9b02-c3d4e5f6a712"));

    [Fact]
    public void ServedAccounts_ADeploymentDeclaringNoOwner_PublishesItsOwnSectionUnderTheSoleOwner()
    {
        // Arrange
        var settings = Synchronizing(Mailbox("primary", "The primary mailbox"));
        var catalog = new ConfiguredMailAccountCatalog(settings, ResolvedServedMailOwners.TheSoleOwner());

        // Act
        var served = catalog.ServedAccounts;

        // Assert
        Assert.Equal([SyntheticMailOwner.Deployment], served.Select(account => account.Owner));
        Assert.Equal(["primary"], served.Select(account => account.Id.Value));
    }

    [Fact]
    public void ServedAccounts_OwnersDeclaringTheirOwnMailboxes_PublishesEachUnderTheOwnerWhoDeclaredIt()
    {
        // Arrange
        var settings = Synchronizing();
        var catalog = new ConfiguredMailAccountCatalog(
            settings,
            ResolvedServedMailOwners.Serving(
                Declaring(Alex, "alex", Mailbox("alex-work", "Alex at work")),
                Declaring(Morgan, "morgan", Mailbox("morgan-work", "Morgan at work"))));

        // Act
        var served = catalog.ServedAccounts;

        // Assert
        Assert.Equal(
            [(Alex, "alex-work"), (Morgan, "morgan-work")],
            served.Select(account => (account.Owner, account.Id.Value)));
    }

    /// <summary>
    /// A scope resolved from this set is the deployment's own, and a continuation cursor issued over it stays valid
    /// while the configuration does not change, so the order is one ordinal sequence across every owner rather than
    /// each owner's own.
    /// </summary>
    [Fact]
    public void ServedAccounts_MailboxesOfSeveralOwners_OrdersThemOrdinallyAcrossTheWholeDeployment()
    {
        // Arrange
        var settings = Synchronizing();
        var catalog = new ConfiguredMailAccountCatalog(
            settings,
            ResolvedServedMailOwners.Serving(
                Declaring(Morgan, "morgan", Mailbox("zeta", "Morgan at zeta"), Mailbox("beta", "Morgan at beta")),
                Declaring(Alex, "alex", Mailbox("alpha", "Alex at alpha"))));

        // Act
        var served = catalog.ServedAccounts;

        // Assert
        Assert.Equal(["alpha", "beta", "zeta"], served.Select(account => account.Id.Value));
    }

    /// <summary>The deployment's own section is refused beside declared owners, so an owner's declaration is the only source here.</summary>
    [Fact]
    public void ServedAccounts_ADeploymentWhoseOwnSectionIsEmpty_PublishesNothingUnderTheDeploymentOwner()
    {
        // Arrange
        var settings = Synchronizing();
        var catalog = new ConfiguredMailAccountCatalog(
            settings,
            ResolvedServedMailOwners.Serving(
                new ServedMailOwner(Alex, "alex", MailOwnerAccountSource.DeploymentSection, MailAccounts: []),
                Declaring(Morgan, "morgan", Mailbox("morgan-work", "Morgan at work"))));

        // Act
        var served = catalog.ServedAccounts;

        // Assert
        Assert.Equal([Morgan], served.Select(account => account.Owner));
    }

    /// <summary>An owner who has taken their record over is served from it, and that is the source the roster carries.</summary>
    [Fact]
    public void ServedAccounts_AnOwnerServedFromTheirOwnDocument_PublishesWhatTheDocumentHolds()
    {
        // Arrange
        var settings = Synchronizing();
        var catalog = new ConfiguredMailAccountCatalog(
            settings,
            ResolvedServedMailOwners.Serving(
                new ServedMailOwner(
                    Alex,
                    "alex",
                    MailOwnerAccountSource.OwnerDocument,
                    [Mailbox("alex-adopted", "Alex, adopted")])));

        // Act
        var served = catalog.ServedAccounts;

        // Assert
        Assert.Equal([(Alex, "alex-adopted")], served.Select(account => (account.Owner, account.Id.Value)));
    }

    private static ServedMailOwner Declaring(
        MailOwnerId owner,
        string displayName,
        params MailSynchronizationAccountOptions[] mailAccounts) =>
        new(owner, displayName, MailOwnerAccountSource.OwnerDeclaration, mailAccounts);

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
