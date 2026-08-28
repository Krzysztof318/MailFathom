// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using System.Text.Encodings.Web;
using MailFathom.Application.Access.Credentials;
using MailFathom.Domain.Access;
using MailFathom.Host.Security.Basic;
using MailFathom.Host.Security.Transport;
using MailFathom.Infrastructure.Security.Passwords;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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

    private static string BasicHeader(string userId, string password) =>
        "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{userId}:{password}"));

    /// <summary>Builds the handler over a real authenticator whose store holds nothing, because what is asserted is which requests reach it.</summary>
    private sealed class HandlerHarness : IDisposable
    {
        private readonly PasswordAttemptLimiter attemptLimiter = new();

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

        internal async Task<IAuthenticationHandler> InitializeAsync(string authorizationHeaderValue, bool https)
        {
            var handler = new BasicAuthenticationHandler(
                new StaticOptionsMonitor(new BasicAuthenticationSchemeOptions
                {
                    Surface = TransportSurface.Client,
                    Grant = [MailFathomPermission.MailRead],
                    AttemptsPerMinute = AttemptsPerMinute,
                }),
                NullLoggerFactory.Instance,
                UrlEncoder.Default,
                this.Authenticator);

            var context = new DefaultHttpContext();
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
