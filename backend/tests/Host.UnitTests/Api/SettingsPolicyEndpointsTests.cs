// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Host.Api;
using MailFathom.Host.Configuration.Policies;
using MailFathom.Host.Signals;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers what the settings-policy routes answer: the policy and its version, what a write did, and each refusal an administrator acts on.</summary>
public sealed class SettingsPolicyEndpointsTests
{
    private const string ForcingPolling = """{"MailAccounts":{"Forced":{"Mode":"Polling"}}}""";

    private static readonly Guid Organization = AccessAuthorizations.ScopedOrganization;

    [Fact]
    public async Task ReadDeploymentPolicyAsync_ADeploymentStoringNoPolicy_AnswersOneStatingNothingAtVersionZero()
    {
        // Arrange
        var harness = new EndpointHarness();

        // Act
        var result = await SettingsPolicyEndpoints.ReadDeploymentPolicyAsync(
            harness.Policies,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(new SettingsPolicyResponse(OrganizationId: null, Version: 0, Document: "{}"), result.Value);
    }

    [Fact]
    public async Task ReadOrganizationPolicyAsync_AnOrganizationsPolicy_AnswersItWithTheVersionASaveStates()
    {
        // Arrange
        var harness = new EndpointHarness();
        harness.Store.Holding(Organization, ForcingPolling, version: 3);

        // Act
        var result = await SettingsPolicyEndpoints.ReadOrganizationPolicyAsync(
            Organization,
            harness.Policies,
            TestContext.Current.CancellationToken);

        // Assert
        var answered = Assert.IsType<Ok<SettingsPolicyResponse>>(result.Result);
        Assert.Equal(new SettingsPolicyResponse(Organization, Version: 3, ForcingPolling), answered.Value);
    }

    [Fact]
    public async Task ReadOrganizationPolicyAsync_AnOrganizationThisDeploymentDoesNotHold_IsNotFound()
    {
        // Arrange
        var harness = new EndpointHarness();

        // Act
        var result = await SettingsPolicyEndpoints.ReadOrganizationPolicyAsync(
            new Guid("0198f0aa-0000-7000-8000-00000000f0ff"),
            harness.Policies,
            TestContext.Current.CancellationToken);

        // Assert
        var absent = Assert.IsType<NotFound<ProblemDetails>>(result.Result);
        Assert.Equal("This deployment holds no such organization.", absent.Value?.Detail);
    }

    /// <summary>An organization outside the caller's scope is answered exactly as one nobody holds.</summary>
    [Fact]
    public async Task SaveOrganizationPolicyAsync_AnOrganizationOutsideTheCallersScope_IsNotFoundAndWritesNothing()
    {
        // Arrange
        var harness = new EndpointHarness(AccessAuthorizations.ForAdministratorScopedAt(
            AccessAuthorizations.ScopesOutsideTheirTarget[0],
            MailFathomPermission.AdminConfigurationWrite));

        // Act
        var result = await SettingsPolicyEndpoints.SaveOrganizationPolicyAsync(
            Organization,
            harness.Policies,
            new SettingsPolicySaveRequest(Version: 0, ForcingPolling),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NotFound<ProblemDetails>>(result.Result);
        Assert.Equal(0, harness.Store.Commits);
    }

    [Fact]
    public async Task ReadOrganizationPolicyAsync_NoOrganizationNamed_IsABadRequest()
    {
        // Arrange
        var harness = new EndpointHarness();

        // Act
        var result = await SettingsPolicyEndpoints.ReadOrganizationPolicyAsync(
            Guid.Empty,
            harness.Policies,
            TestContext.Current.CancellationToken);

        // Assert
        var problem = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
    }

    [Fact]
    public async Task SaveDeploymentPolicyAsync_AFirstPolicy_AnswersTheVersionItWasCommittedAs()
    {
        // Arrange
        var harness = new EndpointHarness();

        // Act
        var result = await SettingsPolicyEndpoints.SaveDeploymentPolicyAsync(
            harness.Policies,
            new SettingsPolicySaveRequest(Version: 0, ForcingPolling),
            TestContext.Current.CancellationToken);

        // Assert
        var answered = Assert.IsType<Ok<SettingsPolicyWriteResponse>>(result.Result);
        Assert.Equivalent(new SettingsPolicyWriteResponse(Committed: true, Version: 1, Code: null, Messages: []), answered.Value);
    }

    /// <summary>A refusal is an outcome the administrator corrects and continues from, so it arrives with a success status, a code, and the version to compose over.</summary>
    [Theory]
    [InlineData("""{"Users":{"Defaults":{"Dialect":"Polish"}}}""", 2, 12007, "Users:Defaults names Dialect")]
    [InlineData(ForcingPolling, 1, 12008, "version 2 is in force")]
    public async Task SaveOrganizationPolicyAsync_ARefusedWrite_AnswersTheCodeTheVersionInForceAndWhatToCorrect(
        string saved,
        long composedOver,
        int expectedCode,
        string expectedMessage)
    {
        // Arrange
        var harness = new EndpointHarness();
        harness.Store.Holding(Organization, "{}", version: 2);

        // Act
        var result = await SettingsPolicyEndpoints.SaveOrganizationPolicyAsync(
            Organization,
            harness.Policies,
            new SettingsPolicySaveRequest(composedOver, saved),
            TestContext.Current.CancellationToken);

        // Assert
        var answered = Assert.IsType<Ok<SettingsPolicyWriteResponse>>(result.Result).Value!;
        Assert.False(answered.Committed);
        Assert.Equal(2, answered.Version);
        Assert.Equal(expectedCode, answered.Code);
        Assert.Contains(expectedMessage, Assert.Single(answered.Messages), StringComparison.Ordinal);
    }

    /// <summary>A request that lost its body is not a decision to empty the policy, so neither is taken for the other.</summary>
    [Theory]
    [InlineData(0L, null, "carries the policy")]
    [InlineData(0L, "", "carries the policy")]
    [InlineData(-1L, "{}", "never negative")]
    public async Task SaveDeploymentPolicyAsync_ARequestStatingNoPolicyOrNoVersion_IsABadRequestAndWritesNothing(
        long version,
        string? document,
        string expected)
    {
        // Arrange
        var harness = new EndpointHarness();

        // Act
        var result = await SettingsPolicyEndpoints.SaveDeploymentPolicyAsync(
            harness.Policies,
            new SettingsPolicySaveRequest(version, document),
            TestContext.Current.CancellationToken);

        // Assert
        var problem = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Contains(expected, problem.ProblemDetails.Detail, StringComparison.Ordinal);
        Assert.Equal(0, harness.Store.Commits);
    }

    [Fact]
    public async Task SaveOrganizationPolicyAsync_NoOrganizationNamed_IsABadRequestAndWritesNothing()
    {
        // Arrange
        var harness = new EndpointHarness();

        // Act
        var result = await SettingsPolicyEndpoints.SaveOrganizationPolicyAsync(
            Guid.Empty,
            harness.Policies,
            new SettingsPolicySaveRequest(Version: 0, ForcingPolling),
            TestContext.Current.CancellationToken);

        // Assert
        var problem = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(0, harness.Store.Commits);
    }

    private sealed class EndpointHarness
    {
        internal EndpointHarness(AccessAuthorization? authorization = null)
        {
            this.Store.HoldingOrganization(Organization);

            this.Policies = new SettingsPolicyAdministration(
                authorization ?? AccessAuthorizations.ForAdministratorGranted(
                    MailFathomPermission.AdminRead,
                    MailFathomPermission.AdminConfigurationWrite),
                this.Store,
                new ConfigurationChangeAnnouncements(
                    () => Task.FromResult(new InMemoryBackplane().Connect()),
                    new RecordingLogger<ConfigurationChangeAnnouncements>()));
        }

        internal SettingsPolicyAdministration Policies { get; }

        internal InMemorySettingsPolicies Store { get; } = new();
    }
}
