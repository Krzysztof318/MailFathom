// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Security.Claims;
using MailFathom.Application.Access;
using MailFathom.Application.Access.Credentials;
using MailFathom.Application.Access.Sessions;
using MailFathom.Domain.Access;
using MailFathom.Host.Api;
using MailFathom.Host.Security.Sessions;
using MailFathom.Host.Security.Transport;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Security.OAuth;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Net.Http.Headers;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers what the exchange answers a sign-in, a renewal, and a sign-out with.</summary>
/// <remarks>
/// The routes are how a client stops presenting a password, so what they answer is what a client branches on: a token
/// and the moment it stops working, a refusal to hold another session, or nothing at all once a session is ended. The
/// three refusals are what these cases exist to keep apart — a sign-in to perform again, a deployment to try again in
/// a moment, and a database that could not be reached, which is the one that must never read as either of the others.
/// </remarks>
public sealed class ClientSessionTokenEndpointsTests
{
    private static readonly DateTimeOffset Instant = new(2026, 9, 8, 9, 0, 0, TimeSpan.Zero);

    private static readonly Guid CredentialId = new("0197c0de-0000-7000-8000-0000000000bb");

    /// <summary>The paths a client appends to the address it was configured with, pinned because the client composes them from constants of its own.</summary>
    [Fact]
    public void Routes_AreThePathsAClientComposes()
    {
        Assert.Equal("/session/token", ClientSessionTokenEndpoints.ExchangeRoute);
        Assert.Equal("/session/token/revocation", ClientSessionTokenEndpoints.RevocationRoute);
    }

    /// <summary>A credential presented to the exchange comes back as a token that authenticates, which is the sign-in.</summary>
    [Fact]
    public async Task Exchange_ACredentialThatAuthenticated_AnswersATokenAdmittingThatCaller()
    {
        // Arrange
        var sessions = Sessions(out _);

        // Act
        var result = await ClientSessionTokenEndpoints.Exchange(
            RequestCarrying(headerValue: "Basic dXNlcjpwYXNzd29yZA=="),
            AuthorizationFor(SyntheticMailUser.Deployment),
            sessions,
            TestContext.Current.CancellationToken);

        // Assert
        var answered = Assert.IsType<Ok<ClientSessionTokenResponse>>(result.Result);
        Assert.NotNull(answered.Value);
        Assert.Equal(Instant + ClientSessionTokens.Lifetime, answered.Value.ExpiresAt);

        var admitted = await sessions.VerifyAsync(answered.Value.Token, TestContext.Current.CancellationToken);
        Assert.Equal(SyntheticMailUser.Deployment, admitted?.User);
        Assert.Equal(CredentialId, admitted?.CredentialId);
    }

    /// <summary>The grant the credential held travels onto the session, so nothing a client could do before the exchange is refused after it.</summary>
    [Fact]
    public async Task Exchange_ACredentialHoldingAGrant_AnswersATokenCarryingTheSameGrant()
    {
        // Arrange
        var sessions = Sessions(out _);

        // Act
        var result = await ClientSessionTokenEndpoints.Exchange(
            RequestCarrying(headerValue: "Basic dXNlcjpwYXNzd29yZA=="),
            AuthorizationFor(SyntheticMailUser.Deployment, MailFathomPermission.MailRead, MailFathomPermission.MailAsk),
            sessions,
            TestContext.Current.CancellationToken);

        // Assert
        var answered = Assert.IsType<Ok<ClientSessionTokenResponse>>(result.Result);
        var admitted = await sessions.VerifyAsync(answered.Value!.Token, TestContext.Current.CancellationToken);
        Assert.Equal([MailFathomPermission.MailRead, MailFathomPermission.MailAsk], admitted?.Permissions);
    }

    /// <summary>A client presenting a live token renews it, so a session outlasts one lifetime without anybody typing a password.</summary>
    [Fact]
    public async Task Exchange_ARequestAlreadyCarryingASession_RenewsItAndLeavesThePresentedTokenRefused()
    {
        // Arrange
        var sessions = Sessions(out _, out var clock);
        var held = await sessions.MintAsync(Admitted(), TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromHours(6));

        // Act
        var result = await ClientSessionTokenEndpoints.Exchange(
            RequestCarrying($"Bearer {held.Token!.Value}"),
            AuthorizationFor(SyntheticMailUser.Deployment),
            sessions,
            TestContext.Current.CancellationToken);

        // Assert
        var answered = Assert.IsType<Ok<ClientSessionTokenResponse>>(result.Result);
        Assert.NotEqual(held.Token.Value, answered.Value!.Token);
        Assert.NotNull(await sessions.VerifyAsync(answered.Value.Token, TestContext.Current.CancellationToken));
        Assert.Null(await sessions.VerifyAsync(held.Token.Value, TestContext.Current.CancellationToken));
    }

    /// <summary>A caller whose principal is named by something other than a credential still mints a session naming the credential row an operator acts on.</summary>
    /// <remarks>
    /// A key and a signed assertion each name their principal by the key rather than by the credential row behind it,
    /// so a session that took its credential from that name would be one no disabling and no deletion could reach —
    /// live until its own expiry however urgently somebody wanted it gone. What the session is ended by is the claim
    /// the admitting scheme wrote, which is why the name is free to be anything.
    /// </remarks>
    [Fact]
    public async Task Exchange_ACallerWhoseIdentityNamesNoCredential_AnswersATokenNamingTheCredentialThatEndsIt()
    {
        // Arrange
        var sessions = Sessions(out var store);

        // Act
        var result = await ClientSessionTokenEndpoints.Exchange(
            RequestCarrying(headerValue: "Bearer an.access.token"),
            AuthorizationNamedBy("https://sso.example.test|subject-7", SyntheticMailUser.Deployment),
            sessions,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<Ok<ClientSessionTokenResponse>>(result.Result);
        Assert.Equal(CredentialId, Assert.Single(store.Grants).CredentialId);
    }

    /// <summary>An access token is refused here rather than exchanged for a session nothing could bound.</summary>
    /// <remarks>
    /// The token's own expiry, its revocation at the authorization server, and the scopes that server's entry requires
    /// are each judged per request against the token, and none of them can be judged against a session this deployment
    /// minted. It costs such a caller nothing either: validating a token derives no key.
    /// </remarks>
    [Fact]
    public async Task Exchange_ARequestAnAccessTokenAdmitted_RefusesRatherThanMintingASessionNothingBounds()
    {
        // Arrange
        var sessions = Sessions(out _);
        var request = new DefaultHttpContext();
        request.Request.Headers[HeaderNames.Authorization] = "Bearer an.access.token";
        request.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(OAuthIdentity.IssuerClaimType, "https://sso.example.test")],
            "test"));

        // Act
        var result = await ClientSessionTokenEndpoints.Exchange(
            request,
            AuthorizationNamedBy("https://sso.example.test|subject-7", SyntheticMailUser.Deployment),
            sessions,
            TestContext.Current.CancellationToken);

        // Assert
        var refused = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, refused.StatusCode);
    }

    /// <summary>A principal no user credential admitted is refused rather than minting a session nothing could end.</summary>
    [Fact]
    public async Task Exchange_APrincipalNamingNoCredential_RefusesRatherThanMintingASessionNothingEnds()
    {
        // Arrange
        var sessions = Sessions(out _);
        var request = RequestAdmittedBy("Basic dXNlcjpwYXNzd29yZA==", credentialId: null);
        var authorization = AuthorizationFor(SyntheticMailUser.Deployment);

        // Act
        var refusal = await Record.ExceptionAsync(() => ClientSessionTokenEndpoints.Exchange(
            request,
            authorization,
            sessions,
            TestContext.Current.CancellationToken));

        // Assert
        Assert.IsType<InvalidOperationException>(refusal);
    }

    /// <summary>An endpoint requiring no credential still signs somebody in, which is a configuration a mandatory credential would have refused outright.</summary>
    /// <remarks>Nothing on such an endpoint is authenticated, so there is no row behind the session for an operator to revoke it by — but the user is named, which is what an erasure reaches it through.</remarks>
    [Fact]
    public async Task Exchange_OnAnEndpointRequiringNoCredential_AnswersATokenNamingTheUserAndNoCredential()
    {
        // Arrange
        var sessions = Sessions(out var store);
        var request = new DefaultHttpContext();
        request.Request.Headers[HeaderNames.Authorization] = string.Empty;
        request.User = new ClaimsPrincipal(new ClaimsIdentity());

        // Act
        var result = await ClientSessionTokenEndpoints.Exchange(
            request,
            AuthorizationFor(SyntheticMailUser.Deployment),
            sessions,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<Ok<ClientSessionTokenResponse>>(result.Result);
        var grant = Assert.Single(store.Grants);
        Assert.Null(grant.CredentialId);
        Assert.Equal(SyntheticMailUser.Deployment, grant.User);
    }

    /// <summary>A deployment already holding every session it will hold says so as a condition that passes, not as a fault.</summary>
    [Fact]
    public async Task Exchange_WithAsManySessionsLiveAsAreHeld_AnswersServiceUnavailableRatherThanAToken()
    {
        // Arrange
        var sessions = Sessions(out _);

        for (var minted = 0; minted < ClientSessionTokens.MostLiveSessions; minted += 1)
        {
            await sessions.MintAsync(Admitted(), TestContext.Current.CancellationToken);
        }

        // Act
        var result = await ClientSessionTokenEndpoints.Exchange(
            RequestCarrying(headerValue: "Basic dXNlcjpwYXNzd29yZA=="),
            AuthorizationFor(SyntheticMailUser.Deployment),
            sessions,
            TestContext.Current.CancellationToken);

        // Assert
        var refused = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, refused.StatusCode);
    }

    /// <summary>A token the deployment is not holding is an authentication failure rather than a deployment that is busy.</summary>
    /// <remarks>
    /// Answered as the bound instead, the client would read it as a deployment to try again in a moment and would go
    /// on presenting a token nothing will ever accept until it expired.
    /// </remarks>
    [Fact]
    public async Task Exchange_ARenewalPresentingATokenTheDeploymentDoesNotHold_AnswersUnauthorizedRatherThanUnavailable()
    {
        // Arrange
        var sessions = Sessions(out _);

        // Act
        var result = await ClientSessionTokenEndpoints.Exchange(
            RequestCarrying(headerValue: "Bearer mfs_nodeploymentheldit.bm90LWEtc2Vzc2lvbg"),
            AuthorizationFor(SyntheticMailUser.Deployment),
            sessions,
            TestContext.Current.CancellationToken);

        // Assert
        var refused = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status401Unauthorized, refused.StatusCode);
    }

    /// <summary>A credential an operator ended while this exchange was in flight is refused rather than answered a session nothing ends.</summary>
    /// <remarks>The refusal is the store's, from inside the transaction that would have written the row, so what this covers is that it reaches the caller as a sign-in to perform again rather than as a deployment to retry.</remarks>
    [Fact]
    public async Task Exchange_ForACredentialAnOperatorEnded_AnswersUnauthorizedRatherThanAToken()
    {
        // Arrange
        var sessions = Sessions(out var store);
        store.NoLongerAdmits = true;

        // Act
        var result = await ClientSessionTokenEndpoints.Exchange(
            RequestCarrying(headerValue: "Basic dXNlcjpwYXNzd29yZA=="),
            AuthorizationFor(SyntheticMailUser.Deployment),
            sessions,
            TestContext.Current.CancellationToken);

        // Assert
        var refused = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status401Unauthorized, refused.StatusCode);
    }

    /// <summary>A store that cannot be reached leaves the exchange as a failure the pipeline answers as unavailable, never as a refused credential.</summary>
    /// <remarks>
    /// The route deliberately catches nothing: a client meets an authentication failure by clearing what it holds and
    /// asking for a password, so an outage answered that way would sign every signed-in client out. What turns it into
    /// a <c>503</c> is one registration covering the exchange and every authenticated request at once.
    /// </remarks>
    [Fact]
    public async Task Exchange_WhenTheSessionsCannotBeReached_LeavesTheFailureForThePipelineToAnswerAsUnavailable()
    {
        // Arrange
        var sessions = Sessions(out var store);
        store.Unreachable = new ClientSessionStoreUnavailableException(
            "The deployment's client sessions could not be reached.",
            new InvalidOperationException("No connection."));

        // Act, Assert
        await Assert.ThrowsAsync<ClientSessionStoreUnavailableException>(() => ClientSessionTokenEndpoints.Exchange(
            RequestCarrying(headerValue: "Basic dXNlcjpwYXNzd29yZA=="),
            AuthorizationFor(SyntheticMailUser.Deployment),
            sessions,
            TestContext.Current.CancellationToken));
    }

    /// <summary>Signing out ends the session on the spot, so the token stops authenticating before it expires.</summary>
    [Fact]
    public async Task Revoke_ARequestCarryingALiveSession_EndsItAndAnswersWithNoBody()
    {
        // Arrange
        var sessions = Sessions(out _);
        var held = await sessions.MintAsync(Admitted(), TestContext.Current.CancellationToken);

        // Act
        var result = await ClientSessionTokenEndpoints.Revoke(
            RequestCarrying($"Bearer {held.Token!.Value}"),
            sessions,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NoContent>(result);
        Assert.Null(await sessions.VerifyAsync(held.Token.Value, TestContext.Current.CancellationToken));
    }

    /// <summary>A sign-out from a client that never exchanged anything is answered identically, so the route reports nothing about what somebody is holding.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("Basic dXNlcjpwYXNzd29yZA==")]
    [InlineData("Bearer mfs_unknown.session")]
    public async Task Revoke_ARequestCarryingNoLiveSession_AnswersWithNoBodyToo(string headerValue)
    {
        // Arrange
        var sessions = Sessions(out _);

        // Act
        var result = await ClientSessionTokenEndpoints.Revoke(
            RequestCarrying(headerValue),
            sessions,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NoContent>(result);
    }

    private static ClientSessionTokens Sessions(out InMemoryClientSessionStore store) => Sessions(out store, out _);

    private static ClientSessionTokens Sessions(out InMemoryClientSessionStore store, out FakeTimeProvider clock)
    {
        store = new InMemoryClientSessionStore();
        clock = new FakeTimeProvider(Instant);

        return new ClientSessionTokens(store, clock);
    }

    private static AdmittedUserCredential Admitted() =>
        new(CredentialId, SyntheticMailUser.Deployment, [MailFathomPermission.MailRead]);

    private static DefaultHttpContext RequestCarrying(string headerValue) =>
        RequestAdmittedBy(headerValue, CredentialId);

    private static DefaultHttpContext RequestAdmittedBy(string headerValue, Guid? credentialId)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[HeaderNames.Authorization] = headerValue;
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            credentialId is { } admittedBy ? [TransportCallerCredential.ClaimFor(admittedBy)] : [],
            "test"));

        return context;
    }

    private static AccessAuthorization AuthorizationFor(MailUserId user, params MailFathomPermission[] granted) =>
        AuthorizationNamedBy(CredentialId.ToString("D", CultureInfo.InvariantCulture), user, granted);

    private static AccessAuthorization AuthorizationNamedBy(
        string principalIdentity,
        MailUserId user,
        params MailFathomPermission[] granted)
    {
        var principals = Substitute.For<IAuthorizedPrincipalSource>();
        principals.Current.Returns(AuthorizedPrincipal.CallerActingFor(
            user,
            principalIdentity,
            granted.Length == 0 ? [MailFathomPermission.MailRead] : granted));

        return new AccessAuthorization(principals);
    }
}
