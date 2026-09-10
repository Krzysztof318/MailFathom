// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Host.Configuration.UserSettings.Administration;
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
    [Fact]
    public async Task ReadAsync_APersonThisDeploymentHolds_HandsThemTheNameItRecordsThemUnder()
    {
        // Arrange
        var harness = new NameHarness(MailFathomPermission.MailRead);
        harness.Recording("Ada Lovelace");

        // Act
        var read = await harness.Names.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Ada Lovelace", read!.Value.DisplayName);
    }

    /// <summary>The whole reason the flag travels with the name: a client draws the name as text rather than offering a field the deployment would refuse.</summary>
    [Fact]
    public async Task ReadAsync_ACallerGrantedTheRecordsWrite_ReportsTheNameAsChangeable()
    {
        // Arrange
        var harness = new NameHarness(MailFathomPermission.MailRead, MailFathomPermission.MailAccountsWrite);
        harness.Recording("Ada Lovelace");

        // Act
        var read = await harness.Names.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(read!.Value.Changeable);
    }

    [Fact]
    public async Task ReadAsync_ACallerWithoutTheRecordsWrite_ReportsTheNameAsUnchangeable()
    {
        // Arrange
        var harness = new NameHarness(MailFathomPermission.MailRead);
        harness.Recording("Ada Lovelace");

        // Act
        var read = await harness.Names.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(read!.Value.Changeable);
    }

    /// <summary>Somebody whose mailboxes an administrator maintains holds the grant and still cannot change the name, because a start would write it back.</summary>
    [Fact]
    public async Task ReadAsync_APersonAConfigurationSourceDeclares_ReportsTheNameAsUnchangeable()
    {
        // Arrange
        var harness = new NameHarness(MailFathomPermission.MailRead, MailFathomPermission.MailAccountsWrite);
        harness.Recording("Ada Lovelace");
        harness.Declared();

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
        var harness = new NameHarness(MailFathomPermission.MailRead);

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
        var harness = new NameHarness(MailFathomPermission.MailRead);
        harness.Recording("Ada Lovelace");

        // Act
        await harness.Names.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        await harness.Directory.Received(1)
            .ReadUserAsync(SyntheticMailUser.Deployment, Arg.Any<CancellationToken>());
        await harness.Directory.DidNotReceive().ReadUsersAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReadAsync_ACallerWithoutTheReadGrant_IsRefused()
    {
        // Arrange
        var harness = new NameHarness(MailFathomPermission.MailAccountsWrite);

        // Act & Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => harness.Names.ReadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ChangeAsync_APersonCorrectingTheirName_RecordsItAndAnswersWhatIsNowStored()
    {
        // Arrange
        var harness = new NameHarness(MailFathomPermission.MailAccountsWrite);
        harness.Recording("Ada Lovelace");

        // Act
        var change = await harness.Names.ChangeAsync("Ada King", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Ada King", change.Recorded);
        await harness.Provisioning.Received(1)
            .RelabelAsync(SyntheticMailUser.Deployment, "Ada King", Arg.Any<CancellationToken>());
    }

    /// <summary>The answer carries what was stored rather than what was sent, so a client redrawing it shows the name this deployment holds.</summary>
    [Fact]
    public async Task ChangeAsync_ANameCarryingSurroundingSpace_RecordsAndAnswersTheTrimmedName()
    {
        // Arrange
        var harness = new NameHarness(MailFathomPermission.MailAccountsWrite);
        harness.Recording("Ada Lovelace");

        // Act
        var change = await harness.Names.ChangeAsync("  Ada King \t", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Ada King", change.Recorded);
        await harness.Provisioning.Received(1)
            .RelabelAsync(SyntheticMailUser.Deployment, "Ada King", Arg.Any<CancellationToken>());
    }

    /// <summary>A person the deployment's own configuration supplies is administered rather than self-served, so their name is not theirs to set.</summary>
    [Fact]
    public async Task ChangeAsync_APersonAConfigurationSourceDeclares_IsRefusedNamingWhoToAsk()
    {
        // Arrange
        var harness = new NameHarness(MailFathomPermission.MailAccountsWrite);
        harness.Recording("declared");
        harness.Declared();

        // Act
        var change = await harness.Names.ChangeAsync("Ada King", TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("the name on it is theirs to set", change.RefusalMessage!, StringComparison.Ordinal);
        await harness.Provisioning.DidNotReceive()
            .RelabelAsync(Arg.Any<MailUserId>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChangeAsync_ABodyStatingNoName_IsRefusedWithoutWriting()
    {
        // Arrange
        var harness = new NameHarness(MailFathomPermission.MailAccountsWrite);
        harness.Recording("Ada Lovelace");

        // Act
        var change = await harness.Names.ChangeAsync("   ", TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(change.RefusalMessage);
        Assert.Null(change.Recorded);
        await harness.Provisioning.DidNotReceive()
            .RelabelAsync(Arg.Any<MailUserId>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>The column stores 128 characters, so a longer name is refused naming the bound rather than truncated into the row.</summary>
    [Fact]
    public async Task ChangeAsync_ANamePastWhatTheColumnStores_IsRefusedNamingTheBound()
    {
        // Arrange
        var harness = new NameHarness(MailFathomPermission.MailAccountsWrite);
        harness.Recording("Ada Lovelace");

        // Act
        var change = await harness.Names.ChangeAsync(
            new string('a', MailUserRecord.MaximumDisplayNameLength + 1),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            MailUserRecord.MaximumDisplayNameLength.ToString(System.Globalization.CultureInfo.InvariantCulture),
            change.RefusalMessage!,
            StringComparison.Ordinal);
        await harness.Provisioning.DidNotReceive()
            .RelabelAsync(Arg.Any<MailUserId>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>The name is unique across the deployment, and the statement itself is what refuses one already taken.</summary>
    [Fact]
    public async Task ChangeAsync_ANameSomebodyElseCarries_IsRefusedWithoutNamingWho()
    {
        // Arrange
        var harness = new NameHarness(MailFathomPermission.MailAccountsWrite);
        harness.Recording("Ada Lovelace");
        harness.Provisioning
            .RelabelAsync(Arg.Any<MailUserId>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
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
        var harness = new NameHarness(MailFathomPermission.MailAccountsWrite);

        // Act
        var change = await harness.Names.ChangeAsync("Ada King", TestContext.Current.CancellationToken);

        // Assert
        Assert.False(change.UserHeld);
        Assert.Null(change.RefusalMessage);
        await harness.Provisioning.DidNotReceive()
            .RelabelAsync(Arg.Any<MailUserId>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>The record's own grant rather than the read every signed-in person holds, because this writes the row an administrator maintains.</summary>
    [Fact]
    public async Task ChangeAsync_ACallerHoldingOnlyTheReadGrant_IsRefused()
    {
        // Arrange
        var harness = new NameHarness(MailFathomPermission.MailRead);

        // Act & Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => harness.Names.ChangeAsync("Ada King", TestContext.Current.CancellationToken));
    }

    /// <summary>The service over a substituted envelope, with the roster a configuration refusal is read from.</summary>
    private sealed class NameHarness
    {
        internal NameHarness(params MailFathomPermission[] granted)
        {
            this.Directory = Substitute.For<IMailUserDirectory>();
            this.Directory
                .ReadUserAsync(Arg.Any<MailUserId>(), Arg.Any<CancellationToken>())
                .Returns((MailUserRecord?)null);

            this.Provisioning = Substitute.For<IMailUserProvisioning>();
            this.Provisioning
                .RelabelAsync(Arg.Any<MailUserId>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(true);

            // The person these tests act for is served from their own document, which is the ordinary case, until a
            // test states otherwise.
            this.Serving(MailUserAccountSource.UserDocument);

            this.Names = new OwnDisplayName(
                AccessAuthorizations.ForUserGranted(SyntheticMailUser.Deployment, granted),
                this.Directory,
                this.Provisioning,
                this.ServedUsers);
        }

        internal OwnDisplayName Names { get; }

        internal IMailUserDirectory Directory { get; }

        internal IMailUserProvisioning Provisioning { get; }

        private ServedMailUsers ServedUsers { get; } = new();

        /// <summary>States the name the envelope of the person these tests act for carries.</summary>
        internal void Recording(string displayName) =>
            this.Directory
                .ReadUserAsync(SyntheticMailUser.Deployment, Arg.Any<CancellationToken>())
                .Returns(new MailUserRecord(SyntheticMailUser.Deployment, displayName, DocumentWrittenAtRuntime: true));

        /// <summary>States that a configuration source still supplies that person's mail accounts.</summary>
        internal void Declared() => this.Serving(MailUserAccountSource.DeploymentSection);

        private void Serving(MailUserAccountSource source) =>
            this.ServedUsers.Resolved([new(SyntheticMailUser.Deployment, "recorded", source, [])]);
    }
}
