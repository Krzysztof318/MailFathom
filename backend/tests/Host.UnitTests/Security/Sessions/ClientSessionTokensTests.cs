// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access.Credentials;
using MailFathom.Domain.Access;
using MailFathom.Host.Security.Sessions;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Host.UnitTests.Security.Sessions;

/// <summary>Covers what a session token admits, what it refuses, how it renews, and what ends it before it expires.</summary>
public sealed class ClientSessionTokensTests
{
    private static readonly DateTimeOffset Instant = new(2026, 9, 8, 9, 0, 0, TimeSpan.Zero);

    private static readonly Guid CredentialId = new("2a5f8f2e-6c1d-4c0a-9c2f-2f3b6a1d4e77");

    /// <summary>A second credential the same user holds, which is what an erasure has to reach and a disabled credential must not.</summary>
    private static readonly Guid SecondCredentialId = new("5d1c7b40-9e33-4a6b-8f21-7c9a0e5b3d12");

    /// <summary>A token names the user and the grant the exchange resolved, which is what lets a request be served without resolving either again.</summary>
    [Fact]
    public void Verify_AFreshlyMintedToken_AdmitsWhatTheExchangeResolved()
    {
        // Arrange
        var sessions = new ClientSessionTokens(new FakeTimeProvider(Instant));
        var admitted = Admitted(MailFathomPermission.MailRead, MailFathomPermission.MailAsk);

        // Act
        var verified = sessions.Verify(sessions.Mint(admitted)?.Value);

        // Assert
        Assert.Equal(admitted, verified);
    }

    /// <summary>Two people signed in at once each hold their own, so one request can never be served as the other person.</summary>
    [Fact]
    public void Verify_TokensMintedForTwoUsers_AdmitsEachAsThemselves()
    {
        // Arrange
        var sessions = new ClientSessionTokens(new FakeTimeProvider(Instant));
        var mine = sessions.Mint(Admitted(SyntheticMailUser.Deployment));
        var theirs = sessions.Mint(Admitted(SyntheticMailUser.Another));

        // Act, Assert
        Assert.Equal(SyntheticMailUser.Deployment, sessions.Verify(mine?.Value)?.User);
        Assert.Equal(SyntheticMailUser.Another, sessions.Verify(theirs?.Value)?.User);
    }

    /// <summary>A session outlives the request that presented it, which is the whole difference between this and a one-connection ticket.</summary>
    [Fact]
    public void Verify_TheSameTokenSeveralTimes_AdmitsEveryTime()
    {
        // Arrange
        var sessions = new ClientSessionTokens(new FakeTimeProvider(Instant));
        var minted = sessions.Mint(Admitted());

        // Act, Assert
        Assert.NotNull(sessions.Verify(minted?.Value));
        Assert.NotNull(sessions.Verify(minted?.Value));
        Assert.NotNull(sessions.Verify(minted?.Value));
    }

    /// <summary>Presenting the identifier with somebody else's proof authenticates nobody, which is what makes only the secret half secret.</summary>
    [Fact]
    public void Verify_ATokenCarryingAnotherSessionsProof_IsRefused()
    {
        // Arrange
        var sessions = new ClientSessionTokens(new FakeTimeProvider(Instant));
        var mine = sessions.Mint(Admitted())!;
        var theirs = sessions.Mint(Admitted())!;

        // Act
        var verified = sessions.Verify(NameOf(mine.Value) + ProofOf(theirs.Value));

        // Assert
        Assert.Null(verified);
    }

    /// <summary>Anything that is not a token this deployment mints is refused before it is looked up.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("mfs_")]
    [InlineData("mfs_no-separator-at-all")]
    [InlineData("mfs_.")]
    [InlineData("mfs_name.")]
    [InlineData("mfk_a-key-rather-than-a-session.proof")]
    [InlineData("name.proof")]
    public void Verify_AValueThatIsNotAMintedToken_IsRefused(string? presented)
    {
        // Arrange
        var sessions = new ClientSessionTokens(new FakeTimeProvider(Instant));

        // Act, Assert
        Assert.Null(sessions.Verify(presented));
    }

    /// <summary>A value longer than anything this type mints is refused unread rather than walked.</summary>
    [Fact]
    public void Verify_AValuePastTheBound_IsRefused()
    {
        // Arrange
        var sessions = new ClientSessionTokens(new FakeTimeProvider(Instant));

        // Act, Assert
        Assert.Null(sessions.Verify(ClientSessionTokens.TokenPrefix + new string('a', 300) + ".proof"));
    }

    /// <summary>A session ends by itself, so a token left on a machine nobody came back to stops authenticating.</summary>
    [Fact]
    public void Verify_ATokenPresentedAfterItsLifetime_IsRefused()
    {
        // Arrange
        var clock = new FakeTimeProvider(Instant);
        var sessions = new ClientSessionTokens(clock);
        var minted = sessions.Mint(Admitted());

        // Act
        clock.Advance(ClientSessionTokens.Lifetime + TimeSpan.FromSeconds(1));

        // Assert
        Assert.Null(sessions.Verify(minted?.Value));
    }

    /// <summary>The last instant of the lifetime still authenticates, so the bound is a lifetime rather than one shorter than it says.</summary>
    [Fact]
    public void Verify_ATokenPresentedAtTheLastInstantOfItsLifetime_IsAdmitted()
    {
        // Arrange
        var clock = new FakeTimeProvider(Instant);
        var sessions = new ClientSessionTokens(clock);
        var minted = sessions.Mint(Admitted());

        // Act
        clock.Advance(ClientSessionTokens.Lifetime);

        // Assert
        Assert.NotNull(sessions.Verify(minted?.Value));
    }

    /// <summary>Renewing gives a client a fresh lifetime without anybody typing a password again.</summary>
    [Fact]
    public void Renew_ALiveSession_AnswersATokenAdmittingTheSameCallerForAFreshLifetime()
    {
        // Arrange
        var clock = new FakeTimeProvider(Instant);
        var sessions = new ClientSessionTokens(clock);
        var admitted = Admitted();
        var minted = sessions.Mint(admitted)!;

        // Act
        clock.Advance(TimeSpan.FromHours(6));
        var renewed = sessions.Renew(minted.Value);

        // Assert
        Assert.NotNull(renewed);
        Assert.Equal(admitted, sessions.Verify(renewed.Value));
        Assert.Equal(clock.GetUtcNow() + ClientSessionTokens.Lifetime, renewed.ExpiresAt);
    }

    /// <summary>The renewed token replaces the presented one, so one sign-in leaves one live credential rather than a trail of them.</summary>
    [Fact]
    public void Renew_ALiveSession_LeavesThePresentedTokenRefused()
    {
        // Arrange
        var sessions = new ClientSessionTokens(new FakeTimeProvider(Instant));
        var minted = sessions.Mint(Admitted())!;

        // Act
        sessions.Renew(minted.Value);

        // Assert
        Assert.Null(sessions.Verify(minted.Value));
    }

    /// <summary>A session that has already ended renews nothing, so an expired token is a sign-in rather than a renewal.</summary>
    [Fact]
    public void Renew_ATokenThatNoLongerAuthenticates_AnswersNothing()
    {
        // Arrange
        var clock = new FakeTimeProvider(Instant);
        var sessions = new ClientSessionTokens(clock);
        var minted = sessions.Mint(Admitted())!;

        // Act
        clock.Advance(ClientSessionTokens.Lifetime + TimeSpan.FromSeconds(1));

        // Assert
        Assert.Null(sessions.Renew(minted.Value));
    }

    /// <summary>Signing out ends the session at once rather than leaving the token good until it expires.</summary>
    [Fact]
    public void Revoke_ALiveSession_LeavesTheTokenRefusedOnTheNextRequest()
    {
        // Arrange
        var sessions = new ClientSessionTokens(new FakeTimeProvider(Instant));
        var minted = sessions.Mint(Admitted())!;

        // Act
        var revoked = sessions.Revoke(minted.Value);

        // Assert
        Assert.True(revoked);
        Assert.Null(sessions.Verify(minted.Value));
    }

    /// <summary>A caller writing an identifier it guessed signs nobody out, the identifier being the half that is not a secret.</summary>
    [Fact]
    public void Revoke_TheIdentifierOfALiveSessionWithoutItsProof_EndsNothing()
    {
        // Arrange
        var sessions = new ClientSessionTokens(new FakeTimeProvider(Instant));
        var mine = sessions.Mint(Admitted())!;
        var theirs = sessions.Mint(Admitted())!;

        // Act
        var revoked = sessions.Revoke(NameOf(mine.Value) + ProofOf(theirs.Value));

        // Assert
        Assert.False(revoked);
        Assert.NotNull(sessions.Verify(mine.Value));
    }

    /// <summary>Disabling or removing a credential ends what it is signed in as, which is what an operator revoking access means by it.</summary>
    [Fact]
    public void RevokeEverythingMintedBy_ACredentialWithSessionsOpen_EndsThoseAndLeavesTheRest()
    {
        // Arrange
        var sessions = new ClientSessionTokens(new FakeTimeProvider(Instant));
        var revokedCredential = Admitted();
        var otherCredential = new AdmittedUserCredential(
            new Guid("8d0b8f11-7f4c-4a3e-9b6a-90a6c4a5b1c2"),
            SyntheticMailUser.Another,
            [MailFathomPermission.MailRead]);

        var first = sessions.Mint(revokedCredential)!;
        var second = sessions.Mint(revokedCredential)!;
        var untouched = sessions.Mint(otherCredential)!;

        // Act
        var ended = sessions.RevokeEverythingMintedBy(CredentialId);

        // Assert
        Assert.Equal(2, ended);
        Assert.Null(sessions.Verify(first.Value));
        Assert.Null(sessions.Verify(second.Value));
        Assert.NotNull(sessions.Verify(untouched.Value));
    }

    /// <summary>The bound refuses rather than evicting, so a process at its ceiling signs nobody else out to admit one more.</summary>
    [Fact]
    public void Mint_AtTheCeilingWithEverySessionLive_RefusesAndLeavesTheLiveOnesAlone()
    {
        // Arrange
        var sessions = new ClientSessionTokens(new FakeTimeProvider(Instant));
        var first = sessions.Mint(Admitted())!;

        for (var minted = 1; minted < ClientSessionTokens.MostLiveSessions; minted += 1)
        {
            Assert.NotNull(sessions.Mint(Admitted()));
        }

        // Act
        var refused = sessions.Mint(Admitted());

        // Assert
        Assert.Null(refused);
        Assert.NotNull(sessions.Verify(first.Value));
    }

    /// <summary>An expired session is not a live one, so a process whose ceiling is reached by history admits the next sign-in.</summary>
    [Fact]
    public void Mint_AtTheCeilingWhereEverySessionHasExpired_AdmitsTheNextSignIn()
    {
        // Arrange
        var clock = new FakeTimeProvider(Instant);
        var sessions = new ClientSessionTokens(clock);

        for (var minted = 0; minted < ClientSessionTokens.MostLiveSessions; minted += 1)
        {
            Assert.NotNull(sessions.Mint(Admitted()));
        }

        // Act
        clock.Advance(ClientSessionTokens.Lifetime + TimeSpan.FromSeconds(1));

        // Assert
        Assert.NotNull(sessions.Mint(Admitted()));
    }

    /// <summary>A replacement never meets the bound, so a full process renews the clients it already signed in rather than expiring them.</summary>
    /// <remarks>
    /// The guarantee the store publishes and the one nothing else would report: a renewal refused for capacity would
    /// end every live session at its own expiry for as long as the process stayed full, with no way back in until the
    /// store drained. It is computed before the presented session is removed, which is exactly the step that could
    /// stop being true without anything failing.
    /// </remarks>
    [Fact]
    public void Renew_AtTheCeilingWithEverySessionLive_StillAnswersAFreshToken()
    {
        // Arrange
        var sessions = new ClientSessionTokens(new FakeTimeProvider(Instant));
        var held = sessions.Mint(Admitted())!;

        for (var minted = 1; minted < ClientSessionTokens.MostLiveSessions; minted += 1)
        {
            Assert.NotNull(sessions.Mint(Admitted()));
        }

        // Act
        var renewed = sessions.Renew(held.Value);

        // Assert
        Assert.NotNull(renewed);
        Assert.NotNull(sessions.Verify(renewed.Value));
        Assert.Null(sessions.Verify(held.Value));
    }

    /// <summary>A credential an operator has just ended mints nothing, so an exchange in flight across the act does not survive it.</summary>
    /// <remarks>
    /// The window is the derivation: a request authenticates against a row that is still enabled, spends half a second
    /// in PBKDF2, and would write its session after the sweep meant to have ended it. Without the barrier the operator
    /// is told the sessions are gone while one is live and renewing from what this store holds.
    /// </remarks>
    [Fact]
    public void Mint_ForACredentialWhoseSessionsWereJustEnded_RefusesRatherThanSurvivingTheAct()
    {
        // Arrange
        var sessions = new ClientSessionTokens(new FakeTimeProvider(Instant));
        sessions.Mint(Admitted());

        // Act
        sessions.RevokeEverythingMintedBy(CredentialId);

        // Assert
        Assert.Null(sessions.Mint(Admitted()));
        Assert.True(sessions.WasEndedRecently(Admitted()));
    }

    /// <summary>The barrier is a window rather than a state, so a credential an operator enabled again signs in.</summary>
    [Fact]
    public void Mint_ForACredentialWhoseBarrierHasPassed_AdmitsTheSignInAgain()
    {
        // Arrange
        var clock = new FakeTimeProvider(Instant);
        var sessions = new ClientSessionTokens(clock);
        sessions.RevokeEverythingMintedBy(CredentialId);

        // Act
        clock.Advance(ClientSessionTokens.MintBarrier + TimeSpan.FromSeconds(1));

        // Assert
        Assert.NotNull(sessions.Mint(Admitted()));
        Assert.False(sessions.WasEndedRecently(Admitted()));
    }

    /// <summary>An erasure bars the user rather than the credential, because the rows it removes are the ones it never names.</summary>
    [Fact]
    public void Mint_ForAUserWhoWasJustErased_RefusesEveryCredentialTheyHeld()
    {
        // Arrange
        var sessions = new ClientSessionTokens(new FakeTimeProvider(Instant));
        var second = new AdmittedUserCredential(
            SecondCredentialId,
            SyntheticMailUser.Deployment,
            [MailFathomPermission.MailRead]);

        // Act
        sessions.RevokeEverythingMintedFor(SyntheticMailUser.Deployment);

        // Assert
        Assert.Null(sessions.Mint(second));
        Assert.NotNull(sessions.Mint(Admitted(SyntheticMailUser.Another)));
    }

    private static AdmittedUserCredential Admitted(params MailFathomPermission[] permissions) =>
        new(CredentialId, SyntheticMailUser.Deployment, permissions.Length == 0 ? [MailFathomPermission.MailRead] : permissions);

    private static AdmittedUserCredential Admitted(MailUserId user) =>
        new(CredentialId, user, [MailFathomPermission.MailRead]);

    /// <summary>The half of a token that is looked up, separator included, so a test can compose one from two.</summary>
    private static string NameOf(string token) => token[..(token.IndexOf('.', StringComparison.Ordinal) + 1)];

    /// <summary>The half of a token that proves it.</summary>
    private static string ProofOf(string token) => token[(token.IndexOf('.', StringComparison.Ordinal) + 1)..];
}
