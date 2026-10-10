// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Nodes;
using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Domain.Failures;
using MailFathom.Host.Configuration.Policies;
using MailFathom.Host.Signals;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Persistence.Policies;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Policies;

/// <summary>
/// Covers what is done to the policy one scope holds: who may read and write which, that a write is judged whole and
/// committed over the version it was composed over, that the write which commits second is the one refused, and that
/// only a commit is announced.
/// </summary>
public sealed class SettingsPolicyAdministrationTests
{
    private const string ForcingPolling = """{"MailAccounts":{"Forced":{"Mode":"Polling"}}}""";

    private const string DefaultingPolish = """{"Users":{"Defaults":{"Language":"Polish"}}}""";

    private static readonly Guid Organization = AccessAuthorizations.ScopedOrganization;

    public static TheoryData<AssignmentScope> ScopesCoveringTheOrganization =>
    [
        AssignmentScope.Deployment,
        AssignmentScope.Organization(Organization),
    ];

    /// <summary>Another organization, and one member of this one: neither holds anything over the organization itself.</summary>
    public static TheoryData<AssignmentScope> ScopesOutsideTheOrganization =>
    [
        .. AccessAuthorizations.ScopesOutsideTheirTarget,
        AssignmentScope.User(AccessAuthorizations.ScopedHolder),
    ];

    [Fact]
    public async Task ReadAsync_AScopeStoringNoPolicy_ReadsOneStatingNothingAtTheVersionAFirstWriteIsComposedOver()
    {
        // Arrange
        var harness = new PolicyHarness(AccessAuthorizations.ForAdministratorGranted(MailFathomPermission.AdminRead));

        // Act
        var deployment = await harness.Policies.ReadAsync(organizationId: null, TestContext.Current.CancellationToken);
        var organization = await harness.Policies.ReadAsync(Organization, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(new SettingsPolicyDocument(null, "{}", Version: 0), deployment);
        Assert.Equal(new SettingsPolicyDocument(Organization, "{}", Version: 0), organization);
    }

    [Fact]
    public async Task ReadAsync_EachScope_ReadsItsOwnPolicyAndNoOtherScopes()
    {
        // Arrange
        var harness = new PolicyHarness(AccessAuthorizations.ForAdministratorGranted(MailFathomPermission.AdminRead));
        harness.Store.Holding(organizationId: null, ForcingPolling, version: 4);
        harness.Store.Holding(Organization, DefaultingPolish, version: 2);

        // Act
        var deployment = await harness.Policies.ReadAsync(organizationId: null, TestContext.Current.CancellationToken);
        var organization = await harness.Policies.ReadAsync(Organization, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(new SettingsPolicyDocument(null, ForcingPolling, Version: 4), deployment);
        Assert.Equal(new SettingsPolicyDocument(Organization, DefaultingPolish, Version: 2), organization);
    }

    [Fact]
    public async Task ReadAsync_AnOrganizationThisDeploymentDoesNotHold_IsAnsweredAsAbsent()
    {
        // Arrange
        var harness = new PolicyHarness(AccessAuthorizations.ForAdministratorGranted(MailFathomPermission.AdminRead));
        var unknown = new Guid("0198f0aa-0000-7000-8000-00000000f0ff");

        // Act
        var policy = await harness.Policies.ReadAsync(unknown, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(policy);
    }

    [Theory]
    [MemberData(nameof(ScopesCoveringTheOrganization))]
    public async Task ReadAsync_AGrantAtAScopeCoveringTheOrganization_ReadsItsPolicy(AssignmentScope scope)
    {
        // Arrange
        var harness = new PolicyHarness(AccessAuthorizations.ForAdministratorScopedAt(scope, MailFathomPermission.AdminRead));
        harness.Store.Holding(Organization, DefaultingPolish, version: 2);

        // Act
        var policy = await harness.Policies.ReadAsync(Organization, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, policy?.Version);
    }

    /// <summary>An organization outside the caller's scope is answered exactly as one nobody holds, so the answer says nothing about which it was.</summary>
    [Theory]
    [MemberData(nameof(ScopesOutsideTheOrganization))]
    public async Task ReadAsync_AGrantAtAScopeOutsideTheOrganization_IsAnsweredAsAbsent(AssignmentScope scope)
    {
        // Arrange
        var harness = new PolicyHarness(AccessAuthorizations.ForAdministratorScopedAt(scope, MailFathomPermission.AdminRead));
        harness.Store.Holding(Organization, DefaultingPolish, version: 2);

        // Act
        var policy = await harness.Policies.ReadAsync(Organization, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(policy);
    }

    /// <summary>The deployment's policy is the deployment's to read and write, so a grant held below it is refused rather than answered as nothing found.</summary>
    [Fact]
    public async Task ReadAsync_TheDeploymentsPolicyUnderAGrantHeldAtAnOrganization_IsRefused()
    {
        // Arrange
        var harness = new PolicyHarness(AccessAuthorizations.ForAdministratorScopedAt(
            AssignmentScope.Organization(Organization),
            MailFathomPermission.AdminRead));

        // Act
        var refusal = await Record.ExceptionAsync(() => harness.Policies.ReadAsync(
            organizationId: null,
            TestContext.Current.CancellationToken));

        // Assert
        Assert.IsType<PrincipalNotAuthorizedException>(refusal);
    }

    [Fact]
    public async Task ApplyAsync_TheDeploymentsPolicyUnderAGrantHeldAtAnOrganization_IsRefusedAndWritesNothing()
    {
        // Arrange
        var harness = new PolicyHarness(AccessAuthorizations.ForAdministratorScopedAt(
            AssignmentScope.Organization(Organization),
            MailFathomPermission.AdminConfigurationWrite));

        // Act
        var refusal = await Record.ExceptionAsync(() => harness.Policies.ApplyAsync(
            organizationId: null,
            ForcingPolling,
            expectedVersion: 0,
            TestContext.Current.CancellationToken));

        // Assert
        Assert.IsType<PrincipalNotAuthorizedException>(refusal);
        Assert.Equal(0, harness.Store.Commits);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApplyAsync_ACallerGrantedOnlyTheRead_IsRefusedAndWritesNothing(bool anOrganizations)
    {
        // Arrange
        var harness = new PolicyHarness(AccessAuthorizations.ForAdministratorGranted(MailFathomPermission.AdminRead));

        // Act
        var refusal = await Record.ExceptionAsync(() => harness.Policies.ApplyAsync(
            anOrganizations ? Organization : null,
            ForcingPolling,
            expectedVersion: 0,
            TestContext.Current.CancellationToken));

        // Assert
        Assert.IsType<PrincipalNotAuthorizedException>(refusal);
        Assert.Equal(0, harness.Store.Commits);
    }

    [Theory]
    [MemberData(nameof(ScopesCoveringTheOrganization))]
    public async Task ApplyAsync_AGrantAtAScopeCoveringTheOrganization_WritesItsPolicy(AssignmentScope scope)
    {
        // Arrange
        var harness = new PolicyHarness(AccessAuthorizations.ForAdministratorScopedAt(
            scope,
            MailFathomPermission.AdminConfigurationWrite));

        // Act
        var outcome = await harness.Policies.ApplyAsync(
            Organization,
            DefaultingPolish,
            expectedVersion: 0,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome!.IsCommitted);
        Assert.Equal(1, outcome.Version);
    }

    [Theory]
    [MemberData(nameof(ScopesOutsideTheOrganization))]
    public async Task ApplyAsync_AGrantAtAScopeOutsideTheOrganization_IsAnsweredAsAbsentAndWritesNothing(AssignmentScope scope)
    {
        // Arrange
        var harness = new PolicyHarness(AccessAuthorizations.ForAdministratorScopedAt(
            scope,
            MailFathomPermission.AdminConfigurationWrite));

        // Act
        var outcome = await harness.Policies.ApplyAsync(
            Organization,
            DefaultingPolish,
            expectedVersion: 0,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(outcome);
        Assert.Equal(0, harness.Store.Commits);
    }

    /// <summary>A scope that stored no policy takes its first one over version zero, and every later one over the version before it.</summary>
    [Fact]
    public async Task ApplyAsync_AFirstPolicyAndThenASecond_CommitsEachOverTheVersionItWasComposedOver()
    {
        // Arrange
        var harness = PolicyHarness.ForTheDeploymentsAdministrator();

        // Act
        var first = await harness.Policies.ApplyAsync(null, ForcingPolling, expectedVersion: 0, TestContext.Current.CancellationToken);
        var second = await harness.Policies.ApplyAsync(null, DefaultingPolish, expectedVersion: 1, TestContext.Current.CancellationToken);
        var held = await harness.Store.ReadAsync(organizationId: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, first!.Version);
        Assert.Equal(2, second!.Version);
        Assert.True(second.IsCommitted);
        Assert.Empty(second.Messages);
        Assert.False(second.Refusal.IsSpecified);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(DefaultingPolish), JsonNode.Parse(held!.Json)));
    }

    /// <summary>The deployment's policy and an organization's are separate documents, so writing one leaves the other where it stood.</summary>
    [Fact]
    public async Task ApplyAsync_AnOrganizationsPolicy_LeavesTheDeploymentsWhereItStood()
    {
        // Arrange
        var harness = PolicyHarness.ForTheDeploymentsAdministrator();
        harness.Store.Holding(organizationId: null, ForcingPolling, version: 4);

        // Act
        await harness.Policies.ApplyAsync(Organization, DefaultingPolish, expectedVersion: 0, TestContext.Current.CancellationToken);
        var deployment = await harness.Store.ReadAsync(organizationId: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(new SettingsPolicyDocument(null, ForcingPolling, Version: 4), deployment);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(5)]
    public async Task ApplyAsync_APolicyComposedOverAVersionNoLongerInForce_IsRefusedNamingTheOneThatIs(long composedOver)
    {
        // Arrange
        var harness = PolicyHarness.ForTheDeploymentsAdministrator();
        harness.Store.Holding(organizationId: null, ForcingPolling, version: 4);

        // Act
        var outcome = await harness.Policies.ApplyAsync(
            organizationId: null,
            DefaultingPolish,
            composedOver,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(outcome!.IsCommitted);
        Assert.Equal(MailFathomErrorCode.ConfigurationVersionSuperseded, outcome.Refusal);
        Assert.Equal(4, outcome.Version);
        Assert.Contains($"version {composedOver}, and version 4 is in force", Assert.Single(outcome.Messages), StringComparison.Ordinal);
        Assert.Equal(0, harness.Store.Commits);
    }

    /// <summary>A superseded write is told so, rather than told about faults in a document it is about to compose again.</summary>
    [Fact]
    public async Task ApplyAsync_ASupersededPolicyThatIsAlsoInvalid_IsRefusedAsSuperseded()
    {
        // Arrange
        var harness = PolicyHarness.ForTheDeploymentsAdministrator();
        harness.Store.Holding(organizationId: null, ForcingPolling, version: 4);

        // Act
        var outcome = await harness.Policies.ApplyAsync(
            organizationId: null,
            """{"Users":{"Defaults":{"Languagee":"Polish"}}}""",
            expectedVersion: 3,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFathomErrorCode.ConfigurationVersionSuperseded, outcome!.Refusal);
    }

    /// <summary>
    /// Two administrators who read the same version each compose a valid policy over it. The statement settles them:
    /// the one that commits second matches nothing, whichever replica each reached, and is told the version the first
    /// produced.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_APolicyAnotherAdministratorMovedWhileThisOneWasJudged_IsRefusedWithTheVersionThatWon()
    {
        // Arrange
        var harness = PolicyHarness.ForTheDeploymentsAdministrator();
        harness.Store.Holding(Organization, ForcingPolling, version: 4);
        harness.Store.BeforeCommit = () =>
        {
            harness.Store.BeforeCommit = null;
            harness.Store.Holding(Organization, ForcingPolling, version: 5);
        };

        // Act
        var outcome = await harness.Policies.ApplyAsync(
            Organization,
            DefaultingPolish,
            expectedVersion: 4,
            TestContext.Current.CancellationToken);
        var held = await harness.Store.ReadAsync(Organization, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(outcome!.IsCommitted);
        Assert.Equal(MailFathomErrorCode.ConfigurationVersionSuperseded, outcome.Refusal);
        Assert.Equal(5, outcome.Version);
        Assert.Equal(ForcingPolling, held!.Json);
    }

    /// <summary>An organization removed underneath a write took its policy with it, so there is nothing left to write for.</summary>
    [Fact]
    public async Task ApplyAsync_AnOrganizationRemovedWhileItsPolicyWasJudged_IsAnsweredAsAbsent()
    {
        // Arrange
        var harness = PolicyHarness.ForTheDeploymentsAdministrator();
        harness.Store.BeforeCommit = () => harness.Store.RemovingOrganization(Organization);

        // Act
        var outcome = await harness.Policies.ApplyAsync(
            Organization,
            DefaultingPolish,
            expectedVersion: 0,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(outcome);
    }

    /// <summary>A candidate is committed whole or not at all, so one fault among valid statements leaves the policy where it stood.</summary>
    [Fact]
    public async Task ApplyAsync_APolicyWithOneFaultAmongValidStatements_IsRefusedWholeNamingTheFault()
    {
        // Arrange
        var harness = PolicyHarness.ForTheDeploymentsAdministrator();
        harness.Store.Holding(organizationId: null, ForcingPolling, version: 4);

        // Act
        var outcome = await harness.Policies.ApplyAsync(
            organizationId: null,
            """{"Users":{"Defaults":{"Language":"Polish"},"Forced":{"DisplayName":"Somebody"}},"MailAccounts":{"Forced":{"Mode":"Polling"}}}""",
            expectedVersion: 4,
            TestContext.Current.CancellationToken);
        var held = await harness.Store.ReadAsync(organizationId: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(outcome!.IsCommitted);
        Assert.Equal(MailFathomErrorCode.ConfigurationCandidateInvalid, outcome.Refusal);
        Assert.Equal(4, outcome.Version);
        Assert.Contains("Users:Forced states DisplayName", Assert.Single(outcome.Messages), StringComparison.Ordinal);
        Assert.Equal(new SettingsPolicyDocument(null, ForcingPolling, Version: 4), held);
    }

    /// <summary>Saving what is already in force writes nothing, so a version never moves for a policy that did not.</summary>
    [Theory]
    [InlineData(ForcingPolling)]
    [InlineData("""{ "MailAccounts": { "Forced": { "Mode": "Polling" } } }""")]
    public async Task ApplyAsync_APolicyStatingWhatTheOneInForceStates_WritesNothingAndSaysSo(string saved)
    {
        // Arrange
        var harness = PolicyHarness.ForTheDeploymentsAdministrator();
        harness.Store.Holding(organizationId: null, ForcingPolling, version: 4);

        // Act
        var outcome = await harness.Policies.ApplyAsync(
            organizationId: null,
            saved,
            expectedVersion: 4,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(outcome!.IsCommitted);
        Assert.False(outcome.Refusal.IsSpecified);
        Assert.Equal(4, outcome.Version);
        Assert.Contains("version 4 stays in force", Assert.Single(outcome.Messages), StringComparison.Ordinal);
        Assert.Equal(0, harness.Store.Commits);
    }

    /// <summary>An emptied policy is a decision like any other, so it is committed and the version moves.</summary>
    [Fact]
    public async Task ApplyAsync_APolicyEmptied_IsCommittedAsOneStatingNothing()
    {
        // Arrange
        var harness = PolicyHarness.ForTheDeploymentsAdministrator();
        harness.Store.Holding(organizationId: null, ForcingPolling, version: 4);

        // Act
        var outcome = await harness.Policies.ApplyAsync(
            organizationId: null,
            "{}",
            expectedVersion: 4,
            TestContext.Current.CancellationToken);
        var held = await harness.Store.ReadAsync(organizationId: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome!.IsCommitted);
        Assert.Equal(new SettingsPolicyDocument(null, "{}", Version: 5), held);
    }

    /// <summary>A committed policy is announced, so a replica that did not commit it reads it without waiting out its interval.</summary>
    [Fact]
    public async Task ApplyAsync_ACommittedPolicy_IsAnnouncedOnceItIsHeld()
    {
        // Arrange
        var harness = PolicyHarness.ForTheDeploymentsAdministrator();
        var heard = await RosterAnnouncementListener.ListenAsync(harness.Backplane, () => harness.Store.Commits);

        // Act
        await harness.Policies.ApplyAsync(Organization, DefaultingPolish, expectedVersion: 0, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([1], heard);
    }

    /// <summary>A write that moved nothing asks no replica to read anything again.</summary>
    [Theory]
    [InlineData("""{"Users":{"Defaults":{"Languagee":"Polish"}}}""", 4)]
    [InlineData(ForcingPolling, 4)]
    [InlineData(DefaultingPolish, 3)]
    public async Task ApplyAsync_AWriteThatMovedNothing_AnnouncesNothing(string saved, long composedOver)
    {
        // Arrange
        var harness = PolicyHarness.ForTheDeploymentsAdministrator();
        harness.Store.Holding(organizationId: null, ForcingPolling, version: 4);
        var heard = await RosterAnnouncementListener.ListenAsync(harness.Backplane, () => true);

        // Act
        await harness.Policies.ApplyAsync(organizationId: null, saved, composedOver, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(heard);
    }

    private sealed class PolicyHarness
    {
        internal PolicyHarness(AccessAuthorization authorization)
        {
            this.Store.HoldingOrganization(Organization);

            this.Policies = new SettingsPolicyAdministration(
                authorization,
                this.Store,
                new ConfigurationChangeAnnouncements(
                    () => Task.FromResult(this.Backplane.Connect()),
                    new RecordingLogger<ConfigurationChangeAnnouncements>()));
        }

        internal SettingsPolicyAdministration Policies { get; }

        /// <summary>Gets the policies this deployment holds, which is none for either scope until a test says otherwise.</summary>
        internal InMemorySettingsPolicies Store { get; } = new();

        /// <summary>Gets the backplane a commit is announced over, which nobody hears until a test listens.</summary>
        internal InMemoryBackplane Backplane { get; } = new();

        /// <summary>Arranges an administrator who may read and write every scope's policy.</summary>
        internal static PolicyHarness ForTheDeploymentsAdministrator() =>
            new(AccessAuthorizations.ForAdministratorGranted(
                MailFathomPermission.AdminRead,
                MailFathomPermission.AdminConfigurationWrite));
    }
}
