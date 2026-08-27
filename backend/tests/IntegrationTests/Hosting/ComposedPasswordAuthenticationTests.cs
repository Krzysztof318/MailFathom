// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Text;
using MailFathom.Application.Access.Credentials;
using MailFathom.Domain.Access;
using MailFathom.Host.Security.Transport;
using MailFathom.IntegrationTests.Orchestration;
using MailFathom.Mcp;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Net.Http.Headers;
using NSubstitute;
using Xunit;

namespace MailFathom.IntegrationTests.Hosting;

/// <summary>Proves what a started host does with an owner's username and password on the surfaces that accept one.</summary>
/// <remarks>
/// <para>
/// Which passwords verify, how a rejection is collapsed, and how the header is decoded are unit covered and none of it
/// is repeated here. What only a started host establishes is that the method exists in the assembled pipeline at all:
/// that a surface configuring a Basic block registers a scheme of its own, that a request naming the scheme reaches
/// that surface's handler rather than the key comparison, that a refusal is answered with the challenge a browser needs
/// in order to ask for a password, and that one surface's password handler is never offered another surface's request.
/// </para>
/// <para>
/// The credential store is replaced after the composition and before the container is built, because everything else in
/// this class is about the pipeline: the real store reaches a database this host does not have, so leaving it in would
/// turn every one of these claims into a connection failure. Nothing else is substituted — the hasher, the limiter, the
/// handler, the scheme registration, and the challenge are all the deployment's own.
/// </para>
/// <para>
/// It joins the composed-host collection for that collection's ordering rather than for its fixture, exactly as
/// <see cref="ComposedClientEndpointSecurityTests" /> does. Nothing here carries <c>[RequiresIntegrationCoverage]</c>,
/// because the classes it exercises belong to <c>Host</c>, which is outside the coverage denominator.
/// </para>
/// </remarks>
[Collection(ComposedHostCollectionDefinition.Name)]
public sealed class ComposedPasswordAuthenticationTests
{
    private const int McpPort = 8080;

    private const int AdminPort = 8082;

    private const int ClientPort = 8084;

    private const string ClientSessionRoute = "/api/client/session";

    private const string AdminSessionRoute = "/api/admin/session";

    private const string AdminKeyName = "operator";

    private const string AdminKey = "not-a-real-admin-key";

    private const string Username = "owner";

    private const string Password = "correcthorsebatterystaple";

    private const string StoredHash = "$mf1$stored$";

    private static readonly Guid CredentialId = new("55555555-5555-5555-5555-555555555555");

    private static readonly MailOwnerId Owner =
        MailOwnerId.Create(new Guid("11111111-1111-1111-1111-111111111111"));

    /// <summary>A password authenticates through the assembled pipeline and the route answers, which is what says the method is wired rather than merely registered.</summary>
    [Fact]
    public async Task ClientEndpoint_ARequestPresentingAProvisionedPassword_ReachesTheSessionHandler()
    {
        // Arrange
        await using var host = await StartAsync(BothOwnerSurfacesAcceptingAPassword());

        // Act
        var response = await host.SendAsync(
            HttpMethods.Get,
            ClientSessionRoute,
            ClientPort,
            (HeaderNames.Authorization, Credential(Username, Password)));

        // Assert
        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
    }

    /// <summary>The scheme is selected by the word the request wrote, so a password reaches the handler that understands passwords rather than the key comparison.</summary>
    [Fact]
    public async Task ClientEndpoint_ARequestNamingTheBasicScheme_ReachesThatSurfacesPasswordHandler()
    {
        // Arrange
        await using var host = await StartAsync(BothOwnerSurfacesAcceptingAPassword());

        // Act
        await host.SendAsync(
            HttpMethods.Get,
            ClientSessionRoute,
            ClientPort,
            (HeaderNames.Authorization, Credential(Username, Password)));

        // Assert
        Assert.Contains(TransportSurface.Client.BasicSchemeName, host.AuthenticatedSchemes.Asked);
    }

    /// <summary>One surface's password handler is never offered another surface's request, which is the separation read from inside the pipeline.</summary>
    [Fact]
    public async Task ClientEndpoint_ARequestOnItsOwnListener_ReachesNoOtherSurfacesPasswordHandler()
    {
        // Arrange
        await using var host = await StartAsync(BothOwnerSurfacesAcceptingAPassword());

        // Act
        await host.SendAsync(
            HttpMethods.Get,
            ClientSessionRoute,
            ClientPort,
            (HeaderNames.Authorization, Credential(Username, Password)));

        // Assert
        Assert.DoesNotContain(TransportSurface.Mcp.BasicSchemeName, host.AuthenticatedSchemes.Asked);
    }

    /// <summary>The agent surface accepts a password on the same terms, and the request reaches its own handler rather than the client's.</summary>
    [Fact]
    public async Task McpEndpoint_ARequestNamingTheBasicScheme_ReachesItsOwnPasswordHandler()
    {
        // Arrange
        await using var host = await StartAsync(BothOwnerSurfacesAcceptingAPassword());

        // Act
        await host.SendAsync(
            HttpMethods.Post,
            McpEndpointRoute.Path,
            McpPort,
            (HeaderNames.Authorization, Credential(Username, Password)));

        // Assert
        Assert.Contains(TransportSurface.Mcp.BasicSchemeName, host.AuthenticatedSchemes.Asked);
        Assert.DoesNotContain(TransportSurface.Client.BasicSchemeName, host.AuthenticatedSchemes.Asked);
    }

    /// <summary>
    /// The challenge is the whole reason a browser ever sends a password, so a refusal that carried only the bearer half
    /// would leave the method unusable from the client this surface exists for — and nothing in it says which half of
    /// the credential was wrong.
    /// </summary>
    [Fact]
    public async Task ClientEndpoint_ARequestPresentingAWrongPassword_IsRefusedWithBothChallenges()
    {
        // Arrange
        await using var host = await StartAsync(BothOwnerSurfacesAcceptingAPassword());

        // Act
        var response = await host.SendAsync(
            HttpMethods.Get,
            ClientSessionRoute,
            ClientPort,
            (HeaderNames.Authorization, Credential(Username, "not-the-password")));

        // Assert
        Assert.Equal(StatusCodes.Status401Unauthorized, response.StatusCode);
        Assert.Equal(
            ["Bearer realm=\"MailFathom\"", "Basic realm=\"MailFathom\", charset=\"UTF-8\""],
            response.Headers[HeaderNames.WWWAuthenticate].Select(static value => value ?? string.Empty));
    }

    /// <summary>An unknown username is refused exactly as a wrong password is, from the pipeline's side as well as from the authenticator's.</summary>
    [Fact]
    public async Task ClientEndpoint_ARequestPresentingAnUnknownUsername_IsRefusedIndistinguishably()
    {
        // Arrange
        await using var host = await StartAsync(BothOwnerSurfacesAcceptingAPassword());

        // Act
        var unknown = await host.SendAsync(
            HttpMethods.Get,
            ClientSessionRoute,
            ClientPort,
            (HeaderNames.Authorization, Credential("nobody", Password)));

        var wrong = await host.SendAsync(
            HttpMethods.Get,
            ClientSessionRoute,
            ClientPort,
            (HeaderNames.Authorization, Credential(Username, "not-the-password")));

        // Assert
        Assert.Equal(unknown.StatusCode, wrong.StatusCode);
        Assert.Equal(
            unknown.Headers[HeaderNames.WWWAuthenticate].ToString(),
            wrong.Headers[HeaderNames.WWWAuthenticate].ToString());
    }

    /// <summary>
    /// The administrative surface answers for the deployment rather than for a person, so a password admitted there
    /// would carry an owner it has no use for. Refusing the shape at startup is what makes that unreachable rather than
    /// merely unintended, and what a deployment meets is a start that stopped.
    /// </summary>
    [Fact]
    public async Task AdminEndpoint_ADeploymentConfiguringAPasswordOnIt_DoesNotStart()
    {
        // Arrange
        IReadOnlyList<KeyValuePair<string, string?>> shape =
        [
            new("AdminEndpoint:Enabled", "true"),
            new("AdminEndpoint:Port", AdminPort.ToString(CultureInfo.InvariantCulture)),
            new("AdminEndpoint:Authentication:0:Basic:AttemptsPerMinute", "10"),
            new("ReverseProxy:TrustedProxies:0", "10.0.0.5"),
        ];

        // Act, Assert
        await Assert.ThrowsAnyAsync<Exception>(() => StartAsync(shape));
    }

    /// <summary>A surface that configured no password serves none, so a header naming the scheme is judged by the methods it did configure.</summary>
    [Fact]
    public async Task AdminEndpoint_ARequestPresentingAPassword_ReachesNoPasswordHandlerAtAll()
    {
        // Arrange
        await using var host = await StartAsync(BothOwnerSurfacesAcceptingAPassword());

        // Act
        var response = await host.SendAsync(
            HttpMethods.Get,
            AdminSessionRoute,
            AdminPort,
            (HeaderNames.Authorization, Credential(Username, Password)));

        // Assert
        Assert.Equal(StatusCodes.Status401Unauthorized, response.StatusCode);
        Assert.DoesNotContain(
            host.AuthenticatedSchemes.Asked,
            static scheme => scheme.EndsWith(":Basic", StringComparison.Ordinal));
    }

    /// <summary>Composes one shape over a credential store this process holds, because the real one reaches a database this host has not got.</summary>
    private static Task<InProcessComposedHost> StartAsync(IReadOnlyList<KeyValuePair<string, string?>> shape) =>
        InProcessComposedHost.StartAsync(
            shape,
            TestContext.Current.CancellationToken,
            static builder =>
            {
                builder.Services.RemoveAll<IOwnerPasswordCredentialStore>();
                builder.Services.AddScoped(static _ => OneProvisionedCredential());
                builder.Services.AddSingleton<IPasswordHasher>(new OneKnownPasswordHasher());
            });

    /// <summary>The store as a deployment holding exactly one enabled credential answers.</summary>
    private static IOwnerPasswordCredentialStore OneProvisionedCredential()
    {
        var credentials = Substitute.For<IOwnerPasswordCredentialStore>();

        credentials.FindByUsernameAsync(Arg.Any<OwnerCredentialUsername>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<OwnerCredentialUsername>() == OwnerCredentialUsername.Create(Username)
                ? new ResolvedOwnerPasswordCredential(CredentialId, Owner, Enabled: true, StoredHash)
                : null);

        return credentials;
    }

    /// <summary>Writes the header a client composes for a username and a password, as RFC 7617 describes it.</summary>
    private static string Credential(string userId, string password) =>
        $"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes($"{userId}:{password}"))}";

    /// <summary>Both surfaces an owner signs in to, each accepting a password, behind a proxy the deployment named.</summary>
    /// <remarks>The proxy is what satisfies the confidentiality refusal without this shape having to carry a certificate: a password may not cross a hop the deployment has not established as encrypted, and naming what stands in front is one of the two arrangements that establishes it.</remarks>
    private static IReadOnlyList<KeyValuePair<string, string?>> BothOwnerSurfacesAcceptingAPassword() =>
    [
        new("McpEndpoint:Enabled", "true"),
        new("McpEndpoint:Authentication:0:Basic:AttemptsPerMinute", "60"),
        new("AdminEndpoint:Enabled", "true"),
        new("AdminEndpoint:Port", AdminPort.ToString(CultureInfo.InvariantCulture)),
        new("AdminEndpoint:Authentication:0:ApiKey:Name", AdminKeyName),
        new("AdminEndpoint:Authentication:0:ApiKey:SecretReference", $"plaintext:{AdminKey}"),
        new("ClientEndpoint:Enabled", "true"),
        new("ClientEndpoint:Port", ClientPort.ToString(CultureInfo.InvariantCulture)),
        new("ClientEndpoint:Authentication:0:Basic:AttemptsPerMinute", "60"),
        new("ReverseProxy:TrustedProxies:0", "127.0.0.1"),
    ];

    /// <summary>Recognizes exactly the one password this class provisioned, without deriving anything.</summary>
    /// <remarks>
    /// Hand-written rather than substituted, because the members take the password as a <see cref="ReadOnlySpan{T}" />
    /// and a dynamic proxy cannot carry a by-ref-like argument through its invocation. It stands in for the real hasher
    /// so that a pipeline claim costs no key derivations; what the real one computes is covered where it lives.
    /// </remarks>
    private sealed class OneKnownPasswordHasher : IPasswordHasher
    {
        public string Hash(ReadOnlySpan<char> password) => StoredHash;

        public PasswordVerification Verify(string storedHash, ReadOnlySpan<char> password) =>
            string.Equals(storedHash, StoredHash, StringComparison.Ordinal) && password.SequenceEqual(Password)
                ? PasswordVerification.Succeeded
                : PasswordVerification.Failed;
    }
}
