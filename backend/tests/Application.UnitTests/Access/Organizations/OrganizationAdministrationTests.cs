// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Access.Organizations;
using MailFathom.Application.Paging;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Access.Organizations;

/// <summary>Covers which grant admits each act on organizations, and what recording one mints.</summary>
public sealed class OrganizationAdministrationTests
{
    /// <summary>The one user a scoped administrator's authorization places, as a member of <see cref="OrganizationId" />.</summary>
    private static readonly UserId User = AccessAuthorizations.ScopedHolder;

    private static readonly Guid OrganizationId = AccessAuthorizations.ScopedOrganization;

    private static readonly Guid OtherOrganizationId = new("0197c0de-0000-4000-8000-000000000004");

    private static readonly DateTimeOffset RecordedAt = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid ScopedMailAccount = new("0197c0de-0000-4000-8000-000000000004");

    [Fact]
    public async Task CreateAsync_ACallerGrantedTheConfigurationWrite_RecordsTheTrimmedNameUnderAVersion7IdentifierMintedAtTheInstantItRecords()
    {
        // Arrange
        var harness = new AdministrationHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Organizations.CreateAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<OrganizationShortName>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(OrganizationWriteResult.Of(OrganizationWriteOutcome.Written));

        // Act
        var result = await harness.Administration.CreateAsync(
            "  Test Firma  ",
            OrganizationShortName.Create("testfirma"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OrganizationWriteOutcome.Written, result.Outcome);
        Assert.Equal(7, result.OrganizationId.Version);
        Assert.Equal(TimestampOf(Guid.CreateVersion7(RecordedAt)), TimestampOf(result.OrganizationId));

        await harness.Organizations.Received(1).CreateAsync(
            result.OrganizationId,
            "Test Firma",
            OrganizationShortName.Create("TESTFIRMA"),
            RecordedAt,
            Arg.Any<CancellationToken>());
    }

    /// <summary>Moving a user changes the login every password of theirs is typed as, so the configuration grant alone does not reach it.</summary>
    [Fact]
    public async Task SetUserOrganizationAsync_ACallerGrantedOnlyTheConfigurationWrite_IsRefusedWithoutTouchingTheStore()
    {
        // Arrange
        var harness = new AdministrationHarness(MailFathomPermission.AdminConfigurationWrite);

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            harness.Administration.SetUserOrganizationAsync(User, OrganizationId, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminCredentialsWrite, refusal.RequiredPermission);
        Assert.Empty(harness.Organizations.ReceivedCalls());
    }

    /// <summary>A short name is half of every member's login, so changing it takes the grant a move takes rather than the configuration grant.</summary>
    [Fact]
    public async Task ChangeShortNameAsync_ACallerGrantedOnlyTheConfigurationWrite_IsRefusedWithoutTouchingTheStore()
    {
        // Arrange
        var harness = new AdministrationHarness(MailFathomPermission.AdminConfigurationWrite);

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            harness.Administration.ChangeShortNameAsync(
                OrganizationId,
                OrganizationShortName.Create("ACME"),
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminCredentialsWrite, refusal.RequiredPermission);
        Assert.Empty(harness.Organizations.ReceivedCalls());
    }

    /// <summary>Moving an account decides who it may be assigned to and nothing about how anybody signs in, so it takes the grant an assignment takes.</summary>
    [Fact]
    public async Task SetMailAccountOrganizationAsync_ACallerGrantedTheConfigurationWrite_ReachesTheStore()
    {
        // Arrange
        var harness = new AdministrationHarness(MailFathomPermission.AdminConfigurationWrite);
        var mailAccount = new Guid("0197c0de-0000-4000-8000-000000000003");
        harness.Organizations.SetMailAccountOrganizationAsync(mailAccount, OrganizationId, Arg.Any<CancellationToken>())
            .Returns(new OrganizationWriteResult(OrganizationWriteOutcome.AssignmentsOutsideOrganization, StandingAssignments: 2));

        // Act
        var result = await harness.Administration.SetMailAccountOrganizationAsync(
            mailAccount,
            OrganizationId,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OrganizationWriteOutcome.AssignmentsOutsideOrganization, result.Outcome);
        Assert.Equal(2, result.StandingAssignments);
    }

    [Fact]
    public async Task SetMailAccountOrganizationAsync_ACallerGrantedOnlyTheAdministrativeRead_IsRefusedWithoutTouchingTheStore()
    {
        // Arrange
        var harness = new AdministrationHarness(MailFathomPermission.AdminRead);

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            harness.Administration.SetMailAccountOrganizationAsync(
                Guid.CreateVersion7(),
                organizationId: null,
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminConfigurationWrite, refusal.RequiredPermission);
        Assert.Empty(harness.Organizations.ReceivedCalls());
    }

    /// <summary>An account in another organization is answered exactly as an account the deployment does not hold.</summary>
    [Fact]
    public async Task SetMailAccountOrganizationAsync_AnAccountOutsideTheCallersOrganization_AnswersUnknownMailAccountWithoutTouchingTheStore()
    {
        // Arrange
        var harness = OrganizationAdministratorOverAccountIn(OrganizationAdministrators.OtherOrganization);

        // Act
        var result = await harness.Administration.SetMailAccountOrganizationAsync(
            ScopedMailAccount,
            OrganizationAdministrators.AdministeredOrganization,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OrganizationWriteOutcome.UnknownMailAccount, result.Outcome);
        Assert.Empty(harness.Organizations.ReceivedCalls());
    }

    /// <summary>Both sides of a move are checked, so an account the caller administers cannot be handed to an organization they do not.</summary>
    [Fact]
    public async Task SetMailAccountOrganizationAsync_IntoAnOrganizationOutsideTheCallersScope_AnswersUnknownOrganizationWithoutTouchingTheStore()
    {
        // Arrange
        var harness = OrganizationAdministratorOverAccountIn(OrganizationAdministrators.AdministeredOrganization);

        // Act
        var result = await harness.Administration.SetMailAccountOrganizationAsync(
            ScopedMailAccount,
            OrganizationAdministrators.OtherOrganization,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OrganizationWriteOutcome.UnknownOrganization, result.Outcome);
        Assert.Empty(harness.Organizations.ReceivedCalls());
    }

    /// <summary>
    /// An account in no organization is covered by the deployment alone, so leaving one there is not an organization's
    /// administrator to do — and the refusal is marked as the deployment's, having been decided before the account was read.
    /// </summary>
    [Fact]
    public async Task SetMailAccountOrganizationAsync_IntoNoOrganizationByAnOrganizationsAdministrator_IsRefusedWithoutTouchingTheStore()
    {
        // Arrange
        var harness = OrganizationAdministratorOverAccountIn(OrganizationAdministrators.AdministeredOrganization);

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            harness.Administration.SetMailAccountOrganizationAsync(
                ScopedMailAccount,
                organizationId: null,
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminConfigurationWrite, refusal.RequiredPermission);
        Assert.True(refusal.RefusedForTheDeploymentAlone);
        Assert.Empty(harness.Organizations.ReceivedCalls());
    }

    [Fact]
    public async Task SetMailAccountOrganizationAsync_AnAccountMovedWithinTheCallersOrganization_ReachesTheStore()
    {
        // Arrange
        var harness = OrganizationAdministratorOverAccountIn(OrganizationAdministrators.AdministeredOrganization);
        harness.Organizations.SetMailAccountOrganizationAsync(
                ScopedMailAccount,
                OrganizationAdministrators.AdministeredOrganization,
                Arg.Any<CancellationToken>())
            .Returns(OrganizationWriteResult.Of(OrganizationWriteOutcome.Written));

        // Act
        var result = await harness.Administration.SetMailAccountOrganizationAsync(
            ScopedMailAccount,
            OrganizationAdministrators.AdministeredOrganization,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OrganizationWriteOutcome.Written, result.Outcome);
    }

    [Fact]
    public async Task DeleteAsync_ACallerGrantedOnlyTheAdministrativeRead_IsRefused()
    {
        // Arrange
        var harness = new AdministrationHarness(MailFathomPermission.AdminRead);

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            harness.Administration.DeleteAsync(OrganizationId, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminConfigurationWrite, refusal.RequiredPermission);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task CreateAsync_ADisplayNameTheBoundaryShouldHaveRefused_Raises(string? displayName)
    {
        // Arrange
        var harness = new AdministrationHarness(MailFathomPermission.AdminConfigurationWrite);

        // Act
        var act = () => harness.Administration.CreateAsync(
            displayName,
            OrganizationShortName.Create("ACME"),
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<ArgumentException>(act);
    }

    public static TheoryData<AssignmentScope> OneScopeOfEachKind => [.. AccessAuthorizations.ScopesCoveringTheirTarget];

    public static TheoryData<AssignmentScope> ScopesCoveringTheOrganization =>
        [AssignmentScope.Deployment, AssignmentScope.Organization(OrganizationId)];

    /// <summary>Gets the narrower scopes that reach no organization: another organization, a member of this one, and somebody else.</summary>
    public static TheoryData<AssignmentScope> ScopesOutsideTheOrganization =>
        [.. AccessAuthorizations.ScopesOutsideTheirTarget, AssignmentScope.User(User)];

    public static TheoryData<AssignmentScope> ScopesOutsideTheUser => [.. AccessAuthorizations.ScopesOutsideTheirTarget];

    public static TheoryData<AssignmentScope> ScopesBelowTheDeployment =>
        [AssignmentScope.Organization(OrganizationId), AssignmentScope.User(User)];

    /// <summary>A listing never refuses over a scope: it answers within the one the caller reads at, whichever kind it is.</summary>
    [Theory]
    [MemberData(nameof(OneScopeOfEachKind))]
    public async Task ReadAsync_ACallerReadingAtOneScope_AsksTheStoreWithinThatScopeAlone(AssignmentScope scope)
    {
        // Arrange
        var harness = new AdministrationHarness(
            AccessAuthorizations.ForAdministratorScopedAt(scope, MailFathomPermission.AdminRead));
        var query = AdministrativeListingQuery.Create(pageSize: null, after: null)!;

        // Act
        await harness.Administration.ReadAsync(query, TestContext.Current.CancellationToken);

        // Assert
        await harness.Organizations.Received(1).ReadAsync(
            query,
            Arg.Is<IReadOnlySet<AssignmentScope>>(scopes => scopes!.SetEquals(new[] { scope })),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [MemberData(nameof(OneScopeOfEachKind))]
    public async Task ReadUnreadableAsync_ACallerReadingAtOneScope_AsksTheStoreWithinThatScopeAlone(AssignmentScope scope)
    {
        // Arrange
        var harness = new AdministrationHarness(
            AccessAuthorizations.ForAdministratorScopedAt(scope, MailFathomPermission.AdminRead));

        // Act
        await harness.Administration.ReadUnreadableAsync(TestContext.Current.CancellationToken);

        // Assert
        await harness.Organizations.Received(1).ReadUnreadableAsync(
            Arg.Is<IReadOnlySet<AssignmentScope>>(scopes => scopes!.SetEquals(new[] { scope })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EveryListing_ACallerHoldingTheAdministrativeReadAtNoScope_IsRefusedWithoutTouchingTheStore()
    {
        // Arrange
        var harness = new AdministrationHarness(AccessAuthorizations.ForAdministratorScopedAt(
            AssignmentScope.Organization(OrganizationId),
            MailFathomPermission.AdminConfigurationWrite));
        var query = AdministrativeListingQuery.Create(pageSize: null, after: null)!;

        // Act
        var readable = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            harness.Administration.ReadAsync(query, TestContext.Current.CancellationToken));
        var unreadable = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            harness.Administration.ReadUnreadableAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminRead, readable.RequiredPermission);
        Assert.Equal(MailFathomPermission.AdminRead, unreadable.RequiredPermission);
        Assert.Empty(harness.Organizations.ReceivedCalls());
    }

    [Theory]
    [MemberData(nameof(ScopesCoveringTheOrganization))]
    public async Task RenameAsync_AnOrganizationTheCallersScopeCovers_ReachesTheStore(AssignmentScope scope)
    {
        // Arrange
        var harness = new AdministrationHarness(
            AccessAuthorizations.ForAdministratorScopedAt(scope, MailFathomPermission.AdminConfigurationWrite));
        harness.Organizations.RenameAsync(OrganizationId, "Renamed", Arg.Any<CancellationToken>())
            .Returns(OrganizationWriteResult.Of(OrganizationWriteOutcome.Written));

        // Act
        var result = await harness.Administration.RenameAsync(OrganizationId, "Renamed", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OrganizationWriteOutcome.Written, result.Outcome);
    }

    /// <summary>An organization outside the caller's scope is answered exactly as one this deployment does not hold.</summary>
    [Theory]
    [MemberData(nameof(ScopesOutsideTheOrganization))]
    public async Task RenameAsync_AnOrganizationOutsideTheCallersScope_IsAnsweredAsUnknownWithoutTouchingTheStore(AssignmentScope scope)
    {
        // Arrange
        var harness = new AdministrationHarness(
            AccessAuthorizations.ForAdministratorScopedAt(scope, MailFathomPermission.AdminConfigurationWrite));

        // Act
        var result = await harness.Administration.RenameAsync(OrganizationId, "Renamed", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OrganizationWriteOutcome.UnknownOrganization, result.Outcome);
        Assert.Empty(harness.Organizations.ReceivedCalls());
    }

    [Fact]
    public async Task RenameAsync_ACallerHoldingTheConfigurationWriteAtNoScope_IsRefusedWithoutTouchingTheStore()
    {
        // Arrange
        var harness = new AdministrationHarness(AccessAuthorizations.ForAdministratorScopedAt(
            AssignmentScope.Organization(OrganizationId),
            MailFathomPermission.AdminRead));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            harness.Administration.RenameAsync(OrganizationId, "Renamed", TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminConfigurationWrite, refusal.RequiredPermission);
        Assert.Empty(harness.Organizations.ReceivedCalls());
    }

    /// <summary>
    /// Recording an organization, deleting one, and changing its short name are the deployment's alone: an organization's
    /// own scope renames it and does none of these, and neither does a scope naming one of its members.
    /// </summary>
    [Theory]
    [MemberData(nameof(ScopesBelowTheDeployment))]
    public async Task EveryDeploymentAct_ACallerHoldingBothWritesOnlyBelowTheDeployment_IsRefusedWithoutTouchingTheStore(AssignmentScope scope)
    {
        // Arrange
        var harness = new AdministrationHarness(AccessAuthorizations.ForAdministratorScopedAt(
            scope,
            MailFathomPermission.AdminConfigurationWrite,
            MailFathomPermission.AdminCredentialsWrite));
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        var creation = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            harness.Administration.CreateAsync("Acme", OrganizationShortName.Create("ACME"), cancellationToken));
        var deletion = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            harness.Administration.DeleteAsync(OrganizationId, cancellationToken));
        var shortNameChange = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            harness.Administration.ChangeShortNameAsync(OrganizationId, OrganizationShortName.Create("ACME"), cancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminConfigurationWrite, creation.RequiredPermission);
        Assert.Equal(MailFathomPermission.AdminConfigurationWrite, deletion.RequiredPermission);
        Assert.Equal(MailFathomPermission.AdminCredentialsWrite, shortNameChange.RequiredPermission);
        Assert.Empty(harness.Organizations.ReceivedCalls());
    }

    /// <summary>A move names a user and a destination, and reaches the store only where the caller's scope covers both.</summary>
    [Theory]
    [MemberData(nameof(ScopesCoveringTheOrganization))]
    public async Task SetUserOrganizationAsync_AUserAndADestinationTheCallersScopeCovers_ReachesTheStore(AssignmentScope scope)
    {
        // Arrange
        var harness = new AdministrationHarness(
            AccessAuthorizations.ForAdministratorScopedAt(scope, MailFathomPermission.AdminCredentialsWrite));
        harness.Organizations.SetUserOrganizationAsync(User, OrganizationId, Arg.Any<CancellationToken>())
            .Returns(OrganizationWriteResult.Of(OrganizationWriteOutcome.Written));

        // Act
        var result = await harness.Administration.SetUserOrganizationAsync(User, OrganizationId, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OrganizationWriteOutcome.Written, result.Outcome);
    }

    [Theory]
    [MemberData(nameof(ScopesOutsideTheUser))]
    public async Task SetUserOrganizationAsync_AUserOutsideTheCallersScope_IsAnsweredAsUnknownWithoutTouchingTheStore(AssignmentScope scope)
    {
        // Arrange
        var harness = new AdministrationHarness(
            AccessAuthorizations.ForAdministratorScopedAt(scope, MailFathomPermission.AdminCredentialsWrite));

        // Act
        var result = await harness.Administration.SetUserOrganizationAsync(User, OrganizationId, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OrganizationWriteOutcome.UnknownUser, result.Outcome);
        Assert.Empty(harness.Organizations.ReceivedCalls());
    }

    /// <summary>An organization's scope covers its members and no other organization, and a scope naming the user covers them and no organization at all.</summary>
    [Theory]
    [MemberData(nameof(ScopesBelowTheDeployment))]
    public async Task SetUserOrganizationAsync_ADestinationOutsideTheCallersScope_IsAnsweredAsUnknownWithoutTouchingTheStore(AssignmentScope scope)
    {
        // Arrange
        var harness = new AdministrationHarness(
            AccessAuthorizations.ForAdministratorScopedAt(scope, MailFathomPermission.AdminCredentialsWrite));

        // Act
        var result = await harness.Administration.SetUserOrganizationAsync(User, OtherOrganizationId, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OrganizationWriteOutcome.UnknownOrganization, result.Outcome);
        Assert.Empty(harness.Organizations.ReceivedCalls());
    }

    [Fact]
    public async Task SetUserOrganizationAsync_OutOfEveryOrganizationByACallerHoldingTheWriteOverTheDeployment_ReachesTheStore()
    {
        // Arrange
        var harness = new AdministrationHarness(MailFathomPermission.AdminCredentialsWrite);
        harness.Organizations.SetUserOrganizationAsync(User, Arg.Is((Guid?)null), Arg.Any<CancellationToken>())
            .Returns(OrganizationWriteResult.Of(OrganizationWriteOutcome.Written));

        // Act
        var result = await harness.Administration.SetUserOrganizationAsync(User, organizationId: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OrganizationWriteOutcome.Written, result.Outcome);
    }

    /// <summary>A user in no organization is covered by the deployment scope alone, so only that scope may leave one there, and the refusal is the same whoever the request named.</summary>
    [Theory]
    [MemberData(nameof(ScopesBelowTheDeployment))]
    public async Task SetUserOrganizationAsync_OutOfEveryOrganizationByACallerScopedBelowTheDeployment_IsRefusedForTheDeploymentAloneWithoutTouchingTheStore(
        AssignmentScope scope)
    {
        // Arrange
        var harness = new AdministrationHarness(
            AccessAuthorizations.ForAdministratorScopedAt(scope, MailFathomPermission.AdminCredentialsWrite));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            harness.Administration.SetUserOrganizationAsync(User, organizationId: null, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminCredentialsWrite, refusal.RequiredPermission);
        Assert.True(refusal.RefusedForTheDeploymentAlone);
        Assert.Empty(harness.Organizations.ReceivedCalls());
    }

    /// <summary>The leading 48 bits of a version 7 value, which are the millisecond it was minted at.</summary>
    private static string TimestampOf(Guid identifier) => identifier.ToString("D")[..13];

    private static AdministrationHarness OrganizationAdministratorOverAccountIn(Guid accountOrganization) =>
        new(OrganizationAdministrators.Holding(
            MailFathomPermission.AdminConfigurationWrite,
            MailAccountId.Create(ScopedMailAccount.ToString("D")),
            accountOrganization));

    private sealed class AdministrationHarness
    {
        internal AdministrationHarness(MailFathomPermission granted)
            : this(AccessAuthorizations.ForAdministratorGranted(granted))
        {
        }

        internal AdministrationHarness(AccessAuthorization authorization)
        {
            this.Organizations = Substitute.For<IOrganizationStore>();

            this.Administration = new OrganizationAdministration(
                authorization,
                this.Organizations,
                new FakeTimeProvider(RecordedAt));
        }

        internal OrganizationAdministration Administration { get; }

        internal IOrganizationStore Organizations { get; }
    }
}
