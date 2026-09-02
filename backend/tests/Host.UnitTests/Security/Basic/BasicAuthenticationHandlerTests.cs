// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Net;
using System.Security.Claims;
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

/// <summary>Covers what the handler decides for itself at the request boundary, and the one thing it deliberately does not.</summary>
/// <remarks>
/// Judging the credential is <see cref="OwnerPasswordAuthenticator" />'s and is covered there. What only exists here is
/// which source an attempt is bounded by, which claims a success carries — and that the transport a request arrived
/// over decides nothing: a password is read, and the challenge offered, on a clear-text hop exactly as on an encrypted
/// one, because this process can read the scheme of its own socket and nothing beyond it.
/// </remarks>
public sealed class BasicAuthenticationHandlerTests
{
    private const int AttemptsPerMinute = 10;

    private const string Password = "correcthorsebattery";

    private const string StoredHash = "$mf1$stored$";

    private static readonly Guid CredentialId = new("0197c0de-0000-7000-8000-000000000001");

    /// <summary>The owner the provisioned credential names, deliberately not the one a deployment is configured with.</summary>
    /// <remarks>A principal that carried the deployment's owner instead would be indistinguishable from one carrying none, which is the widening the success branch exists to prevent.</remarks>
    private static readonly MailOwnerId CredentialOwner =
        MailOwnerId.Create(new Guid("0197c0de-0000-7000-8000-00000000ffff"));

    /// <summary>
    /// The deployment this endpoint is served in is the administrator's to decide, and a Compose or loopback
    /// deployment speaking plain HTTP is the one the client's sign-in exists for. A credential arriving there is
    /// judged and authenticated rather than refused unread; that the hop is unencrypted is reported at startup by
    /// <c>PasswordClearTextTransportWarning</c> instead.
    /// </summary>
    [Fact]
    public async Task AuthenticateAsync_ACredentialArrivingOverClearText_IsAuthenticated()
    {
        // Arrange
        using var harness = new HandlerHarness();
        harness.HoldsTheOwnersCredential();
        var handler = await harness.InitializeAsync(BasicHeader("owner", Password), https: false);

        // Act
        var result = await handler.AuthenticateAsync();

        // Assert
        Assert.True(result.Succeeded);
        Assert.Equal(CredentialOwner, TransportCallerOwner.CarriedBy(result.Principal!));

        await harness.Credentials.ReceivedWithAnyArgs(1)
            .FindAsync(default, default, TestContext.Current.CancellationToken);
    }

    /// <summary>The same credential over an encrypted hop reaches the store and succeeds exactly as above, which is what says the transport is read for nothing here rather than read and permitted.</summary>
    [Fact]
    public async Task AuthenticateAsync_TheSameCredentialOverAnEncryptedHop_ReachesTheStoreAndIsAuthenticatedToo()
    {
        // Arrange
        using var harness = new HandlerHarness();
        harness.HoldsTheOwnersCredential();
        var handler = await harness.InitializeAsync(BasicHeader("owner", Password), https: true);

        // Act
        var result = await handler.AuthenticateAsync();

        // Assert
        Assert.True(result.Succeeded);

        await harness.Credentials.ReceivedWithAnyArgs(1)
            .FindAsync(default, default, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The credential named a person, so the principal carries them. Without that claim
    /// <c>TransportAuthorizedPrincipalSource</c> falls back to the deployment's own owner, and every request a password
    /// authenticated would act for the wrong person's mail on both mail-serving surfaces.
    /// </summary>
    [Fact]
    public async Task AuthenticateAsync_AProvisionedCredential_CarriesItsOwnersClaimRatherThanTheDeployments()
    {
        // Arrange
        using var harness = new HandlerHarness();
        harness.HoldsTheOwnersCredential();
        var handler = await harness.InitializeAsync(BasicHeader("owner", Password), https: true);

        // Act
        var result = await handler.AuthenticateAsync();

        // Assert
        Assert.True(result.Succeeded);
        Assert.Equal(CredentialOwner, TransportCallerOwner.CarriedBy(result.Principal!));
    }

    /// <summary>The grant is the row's rather than the endpoint's, so what a request may do travels with the credential.</summary>
    [Fact]
    public async Task AuthenticateAsync_AProvisionedCredential_CarriesTheGrantOnItsOwnRowRatherThanTheEndpoints()
    {
        // Arrange
        using var harness = new HandlerHarness();
        harness.HoldsTheOwnersCredential();
        var handler = await harness.InitializeAsync(BasicHeader("owner", Password), https: true);

        // Act
        var result = await handler.AuthenticateAsync();

        // Assert
        Assert.Equal(
            HandlerHarness.Grant.ToHashSet(),
            TransportGrant.PermissionsCarriedBy(result.Principal!));
    }

    /// <summary>The credential's identity is what the surface access policy admits the principal on, so the claim naming it is part of the success rather than a diagnostic.</summary>
    [Fact]
    public async Task AuthenticateAsync_AProvisionedCredential_NamesThatCredentialOnThePrincipal()
    {
        // Arrange
        using var harness = new HandlerHarness();
        harness.HoldsTheOwnersCredential();
        var handler = await harness.InitializeAsync(BasicHeader("owner", Password), https: true);

        // Act
        var result = await handler.AuthenticateAsync();

        // Assert
        Assert.Equal(
            CredentialId.ToString("D", CultureInfo.InvariantCulture),
            result.Principal!.FindFirstValue(BasicAuthentication.CredentialIdClaimType));
    }

    /// <summary>A surface that read a password over this hop and then declined to ask for one would be a surface no browser client could sign in to, so the challenge is the same one an encrypted hop gets.</summary>
    [Fact]
    public async Task ChallengeAsync_OverClearText_OffersThePasswordChallenge()
    {
        // Arrange
        using var harness = new HandlerHarness();
        var context = new DefaultHttpContext();
        var handler = await harness.InitializeAsync(authorizationHeaderValue: string.Empty, https: false, context);

        // Act
        await handler.ChallengeAsync(new AuthenticationProperties());

        // Assert
        var challenges = context.Response.Headers[HeaderNames.WWWAuthenticate].ToString();
        Assert.Contains("Basic", challenges, StringComparison.Ordinal);
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
            .FindAsync(default, default, TestContext.Current.CancellationToken);
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
            .FindAsync(default, default, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// <c>ReverseProxy</c> is one section for the whole process while a listener is not, so a deployment declaring a
    /// proxy for one surface may serve another directly. A peer arriving there is the real client and does tell two
    /// callers apart, so the source axis it would otherwise lose still applies.
    /// </summary>
    [Fact]
    public async Task AuthenticateAsync_TwoUsernamesFromAPeerThatIsNotTheDeclaredProxy_SpendsThatPeersAllowance()
    {
        // Arrange
        using var harness = new HandlerHarness();
        var behindAProxy = new ReverseProxyOptions { TrustedProxies = { "10.0.0.1" } };

        // Act
        await harness.JudgeAsync("owner", peer: "203.0.113.7", behindAProxy);
        await harness.JudgeAsync("other", peer: "203.0.113.7", behindAProxy);

        // Assert
        await harness.Credentials.ReceivedWithAnyArgs(1)
            .FindAsync(default, default, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A dual-stack listener reports an IPv4 proxy in its mapped form while the operator wrote the plain address, and
    /// neither comparison matches across address families. Reading the peer any other way would leave the proxy
    /// unrecognized as one, put every request in the deployment into a single per-source partition, and let ten wrong
    /// passwords a minute from anybody behind it close password sign-in for every owner.
    /// </summary>
    [Fact]
    public async Task AuthenticateAsync_TwoUsernamesArrivingThroughADeclaredProxyReportedAsIPv4Mapped_AreBoundedApart()
    {
        // Arrange
        using var harness = new HandlerHarness();
        var behindAProxy = new ReverseProxyOptions { TrustedProxies = { "10.0.0.1" } };

        // Act
        await harness.JudgeAsync("owner", peer: "::ffff:10.0.0.1", behindAProxy);
        await harness.JudgeAsync("other", peer: "::ffff:10.0.0.1", behindAProxy);

        // Assert
        await harness.Credentials.ReceivedWithAnyArgs(2)
            .FindAsync(default, default, TestContext.Current.CancellationToken);
    }

    private static string BasicHeader(string userId, string password) =>
        "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{userId}:{password}"));

    /// <summary>Builds the handler over a real authenticator whose store holds nothing, because what is asserted is which requests reach it.</summary>
    private sealed class HandlerHarness : IDisposable
    {
        /// <summary>The grant the stored credential carries, which is what the principal is admitted with.</summary>
        internal static readonly IReadOnlyList<MailFathomPermission> Grant = [MailFathomPermission.MailRead];

        private readonly PasswordAttemptLimiter attemptLimiter = new(new FakeTimeProvider());

        internal HandlerHarness()
        {
            this.Credentials = Substitute.For<IOwnerCredentialStore>();
            this.Credentials.FindAsync(
                    Arg.Any<OwnerCredentialMethod>(),
                    Arg.Any<OwnerCredentialLookup>(),
                    Arg.Any<CancellationToken>())
                .Returns((ResolvedOwnerCredential?)null);

            var passwordHasher = new UnreachablePasswordHasher();

            this.Authenticator = new OwnerPasswordAuthenticator(
                this.Credentials,
                passwordHasher,
                this.attemptLimiter,
                new DecoyPasswordHash(passwordHasher),
                NullLogger<OwnerPasswordAuthenticator>.Instance);
        }

        internal IOwnerCredentialStore Credentials { get; }

        /// <summary>Holds one enabled credential for <see cref="CredentialOwner" />, whose password the hasher recognizes.</summary>
        internal void HoldsTheOwnersCredential() =>
            this.Credentials.FindAsync(
                    OwnerCredentialMethod.Password,
                    Arg.Is<OwnerCredentialLookup>(lookup => lookup.Value == "owner"),
                    Arg.Any<CancellationToken>())
                .Returns(new ResolvedOwnerCredential(
                    CredentialId,
                    CredentialOwner,
                    OwnerCredentialMethod.Password,
                    Grant,
                    Enabled: true,
                    StoredHash));

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

        public string Hash(ReadOnlySpan<char> password) => StoredHash;

        public PasswordVerification Verify(string storedHash, ReadOnlySpan<char> password) =>
            string.Equals(storedHash, StoredHash, StringComparison.Ordinal) && password.SequenceEqual(Password)
                ? PasswordVerification.Succeeded
                : PasswordVerification.Failed;
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
