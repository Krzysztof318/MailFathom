// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Encodings.Web;
using MailFathom.Application.Access.Credentials;
using MailFathom.Domain.Access;
using MailFathom.Host.Security.Sessions;
using MailFathom.Host.Security.Transport;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace MailFathom.Host.UnitTests.Security.Sessions;

/// <summary>Covers what the handler establishes at the request boundary, and what it costs to establish it.</summary>
/// <remarks>
/// Judging the token is <see cref="ClientSessionTokens" />'s and is covered there. What only exists here is what a
/// success carries — the user the exchange resolved and the grant it carried — and the property the whole exchange was
/// built for: this handler holds no credential store, no password hasher, and no clock beyond the store's own, so an
/// authenticated request derives nothing and reads nothing.
/// </remarks>
public sealed class ClientSessionTokenAuthenticationHandlerTests
{
    private static readonly DateTimeOffset Instant = new(2026, 9, 8, 9, 0, 0, TimeSpan.Zero);

    private static readonly Guid CredentialId = new("0197c0de-0000-7000-8000-0000000000aa");

    /// <summary>A live token authenticates, and the request acts for the user the exchange behind it resolved.</summary>
    [Fact]
    public async Task AuthenticateAsync_ALiveSessionToken_ActsForTheUserTheExchangeResolved()
    {
        // Arrange
        var sessions = Sessions();
        var minted = (await sessions.MintAsync(Admitted(), TestContext.Current.CancellationToken)).Token!;

        // Act
        var result = await AuthenticateAsync(sessions, $"Bearer {minted.Value}");

        // Assert
        Assert.True(result.Succeeded);
        Assert.Equal(SyntheticMailUser.Deployment, TransportCallerUser.CarriedBy(result.Principal!));
    }

    /// <summary>The grant travels on the session, so a request served this way may do what the credential it came from may do.</summary>
    [Fact]
    public async Task AuthenticateAsync_ALiveSessionToken_CarriesTheGrantTheCredentialHeld()
    {
        // Arrange
        var sessions = Sessions();
        var minted = (await sessions.MintAsync(Admitted(), TestContext.Current.CancellationToken)).Token!;

        // Act
        var result = await AuthenticateAsync(sessions, $"Bearer {minted.Value}");

        // Assert
        Assert.True(result.Succeeded);
        Assert.Equal(
            new HashSet<MailFathomPermission> { MailFathomPermission.MailRead, MailFathomPermission.MailAsk },
            TransportGrant.PermissionsCarriedBy(result.Principal!));
    }

    /// <summary>A revoked session is refused on the very next request rather than at the token's own expiry.</summary>
    [Fact]
    public async Task AuthenticateAsync_ASessionRevokedSinceItWasMinted_IsRefused()
    {
        // Arrange
        var sessions = Sessions();
        var minted = (await sessions.MintAsync(Admitted(), TestContext.Current.CancellationToken)).Token!;
        await sessions.RevokeAsync(minted.Value, TestContext.Current.CancellationToken);

        // Act
        var result = await AuthenticateAsync(sessions, $"Bearer {minted.Value}");

        // Assert
        Assert.False(result.Succeeded);
    }

    /// <summary>Everything that is not a live token is refused identically, so a refusal says nothing about which of them it was.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("Bearer mfs_unknown.session")]
    [InlineData("Bearer mfk_a-key-rather-than-a-session")]
    [InlineData("Basic dXNlcjpwYXNzd29yZA==")]
    public async Task AuthenticateAsync_AnythingThatIsNotALiveSession_IsRefused(string headerValue)
    {
        // Arrange
        var sessions = Sessions();

        // Act
        var result = await AuthenticateAsync(sessions, headerValue);

        // Assert
        Assert.False(result.Succeeded);
    }

    /// <summary>A refusal offers the bare challenge every method on the surface produces, and never asks a person for a password.</summary>
    [Fact]
    public async Task ChallengeAsync_ARequestPresentingNoSession_OffersTheBareChallengeAndNoPasswordChallenge()
    {
        // Arrange
        var sessions = Sessions();
        var context = new DefaultHttpContext();
        var handler = await InitializeAsync(sessions, headerValue: string.Empty, context);

        // Act
        await handler.ChallengeAsync(properties: null);

        // Assert
        var challenges = context.Response.Headers.WWWAuthenticate.ToString();
        Assert.Contains("Bearer", challenges, StringComparison.Ordinal);
        Assert.DoesNotContain("Basic", challenges, StringComparison.Ordinal);
    }

    private static ClientSessionTokens Sessions() =>
        new(new InMemoryClientSessionStore(), new FakeTimeProvider(Instant));

    private static AdmittedUserCredential Admitted() => new(
        CredentialId,
        SyntheticMailUser.Deployment,
        [MailFathomPermission.MailRead, MailFathomPermission.MailAsk]);

    private static async Task<AuthenticateResult> AuthenticateAsync(ClientSessionTokens sessions, string headerValue)
    {
        var handler = await InitializeAsync(sessions, headerValue, new DefaultHttpContext());

        return await handler.AuthenticateAsync();
    }

    private static async Task<IAuthenticationHandler> InitializeAsync(
        ClientSessionTokens sessions,
        string headerValue,
        HttpContext context)
    {
        var handler = new ClientSessionTokenAuthenticationHandler(
            new StaticOptionsMonitor(new ClientSessionTokenAuthenticationSchemeOptions
            {
                Surface = TransportSurface.Client,
            }),
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            sessions);

        context.Request.Scheme = "https";
        context.Request.Headers[HeaderNames.Authorization] = headerValue;

        await handler.InitializeAsync(
            new AuthenticationScheme(
                TransportSurface.Client.SessionTokenSchemeName,
                displayName: null,
                typeof(ClientSessionTokenAuthenticationHandler)),
            context);

        return handler;
    }

    private sealed class StaticOptionsMonitor : IOptionsMonitor<ClientSessionTokenAuthenticationSchemeOptions>
    {
        internal StaticOptionsMonitor(ClientSessionTokenAuthenticationSchemeOptions options) => this.CurrentValue = options;

        public ClientSessionTokenAuthenticationSchemeOptions CurrentValue { get; }

        public ClientSessionTokenAuthenticationSchemeOptions Get(string? name) => this.CurrentValue;

        public IDisposable? OnChange(Action<ClientSessionTokenAuthenticationSchemeOptions, string?> listener) => null;
    }
}
