// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Synchronization;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Accounts;

/// <summary>Covers the one place the user axis enters a mailbox read.</summary>
/// <remarks>
/// Everything a caller may reach is composed from this answer, so the outcomes worth stating are all here: a user
/// reads the accounts they are assigned, a user assigned none reads nothing, one mailbox assigned to two users is
/// read by both rather than duplicated between them, and a principal acting for no user is refused rather than
/// answered with an empty set.
/// </remarks>
public sealed class AssignedMailAccountCatalogTests
{
    private static readonly ServedMailAccount ServedAccount = new(
        MailAccountId.Create("personal"),
        MailAccountDisplayName.Create("Personal mail"),
        MailSynchronizationMode.Polling);

    private static readonly ServedMailAccount AnotherUsersAccount = new(
        MailAccountId.Create("work"),
        MailAccountDisplayName.Create("Work mail"),
        MailSynchronizationMode.Polling);

    [Fact]
    public void AssignedAccounts_AUserAssignedEveryAccountTheDeploymentServes_ReadsThemAll()
    {
        // Arrange
        var catalog = CatalogFor(AccessAuthorizations.ForUserGranted(SyntheticMailUser.Deployment));

        // Act
        var assigned = catalog.AssignedAccounts;

        // Assert
        Assert.Equal([ServedAccount], assigned);
    }

    /// <summary>
    /// The refusal a caller sees for a mailbox they are not assigned has to be the one they see for a mailbox nobody
    /// configured, which is what an empty catalog produces: resolution then narrows the scope rather than reporting
    /// that the account exists and somebody else reaches it.
    /// </summary>
    [Fact]
    public void AssignedAccounts_AUserAssignedNoMailbox_ReadsNothingThisDeploymentServes()
    {
        // Arrange
        var catalog = CatalogFor(AccessAuthorizations.ForUserGranted(SyntheticMailUser.Another));

        // Act
        var assigned = catalog.AssignedAccounts;

        // Assert
        Assert.Empty(assigned);
    }

    /// <summary>
    /// The deployment administrator and this process's own identity act for nobody. Answering either with an empty set
    /// would publish a caller-facing read to them in the shape of an answer, so the port refuses instead.
    /// </summary>
    [Theory]
    [MemberData(nameof(PrincipalsActingForNoUser))]
    public void AssignedAccounts_APrincipalActingForNoUser_IsRefused(AuthorizedPrincipal principal)
    {
        // Arrange
        var catalog = CatalogFor(AccessAuthorizations.ForPrincipal(principal));

        // Act
        var refusal = Record.Exception(() => catalog.AssignedAccounts);

        // Assert
        Assert.IsType<PrincipalNotAuthorizedException>(refusal);
    }

    /// <summary>An entrypoint that stated no principal at all is refused by the same requirement rather than answered.</summary>
    [Fact]
    public void AssignedAccounts_ReachedUnderNoPrincipal_IsRefused()
    {
        // Arrange
        var catalog = CatalogFor(AccessAuthorizations.ForPrincipal(principal: null));

        // Act
        var refusal = Record.Exception(() => catalog.AssignedAccounts);

        // Assert
        Assert.IsType<PrincipalNotAuthorizedException>(refusal);
    }

    /// <summary>
    /// The deployment this change exists to enable serves several users, and every user-facing read runs through here.
    /// Each of them is answered with the mailboxes they are assigned rather than with a refusal that no sole user
    /// could be named, which is what asking the deployment for one would have produced.
    /// </summary>
    [Fact]
    public void AssignedAccounts_TwoUsersAssignedOneMailboxEach_AnswersEachWithTheirOwn()
    {
        // Arrange
        var assignments = new StubMailAccountAssignments()
            .Assigning(SyntheticMailUser.Deployment, ServedAccount.Id)
            .Assigning(SyntheticMailUser.Another, AnotherUsersAccount.Id);

        var deploymentUser = CatalogFor(
            AccessAuthorizations.ForUserGranted(SyntheticMailUser.Deployment),
            servedAccounts: [ServedAccount, AnotherUsersAccount],
            assignments: assignments);

        var anotherUser = CatalogFor(
            AccessAuthorizations.ForUserGranted(SyntheticMailUser.Another),
            servedAccounts: [ServedAccount, AnotherUsersAccount],
            assignments: assignments);

        // Act
        var deploymentUsersAccounts = deploymentUser.AssignedAccounts;
        var anotherUsersAccounts = anotherUser.AssignedAccounts;

        // Assert
        Assert.Equal([ServedAccount], deploymentUsersAccounts);
        Assert.Equal([AnotherUsersAccount], anotherUsersAccounts);
    }

    /// <summary>
    /// One mailbox assigned to two people is one mailbox, and both of them read it under the identifier the
    /// deployment gave it. That is the whole of what makes a mailbox shared rather than copied per reader, and it is
    /// the answer every narrowing site downstream composes its scope from.
    /// </summary>
    [Fact]
    public void AssignedAccounts_OneMailboxAssignedToTwoUsers_IsReadByBothUnderTheSameIdentifier()
    {
        // Arrange
        var assignments = new StubMailAccountAssignments()
            .Assigning(SyntheticMailUser.Deployment, ServedAccount.Id)
            .Assigning(SyntheticMailUser.Another, ServedAccount.Id);

        var firstReader = CatalogFor(
            AccessAuthorizations.ForUserGranted(SyntheticMailUser.Deployment),
            assignments: assignments);

        var secondReader = CatalogFor(
            AccessAuthorizations.ForUserGranted(SyntheticMailUser.Another),
            assignments: assignments);

        // Act
        var read = firstReader.AssignedAccounts;
        var alsoRead = secondReader.AssignedAccounts;

        // Assert
        Assert.Equal([ServedAccount], read);
        Assert.Equal(read, alsoRead);
    }

    /// <summary>Whether synchronization runs is a deployment fact rather than a caller's, so it is reported unchanged.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SynchronizationEnabled_WhateverTheDeploymentDecided_IsReportedUnchanged(bool synchronizationEnabled)
    {
        // Arrange
        var catalog = CatalogFor(
            AccessAuthorizations.ForUserGranted(SyntheticMailUser.Deployment),
            synchronizationEnabled);

        // Act & Assert
        Assert.Equal(synchronizationEnabled, catalog.SynchronizationEnabled);
    }

    public static TheoryData<AuthorizedPrincipal> PrincipalsActingForNoUser() =>
    [
        AuthorizedPrincipal.Caller("deployment-administrator", [MailFathomPermission.AdminRead]),
        AuthorizedPrincipal.Process,
    ];

    /// <summary>Builds the catalog over the accounts a deployment serves and who is assigned which of them.</summary>
    private static AssignedMailAccountCatalog CatalogFor(
        AccessAuthorization authorization,
        bool synchronizationEnabled = true,
        IReadOnlyList<ServedMailAccount>? servedAccounts = null,
        IMailAccountAssignments? assignments = null)
    {
        var deploymentAccounts = Substitute.For<IDeploymentMailAccountCatalog>();
        deploymentAccounts.ServedAccounts.Returns(servedAccounts ?? [ServedAccount]);
        deploymentAccounts.SynchronizationEnabled.Returns(synchronizationEnabled);

        return new AssignedMailAccountCatalog(
            deploymentAccounts,
            assignments
                ?? new StubMailAccountAssignments().Assigning(SyntheticMailUser.Deployment, ServedAccount.Id),
            authorization);
    }
}
