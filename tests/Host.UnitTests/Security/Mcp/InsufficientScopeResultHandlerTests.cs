// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using System.Text.Encodings.Web;
using MailFathom.Host.Security.Mcp;
using MailFathom.Infrastructure.Security.OAuth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace MailFathom.Host.UnitTests.Security.Mcp;

/// <summary>Covers what an authenticated caller is told when its token lacks a scope, and whose scopes it is told about.</summary>
/// <remarks>
/// Each configured OAuth entry states the scopes asked of the servers it configures, so the challenge is the place that
/// independence becomes visible to a client. Naming the union instead would send a client to ask its own authorization
/// server for a scope some other entry requires — something that server has no reason to issue, and no way to.
/// </remarks>
public sealed class InsufficientScopeResultHandlerTests
{
    private const string WorkforceIssuer = "https://sso.example.test/realms/mailfathom";

    private const string PartnerIssuer = "https://sso.partner.test/realms/mailfathom";

    private const string MetadataAddress =
        "https://mail.example.test/.well-known/oauth-protected-resource/mcp";

    private const string SchemeName = "test-scheme";

    private static readonly ServiceProvider FrameworkServices = BuildFrameworkServices();

    [Fact]
    public async Task HandleAsync_ATokenMissingItsOwnIssuersScope_NamesThatIssuersScopesAndNoOthers()
    {
        // Arrange
        var handler = HandlerForTwoIssuers();
        var context = ForbiddenRequestFrom(WorkforceIssuer);

        // Act
        await handler.HandleAsync(NothingFurther, context, RequirementPolicy, PolicyAuthorizationResult.Forbid());

        // Assert
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        var challenge = context.Response.Headers[HeaderNames.WWWAuthenticate].ToString();
        Assert.Contains("scope=\"mailfathom.read\"", challenge, StringComparison.Ordinal);
        Assert.DoesNotContain("partners.read", challenge, StringComparison.Ordinal);
        Assert.Contains($"resource_metadata=\"{MetadataAddress}\"", challenge, StringComparison.Ordinal);
    }

    /// <summary>The other half of the same guarantee: a caller from the second entry reads that entry's scopes rather than the first's.</summary>
    [Fact]
    public async Task HandleAsync_ATokenFromTheOtherIssuer_NamesTheOtherEntrysScopes()
    {
        // Arrange
        var handler = HandlerForTwoIssuers();
        var context = ForbiddenRequestFrom(PartnerIssuer);

        // Act
        await handler.HandleAsync(NothingFurther, context, RequirementPolicy, PolicyAuthorizationResult.Forbid());

        // Assert
        var challenge = context.Response.Headers[HeaderNames.WWWAuthenticate].ToString();
        Assert.Contains("scope=\"partners.read\"", challenge, StringComparison.Ordinal);
        Assert.DoesNotContain("mailfathom.read", challenge, StringComparison.Ordinal);
    }

    /// <summary>
    /// An entry asking for no scope can refuse a caller only for who they are, and naming scopes there would send a
    /// client to ask its authorization server for something that would change nothing.
    /// </summary>
    [Fact]
    public async Task HandleAsync_ATokenFromAnIssuerWhoseEntryRequiresNoScope_WritesNoChallenge()
    {
        // Arrange
        var handler = new InsufficientScopeResultHandler(
            RequiredScopes((WorkforceIssuer, []), (PartnerIssuer, ["partners.read"])),
            MetadataAddress);
        var context = ForbiddenRequestFrom(WorkforceIssuer);

        // Act
        await handler.HandleAsync(NothingFurther, context, RequirementPolicy, PolicyAuthorizationResult.Forbid());

        // Assert
        Assert.False(context.Response.Headers.ContainsKey(HeaderNames.WWWAuthenticate));
    }

    /// <summary>A caller carrying every scope its own entry asks for was refused for who they are, which the framework's plain refusal states.</summary>
    [Fact]
    public async Task HandleAsync_ATokenCarryingEveryScopeItsIssuerRequires_WritesNoChallenge()
    {
        // Arrange
        var handler = HandlerForTwoIssuers();
        var context = ForbiddenRequestFrom(WorkforceIssuer, "mailfathom.read");

        // Act
        await handler.HandleAsync(NothingFurther, context, RequirementPolicy, PolicyAuthorizationResult.Forbid());

        // Assert
        Assert.False(context.Response.Headers.ContainsKey(HeaderNames.WWWAuthenticate));
    }

    /// <summary>
    /// No validated token can carry an issuer no entry configures, because only a configured issuer has a validator at
    /// all. It falls through rather than being answered with somebody else's scopes, which is the reading that would
    /// turn a future gap into a challenge naming a server the caller never spoke to.
    /// </summary>
    [Fact]
    public async Task HandleAsync_ATokenFromAnIssuerNoEntryConfigures_WritesNoChallenge()
    {
        // Arrange
        var handler = HandlerForTwoIssuers();
        var context = ForbiddenRequestFrom("https://sso.unconfigured.test/realms/mailfathom");

        // Act
        await handler.HandleAsync(NothingFurther, context, RequirementPolicy, PolicyAuthorizationResult.Forbid());

        // Assert
        Assert.False(context.Response.Headers.ContainsKey(HeaderNames.WWWAuthenticate));
    }

    /// <summary>A caller with no usable credential is told to authenticate, which is a different refusal and not this handler's to write.</summary>
    [Fact]
    public async Task HandleAsync_AChallengeRatherThanARefusal_WritesNoInsufficientScopeChallenge()
    {
        // Arrange
        var handler = HandlerForTwoIssuers();
        var context = ForbiddenRequestFrom(WorkforceIssuer);

        // Act
        await handler.HandleAsync(NothingFurther, context, RequirementPolicy, PolicyAuthorizationResult.Challenge());

        // Assert
        Assert.False(context.Response.Headers.ContainsKey(HeaderNames.WWWAuthenticate));
    }

    private static ServiceProvider BuildFrameworkServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services
            .AddAuthentication(SchemeName)
            .AddScheme<AuthenticationSchemeOptions, RefusingScheme>(SchemeName, configureOptions: null);
        services.AddAuthorization();

        return services.BuildServiceProvider();
    }

    /// <summary>Two entries asking for different scopes, which is what the per-issuer lookup exists for.</summary>
    private static InsufficientScopeResultHandler HandlerForTwoIssuers() =>
        new(
            RequiredScopes((WorkforceIssuer, ["mailfathom.read"]), (PartnerIssuer, ["partners.read"])),
            MetadataAddress);

    private static Dictionary<string, IReadOnlyCollection<string>> RequiredScopes(
        params (string Issuer, string[] Scopes)[] entries) =>
        entries.ToDictionary(entry => entry.Issuer, entry => (IReadOnlyCollection<string>)entry.Scopes, StringComparer.Ordinal);

    /// <summary>
    /// A request whose principal is what a validated token actually produces, mapped by the same code the runtime uses.
    /// </summary>
    /// <remarks>
    /// It carries a service provider because falling through is the behavior half of these tests assert, and the
    /// framework's own handler answers a refusal by reaching for the authentication service. Without one, a test proving
    /// that this handler stayed out of the way would fail inside the handler it deferred to.
    /// </remarks>
    private static DefaultHttpContext ForbiddenRequestFrom(string issuer, params string[] grantedScopes)
    {
        Claim[] tokenClaims =
        [
            new("iss", issuer),
            new("sub", "9f2c7c1e-8a4d-4c62-9f0b-3d2a1b5e7c04"),
            .. grantedScopes.Select(scope => new Claim("scope", scope)),
        ];

        var identity = OAuthIdentity.FromValidatedToken(tokenClaims, SchemeName);

        return new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity!),
            RequestServices = FrameworkServices,
        };
    }

    private static AuthorizationPolicy RequirementPolicy =>
        new([new DenyAnonymousAuthorizationRequirement()], [SchemeName]);

    private static Task NothingFurther(HttpContext context) => Task.CompletedTask;

    /// <summary>The scheme the identity records and the policy names, which has to be one the container actually registered.</summary>
    [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The authentication scheme registration materializes this handler when the framework answers a refusal.")]
    private sealed class RefusingScheme(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
            Task.FromResult(AuthenticateResult.NoResult());
    }
}
