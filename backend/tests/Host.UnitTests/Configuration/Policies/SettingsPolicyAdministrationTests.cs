// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json.Nodes;
using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;
using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.Policies;
using MailFathom.Host.Configuration.SensitiveContent;
using MailFathom.Host.Signals;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Persistence.Policies;
using MailFathom.Infrastructure.Persistence.Users;
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

    private const string ForcingPush = """{"MailAccounts":{"Forced":{"Mode":"Push"}}}""";

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

    /// <summary>
    /// A default or a forced value for a mail account can decide where a mailbox's credential is presented. A grant
    /// held below the deployment therefore may not move the effective settings of an account naming a secret that
    /// was not provisioned for a user it is assigned to — whichever setting moves, and whether the write adds it,
    /// changes it, or takes it away — and the refusal names the account.
    /// </summary>
    [Theory]
    [InlineData("{}", """{"MailAccounts":{"Forced":{"Host":"mail.example.org"}}}""")]
    [InlineData("{}", """{"MailAccounts":{"Defaults":{"Port":1993}}}""")]
    [InlineData(ForcingPush, ForcingPolling)]
    [InlineData(ForcingPush, "{}")]
    public async Task ApplyAsync_AGrantHeldAtTheOrganizationMovingAnAccountNamingASecretNotItsUsers_IsRefusedNamingTheAccount(
        string inForce,
        string saved)
    {
        // Arrange
        var harness = PolicyHarness.ForTheOrganizationsAdministrator();
        harness.Store.Holding(Organization, inForce, version: 4);
        var shared = Mailbox("shared", "file:/run/secrets/shared-password");
        harness.Store.HoldingAccount(shared, Organization, SyntheticUser.Deployment);

        // Act
        var outcome = await harness.Policies.ApplyAsync(Organization, saved, expectedVersion: 4, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(outcome!.IsCommitted);
        Assert.Equal(MailFathomErrorCode.ConfigurationCandidateInvalid, outcome.Refusal);
        Assert.Equal(4, outcome.Version);
        Assert.StartsWith("The saved policy is refused over 1 of the 1 records", outcome.Messages[0], StringComparison.Ordinal);
        Assert.StartsWith($"Mail account 'shared' ({shared.Id:D})", outcome.Messages[1], StringComparison.Ordinal);
        Assert.Contains("was not provisioned for a user this mail account is assigned to", outcome.Messages[1], StringComparison.Ordinal);
        Assert.Equal(0, harness.Store.Commits);
    }

    /// <summary>
    /// The bound is on what the write moves and for whom. The same grant states a mail-account value where every
    /// account it moves names its own user's material, and where the account naming somebody else's states the
    /// setting itself and so is served what it was.
    /// </summary>
    [Theory]
    [InlineData(true, ForcingPolling)]
    [InlineData(false, """{"MailAccounts":{"Defaults":{"Host":"mail.example.org"}}}""")]
    public async Task ApplyAsync_AGrantHeldAtTheOrganizationMovingNoAccountNamingASecretNotItsUsers_Commits(
        bool provisionedForItsUser,
        string saved)
    {
        // Arrange
        var harness = PolicyHarness.ForTheOrganizationsAdministrator();
        harness.Store.HoldingAccount(
            Mailbox(
                "work",
                provisionedForItsUser
                    ? $"file:/run/secrets/user-{SyntheticUser.Deployment.Value:D}-work"
                    : "file:/run/secrets/shared-password"),
            Organization,
            SyntheticUser.Deployment);

        // Act
        var outcome = await harness.Policies.ApplyAsync(Organization, saved, expectedVersion: 0, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome!.IsCommitted);
    }

    /// <summary>A grant held over the whole deployment names any secret, so the same write passes under it.</summary>
    [Fact]
    public async Task ApplyAsync_AGrantHeldAtTheDeploymentMovingAnAccountNamingASecretNotItsUsers_Commits()
    {
        // Arrange
        var harness = PolicyHarness.ForTheDeploymentsAdministrator();
        harness.Store.HoldingAccount(Mailbox("shared", "file:/run/secrets/shared-password"), Organization, SyntheticUser.Deployment);

        // Act
        var outcome = await harness.Policies.ApplyAsync(
            Organization,
            """{"MailAccounts":{"Forced":{"Host":"mail.example.org"}}}""",
            expectedVersion: 0,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome!.IsCommitted);
    }

    /// <summary>
    /// A policy outlives the build that stored it, and during an upgrade two builds serve one. What an account was
    /// served under a policy this build does not judge whole is unknown here, so a grant held below the deployment is
    /// not given the benefit of that doubt: replacing such a policy counts as moving every account it governs.
    /// </summary>
    [Theory]
    [InlineData(ForcingPolling)]
    [InlineData(DefaultingPolish)]
    public async Task ApplyAsync_APolicyInForceThisBuildCannotJudgeUnderAGrantHeldAtTheOrganization_IsRefusedOverAnAccountNamingASecretNotItsUsers(string saved)
    {
        // Arrange
        var harness = PolicyHarness.ForTheOrganizationsAdministrator();
        harness.Store.Holding(
            Organization,
            """{"MailAccounts":{"Forced":{"Mode":"Polling","SettingALaterBuildGoverns":true}}}""",
            version: 4);
        harness.Store.HoldingAccount(Mailbox("shared", "file:/run/secrets/shared-password"), Organization, SyntheticUser.Deployment);

        // Act
        var outcome = await harness.Policies.ApplyAsync(Organization, saved, expectedVersion: 4, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(outcome!.IsCommitted);
        Assert.Equal(MailFathomErrorCode.ConfigurationCandidateInvalid, outcome.Refusal);
        Assert.Contains("Mail account 'shared'", outcome.Messages[1], StringComparison.Ordinal);
        Assert.Equal(0, harness.Store.Commits);
    }

    /// <summary>The deployment's administrator is who corrects a policy this build cannot judge, so that grant still replaces it.</summary>
    [Fact]
    public async Task ApplyAsync_APolicyInForceThisBuildCannotJudgeUnderAGrantHeldAtTheDeployment_IsReplaced()
    {
        // Arrange
        var harness = PolicyHarness.ForTheDeploymentsAdministrator();
        harness.Store.Holding(
            Organization,
            """{"MailAccounts":{"Forced":{"Mode":"Polling","SettingALaterBuildGoverns":true}}}""",
            version: 4);

        // Act
        var outcome = await harness.Policies.ApplyAsync(Organization, ForcingPolling, expectedVersion: 4, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome!.IsCommitted);
    }

    /// <summary>The same write under a grant at the deployment is the deployment's to make, so it is not asked.</summary>
    [Fact]
    public async Task ApplyAsync_AMailAccountValueUnderAGrantHeldAtTheDeployment_IsWrittenToTheOrganizationsPolicy()
    {
        // Arrange
        var harness = PolicyHarness.ForTheDeploymentsAdministrator();

        // Act
        var outcome = await harness.Policies.ApplyAsync(
            Organization,
            """{"MailAccounts":{"Forced":{"Host":"mail.example.org"}}}""",
            expectedVersion: 0,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome!.IsCommitted);
    }

    /// <summary>
    /// A policy is written whole, so an organization's administrator saves back the values the deployment's stated for
    /// its mail accounts. Leaving them as they stand is not stating one, however the keys were typed, and what the
    /// administrator may write beside them — the section about users, and an editing restriction — is written.
    /// </summary>
    [Theory]
    [InlineData("""{"mailaccounts":{"forced":{"mode":"Polling"}},"Users":{"Defaults":{"Language":"Polish"}}}""")]
    [InlineData("""{"MailAccounts":{"Forced":{"Mode":"Polling"},"Editing":{"Mode":"AllExcept","Properties":["Host"]}}}""")]
    [InlineData("""{"MailAccounts":{"Forced":{"Mode":"Polling"},"Defaults":{"Delivery":{}}}}""")]
    public async Task ApplyAsync_APolicyLeavingMailAccountValuesAsTheyStandUnderAGrantHeldAtTheOrganization_IsWritten(string saved)
    {
        // Arrange
        var harness = PolicyHarness.ForTheOrganizationsAdministrator();
        harness.Store.Holding(Organization, ForcingPolling, version: 4);

        // Act
        var outcome = await harness.Policies.ApplyAsync(Organization, saved, expectedVersion: 4, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome!.IsCommitted);
        Assert.Equal(5, outcome.Version);
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
            """{"Users":{"Defaults":{"Dialect":"Polish"}}}""",
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
    [InlineData("""{"Users":{"Defaults":{"Dialect":"Polish"}}}""", 4)]
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

    /// <summary>
    /// A policy may not leave a record it governs unservable, so a write that takes away the language a record relied
    /// on is refused naming the record and what it would be left without.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_APolicyThatWouldLeaveARecordWithoutWhatItReliesOn_IsRefusedNamingTheRecordAndWritesNothing()
    {
        // Arrange
        var harness = PolicyHarness.ForTheDeploymentsAdministrator();
        harness.Store.Holding(Organization, DefaultingPolish, version: 2);
        harness.Store.HoldingUser(SyntheticUser.Deployment, "alex", Organization, "{}");

        // Act
        var outcome = await harness.Policies.ApplyAsync(
            Organization,
            "{}",
            expectedVersion: 2,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFathomErrorCode.ConfigurationCandidateInvalid, outcome!.Refusal);
        Assert.StartsWith("The saved policy is refused over 1 of the 1 records", outcome.Messages[0], StringComparison.Ordinal);
        Assert.Contains("User 'alex'", outcome.Messages[1], StringComparison.Ordinal);
        Assert.Contains("Language is not stated", outcome.Messages[1], StringComparison.Ordinal);
        Assert.Equal(0, harness.Store.Commits);
    }

    /// <summary>
    /// What a request authenticates against is a row, so a policy that moves the switches of the users it governs
    /// rewrites their rows in its own commit, and leaves everybody it does not govern where they were.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_APolicyForcingAnEndpointSwitch_RewritesTheRowsOfTheUsersItGovernsAndNoOthers()
    {
        // Arrange
        var harness = PolicyHarness.ForTheDeploymentsAdministrator();
        harness.Store.HoldingUser(SyntheticUser.Deployment, "alex", Organization, """{"Language":"English"}""");
        harness.Store.HoldingUser(SyntheticUser.Another, "sam", organizationId: null, """{"Language":"English"}""");

        // Act
        var outcome = await harness.Policies.ApplyAsync(
            Organization,
            """{"Users":{"Forced":{"EndpointAccess":{"McpEndpoint":false}}}}""",
            expectedVersion: 0,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome!.IsCommitted);
        Assert.Equal(
            new UserEndpointAccess(McpEndpoint: false, ClientEndpoint: true),
            harness.Store.EndpointAccessOf(SyntheticUser.Deployment));
        Assert.Equal(UserEndpointAccess.Everywhere, harness.Store.EndpointAccessOf(SyntheticUser.Another));
    }

    /// <summary>The judgement runs inside the write, so how many records one write judges is bounded and a write past the bound reads none.</summary>
    [Fact]
    public async Task ApplyAsync_APolicyGoverningMoreRecordsThanOneWriteJudges_IsRefusedNamingTheSettingThatRaisesTheBound()
    {
        // Arrange
        var harness = PolicyHarness.ForTheDeploymentsAdministrator();
        harness.Deployment.ReportReload(new DeploymentOptions { MaximumRecordsJudgedPerPolicyWrite = 1 });
        harness.Store.HoldingUser(SyntheticUser.Deployment, "alex", organizationId: null, """{"Language":"English"}""");
        harness.Store.HoldingUser(SyntheticUser.Another, "sam", organizationId: null, """{"Language":"English"}""");

        // Act
        var outcome = await harness.Policies.ApplyAsync(
            organizationId: null,
            DefaultingPolish,
            expectedVersion: 0,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFathomErrorCode.ConfigurationCandidateInvalid, outcome!.Refusal);
        Assert.Contains(
            "Deployment:MaximumRecordsJudgedPerPolicyWrite",
            Assert.Single(outcome.Messages),
            StringComparison.Ordinal);
        Assert.Equal(0, harness.Store.Commits);
    }

    /// <summary>
    /// A policy may not leave an account it governs one no start would serve, so a write that takes away the language
    /// an account relied on is refused naming the account by its label, never by its address.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_APolicyThatWouldLeaveAMailAccountWithoutWhatItReliesOn_IsRefusedNamingTheAccountAndWritesNothing()
    {
        // Arrange
        var harness = PolicyHarness.ForTheDeploymentsAdministrator();
        harness.Store.Holding(Organization, """{"MailAccounts":{"Defaults":{"Language":"Polish"}}}""", version: 2);
        var work = Mailbox("work", "file:/run/secrets/work-password", language: null);
        harness.Store.HoldingAccount(work, Organization, SyntheticUser.Deployment);

        // Act
        var outcome = await harness.Policies.ApplyAsync(Organization, "{}", expectedVersion: 2, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFathomErrorCode.ConfigurationCandidateInvalid, outcome!.Refusal);
        Assert.StartsWith("The saved policy is refused over 1 of the 1 records", outcome.Messages[0], StringComparison.Ordinal);
        Assert.StartsWith($"Mail account 'work' ({work.Id:D})", outcome.Messages[1], StringComparison.Ordinal);
        Assert.Contains("Language", outcome.Messages[1], StringComparison.Ordinal);
        Assert.DoesNotContain("work@example.test", string.Concat(outcome.Messages), StringComparison.Ordinal);
        Assert.Equal(0, harness.Store.Commits);
    }

    /// <summary>
    /// What a deployment reads across accounts is a row's columns, so a policy that moves what the accounts it governs
    /// are served rewrites their columns in its own commit, and leaves every account it does not govern where it was.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_APolicyForcingTheFolders_RewritesTheSettingsOfTheAccountsItGovernsAndNoOthers()
    {
        // Arrange
        var harness = PolicyHarness.ForTheDeploymentsAdministrator();
        var governed = Mailbox("work", "file:/run/secrets/work-password");
        var elsewhere = Mailbox("home", "file:/run/secrets/home-password");
        harness.Store.HoldingAccount(governed, Organization, SyntheticUser.Deployment);
        harness.Store.HoldingAccount(elsewhere, organizationId: null, SyntheticUser.Another);

        // Act
        var outcome = await harness.Policies.ApplyAsync(
            Organization,
            """{"MailAccounts":{"Forced":{"Folders":[{"Alias":"INBOX","RemotePath":"INBOX"}]}}}""",
            expectedVersion: 0,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome!.IsCommitted);
        Assert.Equal([MailFolderAlias.Create("INBOX")], harness.Store.SettingsOf(governed.Id)!.Folders.Select(folder => folder.Alias));
        Assert.Null(harness.Store.SettingsOf(elsewhere.Id));
    }

    /// <summary>
    /// The rule that an account may switch a scanner on and never off is asked of the account as the policy would
    /// leave it served, so a default switching one off under a deployment that requires it is refused over an account
    /// stating nothing about scanning.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_ADefaultSwitchingOffAScannerTheDeploymentRequires_IsRefusedOverTheAccountsItWouldReach()
    {
        // Arrange
        var scanning = new SensitiveContentOptions();
        scanning.Secrets.Enabled = true;
        var harness = new PolicyHarness(
            AccessAuthorizations.ForAdministratorGranted(MailFathomPermission.AdminRead, MailFathomPermission.AdminConfigurationWrite),
            InMemorySettingsPolicies.JudgingRecords(scanning));
        harness.Store.HoldingAccount(Mailbox("work", "file:/run/secrets/work-password"), Organization, SyntheticUser.Deployment);

        // Act
        var outcome = await harness.Policies.ApplyAsync(
            Organization,
            """{"MailAccounts":{"Defaults":{"SensitiveContent":{"Secrets":{"Enabled":false}}}}}""",
            expectedVersion: 0,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFathomErrorCode.ConfigurationCandidateInvalid, outcome!.Refusal);
        Assert.Contains("SensitiveContent:Secrets:Enabled", outcome.Messages[1], StringComparison.Ordinal);
        Assert.Equal(0, harness.Store.Commits);
    }

    /// <summary>One bound covers the write, so the users and the mail accounts it would judge are counted together.</summary>
    [Fact]
    public async Task ApplyAsync_APolicyGoverningAUserAndAMailAccountPastTheBoundTogether_IsRefusedCountingBoth()
    {
        // Arrange
        var harness = PolicyHarness.ForTheDeploymentsAdministrator();
        harness.Deployment.ReportReload(new DeploymentOptions { MaximumRecordsJudgedPerPolicyWrite = 1 });
        harness.Store.HoldingUser(SyntheticUser.Deployment, "alex", organizationId: null, """{"Language":"English"}""");
        harness.Store.HoldingAccount(Mailbox("work", "file:/run/secrets/work-password"), organizationId: null, SyntheticUser.Deployment);

        // Act
        var outcome = await harness.Policies.ApplyAsync(
            organizationId: null,
            """{"Users":{"Defaults":{"Language":"Polish"}},"MailAccounts":{"Defaults":{"Port":1993}}}""",
            expectedVersion: 0,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFathomErrorCode.ConfigurationCandidateInvalid, outcome!.Refusal);
        Assert.Contains("for 2 records", Assert.Single(outcome.Messages), StringComparison.Ordinal);
        Assert.Equal(0, harness.Store.Commits);
    }

    /// <summary>
    /// Only a default or a forced value can break a record or move what it is served, and only for the kind of record
    /// it is stated about, so a write judges the users where it moves one for users, the mail accounts where it moves
    /// one for mail accounts, and nothing where it states an editing restriction alone.
    /// </summary>
    [Theory]
    [InlineData(DefaultingPolish, true, false)]
    [InlineData("""{"Users":{"Editing":{"Mode":"AllExcept","Properties":["TimeZone"]}}}""", false, false)]
    [InlineData("""{"MailAccounts":{"Editing":{"Mode":"AllExcept","Properties":["Folders"]}}}""", false, false)]
    [InlineData(ForcingPolling, false, true)]
    public async Task ApplyAsync_AnyPolicy_JudgesTheRecordsOfTheKindItMovesADefaultOrAForcedValueFor(
        string policy,
        bool users,
        bool mailAccounts)
    {
        // Arrange
        var harness = PolicyHarness.ForTheDeploymentsAdministrator();

        // Act
        var outcome = await harness.Policies.ApplyAsync(
            organizationId: null,
            policy,
            expectedVersion: 0,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome!.IsCommitted);
        Assert.Equal(
            new GovernedRecordsToJudge(users, mailAccounts, new DeploymentOptions().MaximumRecordsJudgedPerPolicyWrite),
            harness.Store.LastJudged);
    }

    /// <summary>A mailbox a policy write reaches, named by its label and reading its credential from one reference.</summary>
    private static MailAccountRecord Mailbox(string label, string secretReference, string? language = "English")
    {
        var statedLanguage = language is null ? string.Empty : $"\"Language\": \"{language}\",";

        return new MailAccountRecord(
            Guid.NewGuid(),
            $"{label}@example.test",
            label,
            $$"""
              {
                {{statedLanguage}}
                "Host": "imap.example.test",
                "UserName": "{{label}}@example.test",
                "Secrets": { "Password": { "Name": "{{label}}-password", "SecretReference": "{{secretReference}}" } }
              }
              """,
            Version: 1);
    }

    private sealed class PolicyHarness
    {
        internal PolicyHarness(AccessAuthorization authorization, InMemorySettingsPolicies? store = null)
        {
            this.Store = store ?? InMemorySettingsPolicies.JudgingRecords();
            this.Store.HoldingOrganization(Organization);

            this.Policies = new SettingsPolicyAdministration(
                authorization,
                this.Store,
                this.Deployment,
                new ConfigurationChangeAnnouncements(
                    () => Task.FromResult(this.Backplane.Connect()),
                    new RecordingLogger<ConfigurationChangeAnnouncements>()));
        }

        internal SettingsPolicyAdministration Policies { get; }

        /// <summary>Gets the policies this deployment holds, which is none for either scope until a test says otherwise.</summary>
        internal InMemorySettingsPolicies Store { get; }

        /// <summary>Gets the deployment's own options, which bound how many records one policy write judges.</summary>
        internal TestOptionsMonitor<DeploymentOptions> Deployment { get; } = new(new DeploymentOptions());

        /// <summary>Gets the backplane a commit is announced over, which nobody hears until a test listens.</summary>
        internal InMemoryBackplane Backplane { get; } = new();

        /// <summary>Arranges an administrator who may write the organization's policy and holds nothing over the deployment.</summary>
        internal static PolicyHarness ForTheOrganizationsAdministrator() =>
            new(AccessAuthorizations.ForAdministratorScopedAt(
                AssignmentScope.Organization(Organization),
                MailFathomPermission.AdminConfigurationWrite));

        /// <summary>Arranges an administrator who may read and write every scope's policy.</summary>
        internal static PolicyHarness ForTheDeploymentsAdministrator() =>
            new(AccessAuthorizations.ForAdministratorGranted(
                MailFathomPermission.AdminRead,
                MailFathomPermission.AdminConfigurationWrite));
    }
}
