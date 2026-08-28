// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using MailFathom.Application.Access.Credentials;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Security.Basic;
using MailFathom.Host.Security.Transport;
using MailFathom.Infrastructure.Security.Passwords;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Net.Http.Headers;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Security.Basic;

/// <summary>Covers the one judgement the handler makes for itself, which is that a password may not arrive over clear text.</summary>
/// <remarks>
/// Everything else the handler does is <see cref="OwnerPasswordAuthenticator" />'s and is covered there. This is the
/// part that only exists at the request boundary: startup refuses a deployment whose surface answers its routes on an
/// unencrypted socket with nothing declared in front, and the arrangement it permits instead leaves that socket open
/// behind a named proxy — so a request arriving there from anywhere but the proxy carries no forwarded scheme and is
/// refused here, before the header is read.
/// </remarks>
public sealed class BasicAuthenticationHandlerTests
{
    private const int AttemptsPerMinute = 10;

    /// <summary>The password would already have crossed the wire, so nothing about it is read and nothing is spent judging it.</summary>
    [Fact]
    public async Task AuthenticateAsync_ACredentialArrivingOverClearText_IsNotJudgedAtAll()
    {
        // Arrange
        using var harness = new HandlerHarness();
        var handler = await harness.InitializeAsync(BasicHeader("owner", "correcthorsebattery"), https: false);

        // Act
        var result = await handler.AuthenticateAsync();

        // Assert
        Assert.True(result.None);
        Assert.Null(result.Failure);

        await harness.Credentials.DidNotReceiveWithAnyArgs()
            .FindByUsernameAsync(default, TestContext.Current.CancellationToken);
    }

    /// <summary>The same credential over an encrypted hop is judged, which is what says the refusal above is about the transport rather than about the header.</summary>
    [Fact]
    public async Task AuthenticateAsync_TheSameCredentialOverAnEncryptedHop_IsJudged()
    {
        // Arrange
        using var harness = new HandlerHarness();
        var handler = await harness.InitializeAsync(BasicHeader("owner", "correcthorsebattery"), https: true);

        // Act
        var result = await handler.AuthenticateAsync();

        // Assert
        Assert.False(result.None);

        await harness.Credentials.ReceivedWithAnyArgs(1)
            .FindByUsernameAsync(default, TestContext.Current.CancellationToken);
    }

    /// <summary>A challenge is an instruction to send the credential again, so a clear-text hop is not offered one it would send a password over.</summary>
    [Fact]
    public async Task ChallengeAsync_OverClearText_OffersNoPasswordChallenge()
    {
        // Arrange
        using var harness = new HandlerHarness();
        var context = new DefaultHttpContext();
        var handler = await harness.InitializeAsync(authorizationHeaderValue: string.Empty, https: false, context);

        // Act
        await handler.ChallengeAsync(new AuthenticationProperties());

        // Assert
        var challenges = context.Response.Headers[HeaderNames.WWWAuthenticate].ToString();
        Assert.DoesNotContain("Basic", challenges, StringComparison.Ordinal);
        Assert.Contains("Bearer", challenges, StringComparison.Ordinal);
    }

    /// <summary>Over an encrypted hop the password challenge is what tells a browser to ask, so it is written beside the bearer one.</summary>
    [Fact]
    public async Task ChallengeAsync_OverAnEncryptedHop_OffersThePasswordChallenge()
    {
        // Arrange
        using var harness = new HandlerHarness();
        var context = new DefaultHttpContext();
        var handler = await harness.InitializeAsync(authorizationHeaderValue: string.Empty, https: true, context);

        // Act
        await handler.ChallengeAsync(new AuthenticationProperties());

        // Assert
        var challenges = context.Response.Headers[HeaderNames.WWWAuthenticate].ToString();
        Assert.Contains("Basic", challenges, StringComparison.Ordinal);
        Assert.Contains("Bearer", challenges, StringComparison.Ordinal);
    }

    /// <summary>Nothing was declared in front, so the peer is the caller and one host guessing at many owners spends the allowance it is bounded by.</summary>
    [Fact]
    public async Task AuthenticateAsync_TwoUsernamesFromOnePeerWithNoProxyDeclared_SpendsThatPeersAllowance()
    {
        // Arrange
        using var harness = new HandlerHarness();

        // Act
        await harness.JudgeAsync("owner", peer: "203.0.113.7", reverseProxy: null);
        await harness.JudgeAsync("other", peer: "203.0.113.7", reverseProxy: null);

        // Assert
        await harness.Credentials.ReceivedWithAnyArgs(1)
            .FindByUsernameAsync(default, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Behind a declared proxy every request reports the proxy's own address, so bounding by it would be one bound for
    /// the whole world — and one guesser filling it would close password sign-in for every owner at once. The username
    /// is the whole bound there, which is why a second owner's request is judged rather than refused.
    /// </summary>
    [Fact]
    public async Task AuthenticateAsync_TwoUsernamesArrivingThroughOneDeclaredProxy_AreBoundedApart()
    {
        // Arrange
        using var harness = new HandlerHarness();
        var behindAProxy = new ReverseProxyOptions { TrustedProxies = { "10.0.0.1" } };

        // Act
        await harness.JudgeAsync("owner", peer: "10.0.0.1", behindAProxy);
        await harness.JudgeAsync("other", peer: "10.0.0.1", behindAProxy);

        // Assert
        await harness.Credentials.ReceivedWithAnyArgs(2)
            .FindByUsernameAsync(default, TestContext.Current.CancellationToken);
    }

    private static string BasicHeader(string userId, string password) =>
        "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{userId}:{password}"));

    /// <summary>Builds the handler over a real authenticator whose store holds nothing, because what is asserted is which requests reach it.</summary>
    private sealed class HandlerHarness : IDisposable
    {
        private readonly PasswordAttemptLimiter attemptLimiter = new(new FakeTimeProvider());

        internal HandlerHarness()
        {
            this.Credentials = Substitute.For<IOwnerPasswordCredentialStore>();
            this.Credentials.FindByUsernameAsync(Arg.Any<OwnerCredentialUsername>(), Arg.Any<CancellationToken>())
                .Returns((ResolvedOwnerPasswordCredential?)null);

            var passwordHasher = new UnreachablePasswordHasher();

            this.Authenticator = new OwnerPasswordAuthenticator(
                this.Credentials,
                passwordHasher,
                this.attemptLimiter,
                new DecoyPasswordHash(passwordHasher),
                NullLogger<OwnerPasswordAuthenticator>.Instance);
        }

        internal IOwnerPasswordCredentialStore Credentials { get; }

        private OwnerPasswordAuthenticator Authenticator { get; }

        public void Dispose() => this.attemptLimiter.Dispose();

        /// <summary>Runs one request from a stated peer through the handler, under the allowance of a single attempt a minute.</summary>
        /// <remarks>One attempt is what makes the bound observable: whether the second request reached the store says which axis bounded it.</remarks>
        internal async Task JudgeAsync(string username, string peer, ReverseProxyOptions? reverseProxy)
        {
            var context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = IPAddress.Parse(peer);

            var handler = await this.InitializeAsync(
                BasicHeader(username, "correcthorsebattery"),
                https: true,
                context,
                reverseProxy,
                attemptsPerMinute: 1);

            await handler.AuthenticateAsync();
        }

        internal Task<IAuthenticationHandler> InitializeAsync(string authorizationHeaderValue, bool https) =>
            this.InitializeAsync(authorizationHeaderValue, https, new DefaultHttpContext());

        internal async Task<IAuthenticationHandler> InitializeAsync(
            string authorizationHeaderValue,
            bool https,
            HttpContext context,
            ReverseProxyOptions? reverseProxy = null,
            int attemptsPerMinute = AttemptsPerMinute)
        {
            var handler = new BasicAuthenticationHandler(
                new StaticOptionsMonitor(new BasicAuthenticationSchemeOptions
                {
                    Surface = TransportSurface.Client,
                    Grant = [MailFathomPermission.MailRead],
                    AttemptsPerMinute = attemptsPerMinute,
                }),
                NullLoggerFactory.Instance,
                UrlEncoder.Default,
                this.Authenticator,
                Options.Create(reverseProxy ?? new ReverseProxyOptions()));

            context.Request.Scheme = https ? "https" : "http";
            context.Request.Headers[HeaderNames.Authorization] = authorizationHeaderValue;

            await handler.InitializeAsync(
                new AuthenticationScheme(
                    TransportSurface.Client.BasicSchemeName,
                    displayName: null,
                    typeof(BasicAuthenticationHandler)),
                context);

            return handler;
        }
    }

    /// <summary>Answers every comparison as a failure, so no test here spends a real derivation to establish which requests were judged.</summary>
    /// <remarks>Hand-written rather than substituted, because the members take the password as a <see cref="ReadOnlySpan{T}" /> and a dynamic proxy cannot carry a by-ref-like argument through its invocation.</remarks>
    private sealed class UnreachablePasswordHasher : IPasswordHasher
    {
        public string HashDecoy() => "$mf1$decoy$";

        public string Hash(ReadOnlySpan<char> password) => "$mf1$derived$";

        public PasswordVerification Verify(string storedHash, ReadOnlySpan<char> password) =>
            PasswordVerification.Failed;
    }

    /// <summary>Hands the handler the one options instance it is built with, which is all the framework's monitor does for a scheme nothing reconfigures.</summary>
    private sealed class StaticOptionsMonitor : IOptionsMonitor<BasicAuthenticationSchemeOptions>
    {
        internal StaticOptionsMonitor(BasicAuthenticationSchemeOptions schemeOptions) =>
            this.CurrentValue = schemeOptions;

        /// <inheritdoc />
        public BasicAuthenticationSchemeOptions CurrentValue { get; }

        /// <inheritdoc />
        public BasicAuthenticationSchemeOptions Get(string? name) => this.CurrentValue;

        /// <inheritdoc />
        public IDisposable? OnChange(Action<BasicAuthenticationSchemeOptions, string?> listener) => null;
    }
}
