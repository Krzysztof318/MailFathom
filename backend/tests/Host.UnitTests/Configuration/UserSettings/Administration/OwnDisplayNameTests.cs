// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.UserSettings.Administration;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Persistence.Policies;
using MailFathom.Infrastructure.Persistence.Users;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.UserSettings.Administration;

/// <summary>
/// Covers the name a person is recorded under, read and corrected by that person rather than by an administrator. What
/// is worth stating is the gate — the write is the record's own grant and is refused for a person a configuration
/// source still declares — that the read reports whether a write would be accepted instead of leaving a client to find
/// out by being refused, and that the bounds a declaration is judged by are the ones a person's own change meets.
/// </summary>
public sealed class OwnDisplayNameTests
{
    private const string WithholdingTheName = """{"Users":{"Editing":{"Mode":"AllExcept","Properties":["DisplayName"]}}}""";
    private const string NotAPolicy = """{"Users":{"Forced":{"Dialect":"Silesian"}}}""";

    [Fact]
    public async Task ReadAsync_APersonThisDeploymentHolds_HandsThemTheNameItRecordsThemUnder()
    {
        // Arrange
        var harness = new NameHarness(MailFathomPermission.ProfileRead);
        harness.Recording("Ada Lovelace");

        // Act
        var read = await harness.Names.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Ada Lovelace", read!.Value.DisplayName);
    }

    /// <summary>The whole reason the flag travels with the name: a client draws the name as text rather than offering a field the deployment would refuse.</summary>
    [Fact]
    public async Task ReadAsync_ACallerGrantedTheProfileWrite_ReportsTheNameAsChangeable()
    {
        // Arrange
        var harness = new NameHarness(MailFathomPermission.ProfileRead, MailFathomPermission.ProfileWrite);
        harness.Recording("Ada Lovelace");

        // Act
        var read = await harness.Names.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(read!.Value.Changeable);
    }

    [Fact]
    public async Task ReadAsync_ACallerWithoutTheProfileWrite_ReportsTheNameAsUnchangeable()
    {
        // Arrange
        var harness = new NameHarness(MailFathomPermission.ProfileRead);
        harness.Recording("Ada Lovelace");

        // Act
        var read = await harness.Names.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(read!.Value.Changeable);
    }

    /// <summary>Reached where the row behind an authenticated caller has gone, which is a user erased under a credential that has not yet been withdrawn.</summary>
    [Fact]
    public async Task ReadAsync_APersonThisDeploymentDoesNotHold_AnswersNothing()
    {
        // Arrange
        var harness = new NameHarness(MailFathomPermission.ProfileRead);

        // Act
        var read = await harness.Names.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(read);
    }

    /// <summary>A person is asked about by the credential that authenticated, so the read is a lookup on that user rather than a roster filtered down to them.</summary>
    [Fact]
    public async Task ReadAsync_Always_AsksAboutTheUserTheCredentialActsForAndNoOneElse()
    {
        // Arrange
        var harness = new NameHarness(MailFathomPermission.ProfileRead);
        harness.Recording("Ada Lovelace");

        // Act
        await harness.Names.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        await harness.Directory.Received(1)
            .ReadUserAsync(SyntheticUser.Deployment, Arg.Any<CancellationToken>());
        await harness.Directory.DidNotReceive().ReadUsersAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReadAsync_ACallerWithoutTheReadGrant_IsRefused()
    {
        // Arrange
        var harness = new NameHarness(MailFathomPermission.ProfileWrite);

        // Act & Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => harness.Names.ReadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ChangeAsync_APersonCorrectingTheirName_RecordsItAndAnswersWhatIsNowStored()
    {
        // Arrange
        var harness = new NameHarness(MailFathomPermission.ProfileWrite);
        harness.Recording("Ada Lovelace");

        // Act
        var change = await harness.Names.ChangeAsync("Ada King", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Ada King", change.Recorded);
        await harness.Provisioning.Received(1)
            .RelabelAsync(SyntheticUser.Deployment, "Ada King", Arg.Any<CancellationToken>());
    }

    /// <summary>The answer carries what was stored rather than what was sent, so a client redrawing it shows the name this deployment holds.</summary>
    [Fact]
    public async Task ChangeAsync_ANameCarryingSurroundingSpace_RecordsAndAnswersTheTrimmedName()
    {
        // Arrange
        var harness = new NameHarness(MailFathomPermission.ProfileWrite);
        harness.Recording("Ada Lovelace");

        // Act
        var change = await harness.Names.ChangeAsync("  Ada King \t", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Ada King", change.Recorded);
        await harness.Provisioning.Received(1)
            .RelabelAsync(SyntheticUser.Deployment, "Ada King", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChangeAsync_ABodyStatingNoName_IsRefusedWithoutWriting()
    {
        // Arrange
        var harness = new NameHarness(MailFathomPermission.ProfileWrite);
        harness.Recording("Ada Lovelace");

        // Act
        var change = await harness.Names.ChangeAsync("   ", TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(change.RefusalMessage);
        Assert.Null(change.Recorded);
        await harness.Provisioning.DidNotReceive()
            .RelabelAsync(Arg.Any<UserId>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>The column stores 128 characters, so a longer name is refused naming the bound rather than truncated into the row.</summary>
    [Fact]
    public async Task ChangeAsync_ANamePastWhatTheColumnStores_IsRefusedNamingTheBound()
    {
        // Arrange
        var harness = new NameHarness(MailFathomPermission.ProfileWrite);
        harness.Recording("Ada Lovelace");

        // Act
        var change = await harness.Names.ChangeAsync(
            new string('a', UserRecord.MaximumDisplayNameLength + 1),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            UserRecord.MaximumDisplayNameLength.ToString(System.Globalization.CultureInfo.InvariantCulture),
            change.RefusalMessage!,
            StringComparison.Ordinal);
        await harness.Provisioning.DidNotReceive()
            .RelabelAsync(Arg.Any<UserId>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>The name is unique across the deployment, and the statement itself is what refuses one already taken.</summary>
    [Fact]
    public async Task ChangeAsync_ANameSomebodyElseCarries_IsRefusedWithoutNamingWho()
    {
        // Arrange
        var harness = new NameHarness(MailFathomPermission.ProfileWrite);
        harness.Recording("Ada Lovelace");
        harness.Provisioning
            .RelabelAsync(Arg.Any<UserId>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);

        // Act
        var change = await harness.Names.ChangeAsync("morgan", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(change.UserHeld);
        Assert.Null(change.Recorded);
        Assert.DoesNotContain("morgan", change.RefusalMessage!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChangeAsync_APersonThisDeploymentDoesNotHold_AnswersThatThereIsNoRecord()
    {
        // Arrange
        var harness = new NameHarness(MailFathomPermission.ProfileWrite);

        // Act
        var change = await harness.Names.ChangeAsync("Ada King", TestContext.Current.CancellationToken);

        // Assert
        Assert.False(change.UserHeld);
        Assert.Null(change.RefusalMessage);
        await harness.Provisioning.DidNotReceive()
            .RelabelAsync(Arg.Any<UserId>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>What somebody is called is not mail, so the grant that reads mail is not shown it.</summary>
    [Fact]
    public async Task ReadAsync_ACallerGrantedOnlyReadingMail_IsRefusedNamingTheProfileGrant()
    {
        // Arrange
        var harness = new NameHarness(MailFathomPermission.MailRead);

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => harness.Names.ReadAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.ProfileRead, refusal.RequiredPermission);
    }

    /// <summary>
    /// The grant that decides which mailboxes this deployment reads says nothing about what a person is called, so
    /// holding it corrects no name — and somebody whose mailboxes an administrator maintains corrects theirs without it.
    /// </summary>
    [Fact]
    public async Task ChangeAsync_ACallerHoldingOnlyTheMailAccountsGrant_IsRefusedNamingTheProfileGrant()
    {
        // Arrange
        var harness = new NameHarness(MailFathomPermission.MailRead, MailFathomPermission.MailAccountsWrite);

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => harness.Names.ChangeAsync("Ada King", TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.ProfileWrite, refusal.RequiredPermission);
    }

    /// <summary>Seeing the name is not correcting it, so a caller granted only the reading half of their profile writes nothing.</summary>
    [Fact]
    public async Task ChangeAsync_ACallerHoldingOnlyTheReadGrant_IsRefused()
    {
        // Arrange
        var harness = new NameHarness(MailFathomPermission.ProfileRead);

        // Act & Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => harness.Names.ChangeAsync("Ada King", TestContext.Current.CancellationToken));
    }

    /// <summary>The flag is the grant and the policy together, so a client never offers a field the deployment would refuse.</summary>
    [Theory]
    [InlineData(WithholdingTheName, false)]
    [InlineData("{}", true)]
    [InlineData(NotAPolicy, false)]
    public async Task ReadAsync_ACallerGrantedTheProfileWriteUnderAPolicy_ReportsTheNameAsChangeableOnlyWhereThePolicyLeavesItToThem(
        string organizationPolicy,
        bool changeable)
    {
        // Arrange
        var harness = new NameHarness(MailFathomPermission.ProfileRead, MailFathomPermission.ProfileWrite);
        harness.Recording("Ada Lovelace");
        harness.GovernedBy(StatedPolicies.Governing(organization: organizationPolicy));

        // Act
        var read = await harness.Names.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(changeable, read!.Value.Changeable);
    }

    [Fact]
    public async Task ChangeAsync_ANameAPolicyWithholdsFromThePerson_IsRefusedNamingWhoHoldsItWithoutWriting()
    {
        // Arrange
        var harness = new NameHarness(MailFathomPermission.ProfileRead, MailFathomPermission.ProfileWrite);
        harness.Recording("Ada Lovelace");
        harness.GovernedBy(StatedPolicies.Governing(organization: WithholdingTheName));

        // Act
        var change = await harness.Names.ChangeAsync("Ada King", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(change.UserHeld);
        Assert.StartsWith("DisplayName is held by the organization", change.RefusalMessage, StringComparison.Ordinal);
        await harness.Provisioning.DidNotReceive()
            .RelabelAsync(Arg.Any<UserId>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A request stating the name already held changes nothing, so a policy holds nothing against it.</summary>
    [Fact]
    public async Task ChangeAsync_AWithheldNameCarriedThroughAsItStands_PassesWithoutWriting()
    {
        // Arrange
        var harness = new NameHarness(MailFathomPermission.ProfileRead, MailFathomPermission.ProfileWrite);
        harness.Recording("Ada Lovelace");
        harness.GovernedBy(StatedPolicies.Governing(organization: WithholdingTheName));

        // Act
        var change = await harness.Names.ChangeAsync(" Ada Lovelace ", TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(change.RefusalMessage);
        Assert.Equal("Ada Lovelace", change.Recorded);
        Assert.True(change.IsHeld);
        await harness.Provisioning.DidNotReceive()
            .RelabelAsync(Arg.Any<UserId>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>The service over a substituted envelope.</summary>
    private sealed class NameHarness
    {
        internal NameHarness(params MailFathomPermission[] granted)
        {
            this.Directory = Substitute.For<IUserDirectory>();
            this.Directory
                .ReadUserAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>())
                .Returns((UserRecord?)null);

            this.Provisioning = Substitute.For<IUserProvisioning>();
            this.Provisioning
                .RelabelAsync(Arg.Any<UserId>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(true);

            this.Names = new OwnDisplayName(
                AccessAuthorizations.ForUserGranted(SyntheticUser.Deployment, granted),
                this.Directory,
                this.Provisioning,
                this.Documents);
        }

        /// <summary>Gets the reader the person's record and the policies governing it are read from, which holds no record until a test says so.</summary>
        internal IUserSettingsDocumentReader Documents { get; } = Substitute.For<IUserSettingsDocumentReader>();

        internal OwnDisplayName Names { get; }

        internal IUserDirectory Directory { get; }

        internal IUserProvisioning Provisioning { get; }

        /// <summary>States the name the envelope of the person these tests act for carries.</summary>
        internal void Recording(string displayName) =>
            this.Directory
                .ReadUserAsync(SyntheticUser.Deployment, Arg.Any<CancellationToken>())
                .Returns(new UserRecord(SyntheticUser.Deployment, displayName));

        /// <summary>States the settings policies the person's record is read under.</summary>
        internal void GovernedBy(GoverningSettingsPolicies policies) =>
            this.Documents
                .ReadAsync(SyntheticUser.Deployment, Arg.Any<CancellationToken>())
                .Returns(new UserSettingsDocument(SyntheticUser.Deployment, "ada", "{}", Version: 1) { Policies = policies });
    }
}
