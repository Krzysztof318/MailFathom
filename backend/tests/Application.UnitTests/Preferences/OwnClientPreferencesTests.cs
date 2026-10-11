// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Preferences;
using MailFathom.Domain.Access;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Preferences;

/// <summary>
/// Covers the use case a person reads and writes their own client preferences through. What it has to hold is that the
/// user acted on is the one the credential authenticated rather than one a caller could name, that the grant required
/// is the one a signed-in person already holds rather than the grant over their mail configuration, and that somebody
/// who has set nothing is answered with the unset preferences rather than with a refusal.
/// </summary>
public sealed class OwnClientPreferencesTests
{
    private static readonly ClientPreferences Chosen =
        new(false, ClientThemeChoice.Dark, true, false, true, ClientMessageView.Cleaned, false, 12);

    [Fact]
    public async Task ReadAsync_APersonWhoHasSetSomething_AnswersWhatTheySet()
    {
        // Arrange
        var store = Substitute.For<IClientPreferencesStore>();
        store.ReadAsync(SyntheticUser.Deployment, Arg.Any<CancellationToken>()).Returns(Chosen);

        var preferences = ReachedBy(store, MailFathomPermission.ProfileRead, MailFathomPermission.ProfileWrite);

        // Act
        var read = await preferences.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(Chosen, read);
    }

    /// <summary>A first run draws a screen, so an empty store is the unset answers rather than an error a client renders.</summary>
    [Fact]
    public async Task ReadAsync_APersonWhoHasSetNothing_AnswersTheUnsetPreferences()
    {
        // Arrange
        var store = Substitute.For<IClientPreferencesStore>();
        store.ReadAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>()).Returns((ClientPreferences?)null);

        var preferences = ReachedBy(store, MailFathomPermission.ProfileRead, MailFathomPermission.ProfileWrite);

        // Act
        var read = await preferences.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ClientPreferences.Unset, read);
    }

    /// <summary>The user is resolved from the principal, so a deployment serving two people reads the caller's own row and never the other.</summary>
    [Fact]
    public async Task ReadAsync_ADeploymentServingSeveralPeople_ReadsTheRowOfTheUserTheCredentialAuthenticated()
    {
        // Arrange
        var store = Substitute.For<IClientPreferencesStore>();
        var preferences = ReachedBy(store, SyntheticUser.Another, MailFathomPermission.ProfileRead, MailFathomPermission.ProfileWrite);

        // Act
        await preferences.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        await store.Received(1).ReadAsync(SyntheticUser.Another, Arg.Any<CancellationToken>());
        await store.DidNotReceive().ReadAsync(SyntheticUser.Deployment, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReadAsync_ACallerGrantedNothing_IsRefused()
    {
        // Arrange
        var preferences = ReachedBy(Substitute.For<IClientPreferencesStore>());

        // Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => preferences.ReadAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>An administrator acts for nobody's mail, so there is no row of theirs to read here.</summary>
    [Fact]
    public async Task ReadAsync_ACallerActingForNoUser_IsRefused()
    {
        // Arrange
        var preferences = new OwnClientPreferences(
            AccessAuthorizations.ForAdministratorGranted(MailFathomPermission.ProfileRead),
            Substitute.For<IClientPreferencesStore>());

        // Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => preferences.ReadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SaveAsync_APersonStatingTheirPreferences_WritesThemAgainstTheirOwnRow()
    {
        // Arrange
        var store = Substitute.For<IClientPreferencesStore>();
        store.SaveAsync(Arg.Any<UserId>(), Arg.Any<ClientPreferences>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var preferences = ReachedBy(store, SyntheticUser.Another, MailFathomPermission.ProfileRead, MailFathomPermission.ProfileWrite);

        // Act
        var written = await preferences.SaveAsync(Chosen, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(written);
        await store.Received(1).SaveAsync(SyntheticUser.Another, Chosen, Arg.Any<CancellationToken>());
    }

    /// <summary>The row behind an authenticated caller can be gone, which is a user erased under a credential that has not yet been withdrawn.</summary>
    [Fact]
    public async Task SaveAsync_ACallerWhoseRowHasGone_ReportsThatThereWasNobodyToWriteFor()
    {
        // Arrange
        var store = Substitute.For<IClientPreferencesStore>();
        store.SaveAsync(Arg.Any<UserId>(), Arg.Any<ClientPreferences>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var preferences = ReachedBy(store, MailFathomPermission.ProfileRead, MailFathomPermission.ProfileWrite);

        // Act
        var written = await preferences.SaveAsync(Chosen, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(written);
    }

    /// <summary>Reading what somebody set is not setting it, so a caller granted only the reading half writes nothing.</summary>
    [Fact]
    public async Task SaveAsync_ACallerGrantedOnlyReadingTheirProfile_IsRefused()
    {
        // Arrange
        var store = Substitute.For<IClientPreferencesStore>();
        var preferences = ReachedBy(store, MailFathomPermission.ProfileRead);

        // Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => preferences.SaveAsync(Chosen, TestContext.Current.CancellationToken));

        await store.DidNotReceive()
            .SaveAsync(Arg.Any<UserId>(), Arg.Any<ClientPreferences>(), Arg.Any<CancellationToken>());
    }

    /// <summary>The answer a write carries forward is a step of the write, so it is read under the writing grant and under no other.</summary>
    [Fact]
    public async Task ReadTelemetryAnswerToCarryAsync_ACallerGrantedOnlyReadingTheirProfile_IsRefused()
    {
        // Arrange
        var preferences = ReachedBy(Substitute.For<IClientPreferencesStore>(), MailFathomPermission.ProfileRead);

        // Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => preferences.ReadTelemetryAnswerToCarryAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>Somebody who has set nothing has stored no refusal, so the answer carried forward is the unset one.</summary>
    [Fact]
    public async Task ReadTelemetryAnswerToCarryAsync_ACallerWhoHasSetNothing_AnswersTheUnsetAnswer()
    {
        // Arrange
        var preferences = ReachedBy(Substitute.For<IClientPreferencesStore>(), MailFathomPermission.ProfileWrite);

        // Act
        var carried = await preferences.ReadTelemetryAnswerToCarryAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ClientPreferences.Unset.TelemetryEnabled, carried);
    }

    /// <summary>What somebody set about their own client is not mail, so the grant that reads mail reads none of it.</summary>
    [Fact]
    public async Task ReadAsync_ACallerGrantedOnlyReadingMail_IsRefused()
    {
        // Arrange
        var preferences = ReachedBy(Substitute.For<IClientPreferencesStore>(), MailFathomPermission.MailRead);

        // Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => preferences.ReadAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The write is admitted under a grant over the person's own profile rather than under the one that decides which
    /// mailboxes this deployment reads: somebody whose accounts an administrator maintains still turns their own
    /// telemetry off, and holding the second says nothing about the first.
    /// </summary>
    [Fact]
    public async Task SaveAsync_ACallerHoldingOnlyTheMailAccountsGrant_IsRefused()
    {
        // Arrange
        var preferences = ReachedBy(
            Substitute.For<IClientPreferencesStore>(),
            MailFathomPermission.MailAccountsWrite);

        // Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => preferences.SaveAsync(Chosen, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SaveAsync_NoPreferencesAtAll_IsRefusedBeforeAnythingIsWritten()
    {
        // Arrange
        var store = Substitute.For<IClientPreferencesStore>();
        var preferences = ReachedBy(store, MailFathomPermission.ProfileRead, MailFathomPermission.ProfileWrite);

        // Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => preferences.SaveAsync(null!, TestContext.Current.CancellationToken));

        await store.DidNotReceive()
            .SaveAsync(Arg.Any<UserId>(), Arg.Any<ClientPreferences>(), Arg.Any<CancellationToken>());
    }

    private static OwnClientPreferences ReachedBy(
        IClientPreferencesStore store,
        params MailFathomPermission[] granted) =>
        ReachedBy(store, SyntheticUser.Deployment, granted);

    private static OwnClientPreferences ReachedBy(
        IClientPreferencesStore store,
        UserId user,
        params MailFathomPermission[] granted) =>
        new(AccessAuthorizations.ForUserGranted(user, granted), store);
}
