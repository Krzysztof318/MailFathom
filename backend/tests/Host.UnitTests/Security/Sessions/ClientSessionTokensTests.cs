// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access.Credentials;
using MailFathom.Application.Access.Sessions;
using MailFathom.Domain.Access;
using MailFathom.Host.Security.Sessions;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Host.UnitTests.Security.Sessions;

/// <summary>Covers what a session token admits, what it refuses, how it renews, what ends it before it expires, and when the deployment removes what it can no longer authenticate.</summary>
/// <remarks>
/// What these cases cover is the half of the session that is above the port: the shape a presented value must have,
/// the digest the deployment is asked to compare, the expiry, the grant a renewal carries forward, and when a sweep is
/// asked for. What the composed statements do — the lock order, a concurrent pair, the two cascading foreign keys, and
/// the bound counted by the insert — is the orchestrated suite's, for the reason the adapter carries
/// <c>[RequiresIntegrationCoverage]</c>: only PostgreSQL settles any of them, and a dictionary standing in would only
/// prove itself.
/// </remarks>
public sealed class ClientSessionTokensTests
{
    private static readonly DateTimeOffset Instant = new(2026, 9, 8, 9, 0, 0, TimeSpan.Zero);

    private static readonly Guid CredentialId = new("2a5f8f2e-6c1d-4c0a-9c2f-2f3b6a1d4e77");

    /// <summary>A token names the user and the grant the exchange resolved, which is what lets a request be served without resolving either again.</summary>
    [Fact]
    public async Task VerifyAsync_AFreshlyMintedToken_AdmitsWhatTheExchangeResolved()
    {
        // Arrange
        var sessions = Sessions(out _);
        var admitted = Admitted(MailFathomPermission.MailRead, MailFathomPermission.MailAsk);

        // Act
        var minted = await sessions.MintAsync(admitted, TestContext.Current.CancellationToken);
        var verified = await sessions.VerifyAsync(minted.Token?.Value, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ClientSessionMintOutcome.Minted, minted.Outcome);
        Assert.Equal(admitted, verified);
    }

    /// <summary>Two people signed in at once each hold their own, so one request can never be served as the other person.</summary>
    [Fact]
    public async Task VerifyAsync_TokensMintedForTwoUsers_AdmitsEachAsThemselves()
    {
        // Arrange
        var sessions = Sessions(out _);
        var mine = await sessions.MintAsync(Admitted(SyntheticMailUser.Deployment), TestContext.Current.CancellationToken);
        var theirs = await sessions.MintAsync(Admitted(SyntheticMailUser.Another), TestContext.Current.CancellationToken);

        // Act
        var admittedAsMe = await sessions.VerifyAsync(mine.Token?.Value, TestContext.Current.CancellationToken);
        var admittedAsThem = await sessions.VerifyAsync(theirs.Token?.Value, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SyntheticMailUser.Deployment, admittedAsMe?.User);
        Assert.Equal(SyntheticMailUser.Another, admittedAsThem?.User);
    }

    /// <summary>A session outlives the request that presented it, which is the whole difference between this and a one-connection ticket.</summary>
    [Fact]
    public async Task VerifyAsync_TheSameTokenSeveralTimes_AdmitsEveryTime()
    {
        // Arrange
        var sessions = Sessions(out _);
        var minted = await sessions.MintAsync(Admitted(), TestContext.Current.CancellationToken);

        // Act, Assert
        Assert.NotNull(await sessions.VerifyAsync(minted.Token?.Value, TestContext.Current.CancellationToken));
        Assert.NotNull(await sessions.VerifyAsync(minted.Token?.Value, TestContext.Current.CancellationToken));
        Assert.NotNull(await sessions.VerifyAsync(minted.Token?.Value, TestContext.Current.CancellationToken));
    }

    /// <summary>Presenting the identifier with somebody else's proof authenticates nobody, which is what makes only the secret half secret.</summary>
    [Fact]
    public async Task VerifyAsync_ATokenCarryingAnotherSessionsProof_IsRefused()
    {
        // Arrange
        var sessions = Sessions(out _);
        var mine = await sessions.MintAsync(Admitted(), TestContext.Current.CancellationToken);
        var theirs = await sessions.MintAsync(Admitted(), TestContext.Current.CancellationToken);

        // Act
        var verified = await sessions.VerifyAsync(
            NameOf(mine.Token!.Value) + ProofOf(theirs.Token!.Value),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(verified);
    }

    /// <summary>Anything that is not a token this deployment mints is refused before the store is asked.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("mfs_")]
    [InlineData("mfs_no-separator-at-all")]
    [InlineData("mfs_.")]
    [InlineData("mfs_name.")]
    [InlineData("mfs_name.not base64url")]
    [InlineData("mfk_a-key-rather-than-a-session.proof")]
    [InlineData("name.proof")]
    public async Task VerifyAsync_AValueThatIsNotAMintedToken_IsRefusedWithoutAskingTheStore(string? presented)
    {
        // Arrange
        var sessions = Sessions(out var store);
        store.Unreachable = new ClientSessionStoreUnavailableException("Nothing should reach this store.", new InvalidOperationException());

        // Act, Assert
        Assert.Null(await sessions.VerifyAsync(presented, TestContext.Current.CancellationToken));
    }

    /// <summary>A value longer than anything this type mints is refused unread rather than walked.</summary>
    /// <remarks>
    /// The value presented is a live token of this deployment's own with whitespace inside its proof, which the
    /// base64url decoder ignores — so every step after the length check holds and the value authenticates the moment
    /// the bound stops refusing to walk it. That is what makes this an assertion about the bound rather than about a
    /// lookup missing in an empty store.
    /// </remarks>
    [Fact]
    public async Task VerifyAsync_AValuePastTheBound_IsRefusedWithoutBeingWalked()
    {
        // Arrange
        var sessions = Sessions(out _);
        var minted = await sessions.MintAsync(Admitted(), TestContext.Current.CancellationToken);
        var separator = minted.Token!.Value.IndexOf('.', StringComparison.Ordinal) + 1;
        var padded = minted.Token.Value[..separator] + new string(' ', 300) + minted.Token.Value[separator..];

        // Act, Assert
        Assert.NotNull(await sessions.VerifyAsync(minted.Token.Value, TestContext.Current.CancellationToken));
        Assert.True(padded.Length > ClientSessionTokens.LongestPresentedToken);
        Assert.Null(await sessions.VerifyAsync(padded, TestContext.Current.CancellationToken));
    }

    /// <summary>A session ends by itself, so a token left on a machine nobody came back to stops authenticating.</summary>
    [Fact]
    public async Task VerifyAsync_ATokenPresentedAfterItsLifetime_IsRefused()
    {
        // Arrange
        var sessions = Sessions(out _, out var clock);
        var minted = await sessions.MintAsync(Admitted(), TestContext.Current.CancellationToken);

        // Act
        clock.Advance(ClientSessionTokens.Lifetime + TimeSpan.FromSeconds(1));

        // Assert
        Assert.Null(await sessions.VerifyAsync(minted.Token?.Value, TestContext.Current.CancellationToken));
    }

    /// <summary>The last instant of the lifetime still authenticates, so the bound is a lifetime rather than one shorter than it says.</summary>
    [Fact]
    public async Task VerifyAsync_ATokenPresentedAtTheLastInstantOfItsLifetime_IsAdmitted()
    {
        // Arrange
        var sessions = Sessions(out _, out var clock);
        var minted = await sessions.MintAsync(Admitted(), TestContext.Current.CancellationToken);

        // Act
        clock.Advance(ClientSessionTokens.Lifetime);

        // Assert
        Assert.NotNull(await sessions.VerifyAsync(minted.Token?.Value, TestContext.Current.CancellationToken));
    }

    /// <summary>A store that cannot answer refuses the request as unavailable rather than as unauthenticated, which is what stops a database blip from signing everybody out.</summary>
    /// <remarks>
    /// The one case that must not be a <see langword="null" />: a client meets a refusal by clearing what it holds and
    /// asking for a password, so a read answered "nobody holds this" during an outage would sign every signed-in
    /// client out and lose every session the outage covered.
    /// </remarks>
    [Fact]
    public async Task VerifyAsync_WhenTheStoreCannotBeReached_RaisesRatherThanRefusingTheSession()
    {
        // Arrange
        var sessions = Sessions(out var store);
        var minted = await sessions.MintAsync(Admitted(), TestContext.Current.CancellationToken);
        store.Unreachable = Unreachable();

        // Act, Assert
        await Assert.ThrowsAsync<ClientSessionStoreUnavailableException>(
            () => sessions.VerifyAsync(minted.Token?.Value, TestContext.Current.CancellationToken));
    }

    /// <summary>The exchange refuses the same way, so a sign-in during an outage is answered as unavailable rather than as a wrong password.</summary>
    [Fact]
    public async Task MintAsync_WhenTheStoreCannotBeReached_RaisesRatherThanRefusingTheSignIn()
    {
        // Arrange
        var sessions = Sessions(out var store);
        store.Unreachable = Unreachable();

        // Act, Assert
        await Assert.ThrowsAsync<ClientSessionStoreUnavailableException>(
            () => sessions.MintAsync(Admitted(), TestContext.Current.CancellationToken));
    }

    /// <summary>Renewing gives a client a fresh lifetime without anybody typing a password again.</summary>
    [Fact]
    public async Task RenewAsync_ALiveSession_AnswersATokenAdmittingTheSameCallerForAFreshLifetime()
    {
        // Arrange
        var sessions = Sessions(out _, out var clock);
        var admitted = Admitted();
        var minted = await sessions.MintAsync(admitted, TestContext.Current.CancellationToken);

        // Act
        clock.Advance(TimeSpan.FromHours(6));
        var renewed = await sessions.RenewAsync(minted.Token!.Value, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(renewed);
        Assert.Equal(admitted, await sessions.VerifyAsync(renewed.Value, TestContext.Current.CancellationToken));
        Assert.Equal(clock.GetUtcNow() + ClientSessionTokens.Lifetime, renewed.ExpiresAt);
    }

    /// <summary>The renewed token replaces the presented one, so one sign-in leaves one live credential rather than a trail of them.</summary>
    [Fact]
    public async Task RenewAsync_ALiveSession_LeavesThePresentedTokenRefused()
    {
        // Arrange
        var sessions = Sessions(out var store);
        var minted = await sessions.MintAsync(Admitted(), TestContext.Current.CancellationToken);

        // Act
        await sessions.RenewAsync(minted.Token!.Value, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(await sessions.VerifyAsync(minted.Token.Value, TestContext.Current.CancellationToken));
        Assert.Equal(1, store.Count);
    }

    /// <summary>A renewal presenting a correct identifier with a wrong secret leaves the session live, which is the proof the removal was guarded rather than performed and then judged.</summary>
    /// <remarks>
    /// A session survives being presented, unlike a one-connection ticket, so removing by the identifier and comparing
    /// the secret afterwards would let anybody writing the half of a token that is not a secret end somebody else's
    /// session — the denial a revocation is guarded against, arriving through the renewal instead.
    /// </remarks>
    [Fact]
    public async Task RenewAsync_ACorrectIdentifierWithTheWrongSecret_LeavesTheSessionLive()
    {
        // Arrange
        var sessions = Sessions(out _);
        var mine = await sessions.MintAsync(Admitted(), TestContext.Current.CancellationToken);
        var theirs = await sessions.MintAsync(Admitted(), TestContext.Current.CancellationToken);

        // Act
        var renewed = await sessions.RenewAsync(
            NameOf(mine.Token!.Value) + ProofOf(theirs.Token!.Value),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(renewed);
        Assert.NotNull(await sessions.VerifyAsync(mine.Token.Value, TestContext.Current.CancellationToken));
    }

    /// <summary>A session that has already ended renews nothing, so an expired token is a sign-in rather than a renewal.</summary>
    [Fact]
    public async Task RenewAsync_ATokenThatNoLongerAuthenticates_AnswersNothing()
    {
        // Arrange
        var sessions = Sessions(out _, out var clock);
        var minted = await sessions.MintAsync(Admitted(), TestContext.Current.CancellationToken);

        // Act
        clock.Advance(ClientSessionTokens.Lifetime + TimeSpan.FromSeconds(1));

        // Assert
        Assert.Null(await sessions.RenewAsync(minted.Token!.Value, TestContext.Current.CancellationToken));
    }

    /// <summary>A renewal whose credential an operator disabled meanwhile answers nothing, so the act reaches the path a client stays signed in through.</summary>
    /// <remarks>The renewal is the second path that writes a session row, and the one the share lock is easiest to leave off — nothing on the read path would catch a replacement written past a disable.</remarks>
    [Fact]
    public async Task RenewAsync_WhenTheCredentialNoLongerAdmitsASession_AnswersNothing()
    {
        // Arrange
        var sessions = Sessions(out var store);
        var minted = await sessions.MintAsync(Admitted(), TestContext.Current.CancellationToken);

        // Act
        store.NoLongerAdmits = true;
        var renewed = await sessions.RenewAsync(minted.Token!.Value, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(renewed);
    }

    /// <summary>Signing out ends the session at once rather than leaving the token good until it expires.</summary>
    [Fact]
    public async Task RevokeAsync_ALiveSession_LeavesTheTokenRefusedOnTheNextRequest()
    {
        // Arrange
        var sessions = Sessions(out _);
        var minted = await sessions.MintAsync(Admitted(), TestContext.Current.CancellationToken);

        // Act
        var revoked = await sessions.RevokeAsync(minted.Token!.Value, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(revoked);
        Assert.Null(await sessions.VerifyAsync(minted.Token.Value, TestContext.Current.CancellationToken));
    }

    /// <summary>A caller writing an identifier it guessed signs nobody out, the identifier being the half that is not a secret.</summary>
    [Fact]
    public async Task RevokeAsync_TheIdentifierOfALiveSessionWithoutItsProof_EndsNothing()
    {
        // Arrange
        var sessions = Sessions(out _);
        var mine = await sessions.MintAsync(Admitted(), TestContext.Current.CancellationToken);
        var theirs = await sessions.MintAsync(Admitted(), TestContext.Current.CancellationToken);

        // Act
        var revoked = await sessions.RevokeAsync(
            NameOf(mine.Token!.Value) + ProofOf(theirs.Token!.Value),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(revoked);
        Assert.NotNull(await sessions.VerifyAsync(mine.Token.Value, TestContext.Current.CancellationToken));
    }

    /// <summary>A credential or a user an operator ended is reported as a sign-in to perform again, which is a different answer from the deployment being full.</summary>
    /// <remarks>Whether either still admits a session is the store's answer from inside the transaction that would have written the row, rather than a check this type makes first — so what this covers is that the two refusals stay told apart all the way to the route.</remarks>
    [Fact]
    public async Task MintAsync_WhenTheUserOrTheCredentialNoLongerAdmitsASession_ReportsThatRatherThanTheBound()
    {
        // Arrange
        var sessions = Sessions(out var store);
        store.NoLongerAdmits = true;

        // Act
        var minted = await sessions.MintAsync(Admitted(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ClientSessionMintOutcome.NoLongerAdmitted, minted.Outcome);
        Assert.Null(minted.Token);
    }

    /// <summary>A client endpoint requiring no credential still signs somebody in, and the session it mints names the user an erasure reaches it through.</summary>
    /// <remarks>
    /// The configuration a foreign key on the credential would have broken outright: nothing on such an endpoint is
    /// authenticated, so the exchange reports an identifier matching no credential the deployment could hold, and the
    /// row carries an absent credential rather than that sentinel.
    /// </remarks>
    [Fact]
    public async Task MintAsync_OnAnEndpointRequiringNoCredential_HoldsASessionNamingTheUserAndNoCredential()
    {
        // Arrange
        var sessions = Sessions(out var store);
        var admitted = new AdmittedUserCredential(Guid.Empty, SyntheticMailUser.Deployment, [MailFathomPermission.MailRead]);

        // Act
        var minted = await sessions.MintAsync(admitted, TestContext.Current.CancellationToken);
        var verified = await sessions.VerifyAsync(minted.Token?.Value, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ClientSessionMintOutcome.Minted, minted.Outcome);
        Assert.Equal(SyntheticMailUser.Deployment, verified?.User);
        Assert.Equal(Guid.Empty, verified?.CredentialId);
        Assert.Null(Assert.Single(store.Grants).CredentialId);
    }

    /// <summary>The bound refuses rather than evicting, so a deployment at its ceiling signs nobody else out to admit one more.</summary>
    [Fact]
    public async Task MintAsync_AtTheCeilingWithEverySessionLive_RefusesAndLeavesTheLiveOnesAlone()
    {
        // Arrange
        var sessions = Sessions(out _);
        var first = await sessions.MintAsync(Admitted(), TestContext.Current.CancellationToken);

        for (var minted = 1; minted < ClientSessionTokens.MostLiveSessions; minted += 1)
        {
            await sessions.MintAsync(Admitted(), TestContext.Current.CancellationToken);
        }

        // Act
        var refused = await sessions.MintAsync(Admitted(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ClientSessionMintOutcome.BoundReached, refused.Outcome);
        Assert.NotNull(await sessions.VerifyAsync(first.Token?.Value, TestContext.Current.CancellationToken));
    }

    /// <summary>An expired session is not a live one, so a deployment whose ceiling is reached by history sweeps and admits the next sign-in.</summary>
    /// <remarks>The bound is counted by the statement that writes the row, which counts what stands rather than what is live — so without the sweep before the refusal, ten thousand abandoned rows would refuse every sign-in until something else removed them.</remarks>
    [Fact]
    public async Task MintAsync_AtTheCeilingWhereEverySessionHasExpired_SweepsAndAdmitsTheNextSignIn()
    {
        // Arrange
        var sessions = Sessions(out _, out var clock);

        for (var minted = 0; minted < ClientSessionTokens.MostLiveSessions; minted += 1)
        {
            await sessions.MintAsync(Admitted(), TestContext.Current.CancellationToken);
        }

        // Act
        clock.Advance(ClientSessionTokens.Lifetime + TimeSpan.FromSeconds(1));
        var admitted = await sessions.MintAsync(Admitted(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ClientSessionMintOutcome.Minted, admitted.Outcome);
    }

    /// <summary>A replacement never meets the bound, so a full deployment renews the clients it already signed in rather than expiring them.</summary>
    /// <remarks>
    /// The guarantee the store publishes and the one nothing else would report: a renewal refused for capacity would
    /// end every live session at its own expiry for as long as the deployment stayed full, with no way back in until
    /// the table drained.
    /// </remarks>
    [Fact]
    public async Task RenewAsync_AtTheCeilingWithEverySessionLive_StillAnswersAFreshToken()
    {
        // Arrange
        var sessions = Sessions(out _);
        var held = await sessions.MintAsync(Admitted(), TestContext.Current.CancellationToken);

        for (var minted = 1; minted < ClientSessionTokens.MostLiveSessions; minted += 1)
        {
            await sessions.MintAsync(Admitted(), TestContext.Current.CancellationToken);
        }

        // Act
        var renewed = await sessions.RenewAsync(held.Token!.Value, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(renewed);
        Assert.NotNull(await sessions.VerifyAsync(renewed.Value, TestContext.Current.CancellationToken));
        Assert.Null(await sessions.VerifyAsync(held.Token.Value, TestContext.Current.CancellationToken));
    }

    /// <summary>The removal of what can no longer authenticate is reached from the read path, which is the half that separates this sweep from the one it copies.</summary>
    /// <remarks>
    /// A sweep hanging off the minting path alone would run only while people are signing in, which is not where
    /// abandoned sessions accumulate. The read is what every authenticated request performs, so any client traffic at
    /// all sweeps — and a deployment nobody signs into again still removes what expired.
    /// </remarks>
    [Fact]
    public async Task VerifyAsync_OnceTheSweepIsDue_RemovesWhatCanNoLongerAuthenticate()
    {
        // Arrange
        var sessions = Sessions(out var store, out var clock);
        var minted = await sessions.MintAsync(Admitted(), TestContext.Current.CancellationToken);
        var swept = store.RemovalCount;

        // Act
        clock.Advance(ClientSessionTokens.SweepInterval);
        await sessions.VerifyAsync(minted.Token?.Value, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(swept + 1, store.RemovalCount);
    }

    /// <summary>The sweep is throttled to its own interval rather than issued per request, so reading a session is one statement and not two.</summary>
    [Fact]
    public async Task VerifyAsync_BeforeTheSweepIsDue_RemovesNothing()
    {
        // Arrange
        var sessions = Sessions(out var store, out var clock);
        var minted = await sessions.MintAsync(Admitted(), TestContext.Current.CancellationToken);
        var swept = store.RemovalCount;

        // Act
        clock.Advance(ClientSessionTokens.SweepInterval - TimeSpan.FromMinutes(1));
        await sessions.VerifyAsync(minted.Token?.Value, TestContext.Current.CancellationToken);
        await sessions.VerifyAsync(minted.Token?.Value, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(swept, store.RemovalCount);
    }

    /// <summary>The interval is not the lifetime, which is what the two stores this one copies use and what would leave a sweep due after most processes have ended.</summary>
    [Fact]
    public void SweepInterval_AgainstTheLifetime_IsShorterRatherThanEqualToIt()
    {
        // Act, Assert
        Assert.True(ClientSessionTokens.SweepInterval < ClientSessionTokens.Lifetime);
    }

    private static ClientSessionTokens Sessions(out InMemoryClientSessionStore store) =>
        Sessions(out store, out _);

    private static ClientSessionTokens Sessions(out InMemoryClientSessionStore store, out FakeTimeProvider clock)
    {
        store = new InMemoryClientSessionStore();
        clock = new FakeTimeProvider(Instant);

        return new ClientSessionTokens(store, clock);
    }

    private static ClientSessionStoreUnavailableException Unreachable() =>
        new("The deployment's client sessions could not be reached.", new InvalidOperationException("No connection."));

    private static AdmittedUserCredential Admitted(params MailFathomPermission[] permissions) =>
        new(CredentialId, SyntheticMailUser.Deployment, permissions.Length == 0 ? [MailFathomPermission.MailRead] : permissions);

    private static AdmittedUserCredential Admitted(MailUserId user) =>
        new(CredentialId, user, [MailFathomPermission.MailRead]);

    /// <summary>The half of a token that is looked up, separator included, so a test can compose one from two.</summary>
    private static string NameOf(string token) => token[..(token.IndexOf('.', StringComparison.Ordinal) + 1)];

    /// <summary>The half of a token that proves it.</summary>
    private static string ProofOf(string token) => token[(token.IndexOf('.', StringComparison.Ordinal) + 1)..];
}
