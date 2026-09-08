// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Security.Claims;
using MailFathom.Application.Access;
using MailFathom.Application.Access.Credentials;
using MailFathom.Domain.Access;
using MailFathom.Host.Api;
using MailFathom.Host.Security.Sessions;
using MailFathom.Host.Security.Transport;
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
/// and the moment it stops working, a refusal to hold another session, or nothing at all once a session is ended.
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
    public void Exchange_ACredentialThatAuthenticated_AnswersATokenAdmittingThatCaller()
    {
        // Arrange
        var sessions = new ClientSessionTokens(new FakeTimeProvider(Instant));

        // Act
        var result = ClientSessionTokenEndpoints.Exchange(
            RequestCarrying(headerValue: "Basic dXNlcjpwYXNzd29yZA=="),
            AuthorizationFor(SyntheticMailUser.Deployment),
            sessions);

        // Assert
        var answered = Assert.IsType<Ok<ClientSessionTokenResponse>>(result.Result);
        Assert.NotNull(answered.Value);
        Assert.Equal(Instant + ClientSessionTokens.Lifetime, answered.Value.ExpiresAt);

        var admitted = sessions.Verify(answered.Value.Token);
        Assert.Equal(SyntheticMailUser.Deployment, admitted?.User);
        Assert.Equal(CredentialId, admitted?.CredentialId);
    }

    /// <summary>The grant the credential held travels onto the session, so nothing a client could do before the exchange is refused after it.</summary>
    [Fact]
    public void Exchange_ACredentialHoldingAGrant_AnswersATokenCarryingTheSameGrant()
    {
        // Arrange
        var sessions = new ClientSessionTokens(new FakeTimeProvider(Instant));

        // Act
        var result = ClientSessionTokenEndpoints.Exchange(
            RequestCarrying(headerValue: "Basic dXNlcjpwYXNzd29yZA=="),
            AuthorizationFor(SyntheticMailUser.Deployment, MailFathomPermission.MailRead, MailFathomPermission.MailAsk),
            sessions);

        // Assert
        var answered = Assert.IsType<Ok<ClientSessionTokenResponse>>(result.Result);
        Assert.Equal(
            [MailFathomPermission.MailRead, MailFathomPermission.MailAsk],
            sessions.Verify(answered.Value!.Token)?.Permissions);
    }

    /// <summary>A client presenting a live token renews it, so a session outlasts one lifetime without anybody typing a password.</summary>
    [Fact]
    public void Exchange_ARequestAlreadyCarryingASession_RenewsItAndLeavesThePresentedTokenRefused()
    {
        // Arrange
        var clock = new FakeTimeProvider(Instant);
        var sessions = new ClientSessionTokens(clock);
        var held = sessions.Mint(Admitted())!;
        clock.Advance(TimeSpan.FromHours(6));

        // Act
        var result = ClientSessionTokenEndpoints.Exchange(
            RequestCarrying($"Bearer {held.Value}"),
            AuthorizationFor(SyntheticMailUser.Deployment),
            sessions);

        // Assert
        var answered = Assert.IsType<Ok<ClientSessionTokenResponse>>(result.Result);
        Assert.NotEqual(held.Value, answered.Value!.Token);
        Assert.NotNull(sessions.Verify(answered.Value.Token));
        Assert.Null(sessions.Verify(held.Value));
    }

    /// <summary>A caller whose principal is named by something other than a credential still mints a session an operator can end.</summary>
    /// <remarks>
    /// An access token's principal is named by the issuer and the subject the deployment authorized, so a session that
    /// took its credential from that name would be one no disabling and no deletion could reach — live until its own
    /// expiry however urgently somebody wanted it gone.
    /// </remarks>
    [Fact]
    public void Exchange_ACallerWhoseIdentityNamesNoCredential_AnswersATokenTheCredentialStillEnds()
    {
        // Arrange
        var sessions = new ClientSessionTokens(new FakeTimeProvider(Instant));

        // Act
        var result = ClientSessionTokenEndpoints.Exchange(
            RequestCarrying(headerValue: "Bearer an.access.token"),
            AuthorizationNamedBy("https://sso.example.test|subject-7", SyntheticMailUser.Deployment),
            sessions);

        // Assert
        var answered = Assert.IsType<Ok<ClientSessionTokenResponse>>(result.Result);
        Assert.Equal(1, sessions.RevokeEverythingMintedBy(CredentialId));
        Assert.Null(sessions.Verify(answered.Value!.Token));
    }

    /// <summary>A principal no user credential admitted is refused rather than minting a session nothing could end.</summary>
    [Fact]
    public void Exchange_APrincipalNamingNoCredential_RefusesRatherThanMintingASessionNothingEnds()
    {
        // Arrange
        var sessions = new ClientSessionTokens(new FakeTimeProvider(Instant));
        var request = RequestAdmittedBy("Basic dXNlcjpwYXNzd29yZA==", credentialId: null);
        var authorization = AuthorizationFor(SyntheticMailUser.Deployment);

        // Act
        var refusal = Record.Exception(() => ClientSessionTokenEndpoints.Exchange(request, authorization, sessions));

        // Assert
        Assert.IsType<InvalidOperationException>(refusal);
    }

    /// <summary>A deployment already holding every session it will hold says so as a condition that passes, not as a fault.</summary>
    [Fact]
    public void Exchange_WithAsManySessionsLiveAsAreHeld_AnswersServiceUnavailableRatherThanAToken()
    {
        // Arrange
        var sessions = new ClientSessionTokens(new FakeTimeProvider(Instant));

        for (var minted = 0; minted < ClientSessionTokens.MostLiveSessions; minted += 1)
        {
            sessions.Mint(Admitted());
        }

        // Act
        var result = ClientSessionTokenEndpoints.Exchange(
            RequestCarrying(headerValue: "Basic dXNlcjpwYXNzd29yZA=="),
            AuthorizationFor(SyntheticMailUser.Deployment),
            sessions);

        // Assert
        var refused = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, refused.StatusCode);
    }

    /// <summary>Signing out ends the session on the spot, so the token stops authenticating before it expires.</summary>
    [Fact]
    public void Revoke_ARequestCarryingALiveSession_EndsItAndAnswersWithNoBody()
    {
        // Arrange
        var sessions = new ClientSessionTokens(new FakeTimeProvider(Instant));
        var held = sessions.Mint(Admitted())!;

        // Act
        var result = ClientSessionTokenEndpoints.Revoke(RequestCarrying($"Bearer {held.Value}"), sessions);

        // Assert
        Assert.IsType<NoContent>(result);
        Assert.Null(sessions.Verify(held.Value));
    }

    /// <summary>A sign-out from a client that never exchanged anything is answered identically, so the route reports nothing about what somebody is holding.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("Basic dXNlcjpwYXNzd29yZA==")]
    [InlineData("Bearer mfs_unknown.session")]
    public void Revoke_ARequestCarryingNoLiveSession_AnswersWithNoBodyToo(string headerValue)
    {
        // Arrange
        var sessions = new ClientSessionTokens(new FakeTimeProvider(Instant));

        // Act
        var result = ClientSessionTokenEndpoints.Revoke(RequestCarrying(headerValue), sessions);

        // Assert
        Assert.IsType<NoContent>(result);
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
