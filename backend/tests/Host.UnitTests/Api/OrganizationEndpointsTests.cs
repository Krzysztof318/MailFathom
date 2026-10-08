// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Access.Organizations;
using MailFathom.Domain.Access;
using MailFathom.Host.Api;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers what the organization routes answer, and above all what each refusal names for an administrator to act on.</summary>
public sealed class OrganizationEndpointsTests
{
    private static readonly Guid OrganizationId = new("44444444-4444-4444-8444-444444444444");

    /// <summary>A move into a scope already holding one of the user's usernames is refused naming it, so the administrator knows which credential to rename.</summary>
    [Fact]
    public async Task SetUserOrganizationAsync_ATargetHoldingOneOfTheUsersUsernames_IsRefusedNamingIt()
    {
        // Arrange
        var harness = new EndpointHarness(MailFathomPermission.AdminCredentialsWrite);
        harness.Organizations.SetUserOrganizationAsync(
                SyntheticUser.Deployment,
                OrganizationId,
                Arg.Any<CancellationToken>())
            .Returns(new OrganizationWriteResult(OrganizationWriteOutcome.UsernameTaken, CollidingUsername: "jan"));

        // Act
        var result = await OrganizationEndpoints.SetUserOrganizationAsync(
            SyntheticUser.Deployment.Value,
            new UserOrganizationRequest(OrganizationId, None: null),
            harness.Administration,
            TestContext.Current.CancellationToken);

        // Assert
        var problem = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
        Assert.Contains("'jan'", problem.ProblemDetails.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// An empty object and a misspelled field both bind to a request naming nothing, and reading that as a move out would
    /// change every login the user's passwords are typed as — so a body that states no decision reaches no store.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SetUserOrganizationAsync_ABodyStatingNeitherOrBothDecisions_IsRefusedWithoutMovingAnybody(bool both)
    {
        // Arrange
        var harness = new EndpointHarness(MailFathomPermission.AdminCredentialsWrite);
        var request = both ? new UserOrganizationRequest(OrganizationId, None: true) : new UserOrganizationRequest(null, None: null);

        // Act
        var result = await OrganizationEndpoints.SetUserOrganizationAsync(
            SyntheticUser.Deployment.Value,
            request,
            harness.Administration,
            TestContext.Current.CancellationToken);

        // Assert
        var problem = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Empty(harness.Organizations.ReceivedCalls());
    }

    [Fact]
    public async Task SetUserOrganizationAsync_NoOrganization_MovesTheUserOutOfEveryOrganization()
    {
        // Arrange
        var harness = new EndpointHarness(MailFathomPermission.AdminCredentialsWrite);
        harness.Organizations.SetUserOrganizationAsync(Arg.Any<UserId>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(OrganizationWriteResult.Of(OrganizationWriteOutcome.Written));

        // Act
        var result = await OrganizationEndpoints.SetUserOrganizationAsync(
            SyntheticUser.Deployment.Value,
            new UserOrganizationRequest(null, None: true),
            harness.Administration,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NoContent>(result.Result);

        await harness.Organizations.Received(1).SetUserOrganizationAsync(
            SyntheticUser.Deployment,
            null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_AnOrganizationWithMembers_IsRefusedNamingHowMany()
    {
        // Arrange
        var harness = new EndpointHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Organizations.DeleteAsync(OrganizationId, Arg.Any<CancellationToken>())
            .Returns(new OrganizationWriteResult(OrganizationWriteOutcome.StillHasMembers, OrganizationId, RemainingMembers: 3));

        // Act
        var result = await OrganizationEndpoints.DeleteAsync(
            OrganizationId,
            harness.Administration,
            TestContext.Current.CancellationToken);

        // Assert
        var problem = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
        Assert.Contains("3 member", problem.ProblemDetails.Detail, StringComparison.Ordinal);
    }

    /// <summary>An organization holding mail accounts is not removed, and the refusal says how many so the administrator knows what to move first.</summary>
    [Fact]
    public async Task DeleteAsync_AnOrganizationHoldingMailAccounts_IsRefusedNamingHowMany()
    {
        // Arrange
        var harness = new EndpointHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Organizations.DeleteAsync(OrganizationId, Arg.Any<CancellationToken>())
            .Returns(new OrganizationWriteResult(
                OrganizationWriteOutcome.StillHoldsMailAccounts,
                OrganizationId,
                RemainingMailAccounts: 2));

        // Act
        var result = await OrganizationEndpoints.DeleteAsync(
            OrganizationId,
            harness.Administration,
            TestContext.Current.CancellationToken);

        // Assert
        var problem = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
        Assert.Contains("2 mail account", problem.ProblemDetails.Detail, StringComparison.Ordinal);
    }

    /// <summary>A user is not moved away from the accounts they are assigned, and the refusal says how many stand in the way rather than unassigning them.</summary>
    [Fact]
    public async Task SetUserOrganizationAsync_AUserAssignedAccountsOutsideTheTarget_IsAConflictNamingHowMany()
    {
        // Arrange
        var harness = new EndpointHarness(MailFathomPermission.AdminCredentialsWrite);
        harness.Organizations.SetUserOrganizationAsync(SyntheticUser.Deployment, OrganizationId, Arg.Any<CancellationToken>())
            .Returns(new OrganizationWriteResult(OrganizationWriteOutcome.AssignmentsOutsideOrganization, StandingAssignments: 4));

        // Act
        var result = await OrganizationEndpoints.SetUserOrganizationAsync(
            SyntheticUser.Deployment.Value,
            new UserOrganizationRequest(OrganizationId, None: null),
            harness.Administration,
            TestContext.Current.CancellationToken);

        // Assert
        var problem = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
        Assert.Contains("4 mail account", problem.ProblemDetails.Detail, StringComparison.Ordinal);
    }

    /// <summary>An account is not moved away from the users it is assigned to, and the refusal says how many stand in the way rather than unassigning them.</summary>
    [Fact]
    public async Task SetMailAccountOrganizationAsync_AnAccountAssignedToUsersOutsideTheTarget_IsAConflictNamingHowMany()
    {
        // Arrange
        var harness = new EndpointHarness(MailFathomPermission.AdminConfigurationWrite);
        var mailAccount = Guid.CreateVersion7();
        harness.Organizations.SetMailAccountOrganizationAsync(mailAccount, null, Arg.Any<CancellationToken>())
            .Returns(new OrganizationWriteResult(OrganizationWriteOutcome.AssignmentsOutsideOrganization, StandingAssignments: 3));

        // Act
        var result = await OrganizationEndpoints.SetMailAccountOrganizationAsync(
            mailAccount,
            new MailAccountOrganizationRequest(null, None: true),
            harness.Administration,
            TestContext.Current.CancellationToken);

        // Assert
        var problem = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
        Assert.Contains("3 user", problem.ProblemDetails.Detail, StringComparison.Ordinal);
    }

    /// <summary>A body naming no decision is refused rather than read as a move out of every organization, which would decide who the account may be assigned to.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SetMailAccountOrganizationAsync_ABodyStatingNeitherOrBothDecisions_IsRefusedWithoutMovingIt(bool both)
    {
        // Arrange
        var harness = new EndpointHarness(MailFathomPermission.AdminConfigurationWrite);
        var request = both
            ? new MailAccountOrganizationRequest(OrganizationId, None: true)
            : new MailAccountOrganizationRequest(null, None: null);

        // Act
        var result = await OrganizationEndpoints.SetMailAccountOrganizationAsync(
            Guid.CreateVersion7(),
            request,
            harness.Administration,
            TestContext.Current.CancellationToken);

        // Assert
        var problem = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Empty(harness.Organizations.ReceivedCalls());
    }

    [Fact]
    public async Task SetMailAccountOrganizationAsync_AnAccountThisDeploymentDoesNotHold_IsNotFound()
    {
        // Arrange
        var harness = new EndpointHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Organizations.SetMailAccountOrganizationAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(OrganizationWriteResult.Of(OrganizationWriteOutcome.UnknownMailAccount));

        // Act
        var result = await OrganizationEndpoints.SetMailAccountOrganizationAsync(
            Guid.CreateVersion7(),
            new MailAccountOrganizationRequest(OrganizationId, None: null),
            harness.Administration,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NotFound<ProblemDetails>>(result.Result);
    }

    /// <summary>Changing a short name to one another organization signs in under is a conflict, not a failure.</summary>
    [Fact]
    public async Task ChangeShortNameAsync_AShortNameAnotherOrganizationHolds_IsAConflict()
    {
        // Arrange
        var harness = new EndpointHarness(MailFathomPermission.AdminCredentialsWrite);
        harness.Organizations.ChangeShortNameAsync(OrganizationId, Arg.Any<OrganizationShortName>(), Arg.Any<CancellationToken>())
            .Returns(OrganizationWriteResult.Of(OrganizationWriteOutcome.ShortNameTaken));

        // Act
        var result = await OrganizationEndpoints.ChangeShortNameAsync(
            OrganizationId,
            new OrganizationShortNameRequest("acme"),
            harness.Administration,
            TestContext.Current.CancellationToken);

        // Assert
        var problem = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
        Assert.Contains("'ACME'", problem.ProblemDetails.Detail, StringComparison.Ordinal);
    }

    /// <summary>An organization past the listing's bound would sign people in where no listing shows it, so the deployment refuses it and says why.</summary>
    [Fact]
    public async Task CreateAsync_ADeploymentAtTheOrganizationCeiling_IsAConflictNamingTheCeiling()
    {
        // Arrange
        var harness = new EndpointHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Organizations.CreateAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<OrganizationShortName>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(OrganizationWriteResult.Of(OrganizationWriteOutcome.OrganizationCeilingReached));

        // Act
        var result = await OrganizationEndpoints.CreateAsync(
            new OrganizationProvisioningRequest("Acme Corporation", "ACME"),
            harness.Administration,
            TestContext.Current.CancellationToken);

        // Assert
        var problem = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
        Assert.Contains($"{Organization.MaximumListed}", problem.ProblemDetails.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAsync_AShortNameCarryingASlash_IsRefusedWithoutReachingTheStore()
    {
        // Arrange
        var harness = new EndpointHarness(MailFathomPermission.AdminConfigurationWrite);

        // Act
        var result = await OrganizationEndpoints.CreateAsync(
            new OrganizationProvisioningRequest("Test Firma", "TEST/FIRMA"),
            harness.Administration,
            TestContext.Current.CancellationToken);

        // Assert
        var problem = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Empty(harness.Organizations.ReceivedCalls());
    }

    private sealed class EndpointHarness
    {
        internal EndpointHarness(MailFathomPermission granted)
        {
            var principals = Substitute.For<IAuthorizedPrincipalSource>();
            principals.Current.Returns(AuthorizedPrincipal.Caller("operations", [granted]));

            this.Organizations = Substitute.For<IOrganizationStore>();

            this.Administration = new OrganizationAdministration(
                new AccessAuthorization(principals),
                this.Organizations,
                new FakeTimeProvider(new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero)));
        }

        internal OrganizationAdministration Administration { get; }

        internal IOrganizationStore Organizations { get; }
    }
}
