// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Claims;
using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Host.Api;
using MailFathom.Host.Configuration.Access;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Security.ApiKeys;
using MailFathom.Host.Security.Transport;
using MailFathom.Mcp;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Security.Transport;

/// <summary>Covers the one adapter that turns what the transport established into what the application layer reads.</summary>
/// <remarks>
/// Three answers are asserted, one per principal kind the application layer models, and so is the fourth case that is
/// none of them: a request that authenticated nothing. That last one is the reason the adapter cannot simply report a
/// caller holding nothing — the download route is reached that way legitimately, and a principal appearing out of the
/// transport there would be a second and weaker way into an attachment.
/// </remarks>
public sealed class TransportAuthorizedPrincipalSourceTests
{
    private const string ConfiguredKeyName = "mcp-key";

    [Fact]
    public void Current_AnAuthenticatedRequest_ReportsTheCallerAndTheGrantItsEntryResolvedTo()
    {
        // Arrange
        var context = RequestBy(AuthenticatedCallerHolding(MailFathomPermission.MailRead));
        var source = SourceOver(context);

        // Act
        var principal = source.Current;

        // Assert
        Assert.NotNull(principal);
        Assert.Equal(AuthorizedPrincipalKind.Caller, principal.Kind);
        Assert.Equal(ConfiguredKeyName, principal.Identity);
        Assert.Equal([MailFathomPermission.MailRead], principal.Permissions);
    }

    /// <summary>An entry an operator emptied admits a caller that holds nothing, which is a caller rather than nobody.</summary>
    [Fact]
    public void Current_AnAuthenticatedRequestWhoseEntryGrantedNothing_ReportsACallerHoldingNothing()
    {
        // Arrange
        var context = RequestBy(AuthenticatedCallerHolding());
        var source = SourceOver(context);

        // Act
        var principal = source.Current;

        // Assert
        Assert.Equal(AuthorizedPrincipalKind.Caller, principal?.Kind);
        Assert.Empty(principal?.Permissions ?? new HashSet<MailFathomPermission>());
    }

    /// <summary>
    /// A request nothing authenticated, on a surface that configures a credential, is none of the three kinds.
    /// Reporting it as an anonymous caller would give the download route a principal it never verified, which is what
    /// its capability is for.
    /// </summary>
    [Fact]
    public void Current_ARequestThatAuthenticatedNothingWhereTheSurfaceConfiguresACredential_ReportsNoPrincipal()
    {
        // Arrange
        var source = SourceOver(RequestTo(McpEndpointRoute.Path), mcpConfiguresACredential: true);

        // Act & Assert
        Assert.Null(source.Current);
    }

    /// <summary>
    /// ADR 0012 leaves a surface with no <c>Authentication</c> entry granting its whole half, because there is no entry
    /// for a grant to hang on, and the startup report tells the operator exactly that. Reporting no principal here
    /// would have a use case refuse every call on a deployment whose own record says it grants everything.
    /// </summary>
    [Theory]
    [InlineData(McpEndpointRoute.Path, ProtectedSurface.Mail)]
    [InlineData(EmailAttachmentDownloadEndpoint.RoutePrefix + "/an-object", ProtectedSurface.Mail)]
    [InlineData(AdminEndpointOptions.RoutePrefix + "/session", ProtectedSurface.Administration)]
    public void Current_ARequestOnASurfaceConfiguringNoCredential_ReportsACallerHoldingThatWholeSurface(
        string path,
        ProtectedSurface surface)
    {
        // Arrange
        var source = SourceOver(RequestTo(path));

        // Act
        var principal = source.Current;

        // Assert
        Assert.Equal(AuthorizedPrincipalKind.Caller, principal?.Kind);
        Assert.Equal(
            MailFathomPermission.PublishedFor(surface).ToHashSet(),
            principal?.Permissions);
    }

    /// <summary>A path neither surface serves is nobody's, so the posture of either endpoint decides nothing about it.</summary>
    [Fact]
    public void Current_ARequestToAPathNeitherSurfaceServes_ReportsNoPrincipal()
    {
        // Arrange
        var source = SourceOver(RequestTo("/health"));

        // Act & Assert
        Assert.Null(source.Current);
    }

    /// <summary>
    /// Work reached outside a request in this process is work no caller asked for, which is exactly what the process
    /// identity names. Saying so is what lets a use case that runs without a caller admit it by name.
    /// </summary>
    [Fact]
    public void Current_NoRequestAtAll_ReportsTheProcessIdentity()
    {
        // Arrange
        var source = SourceOver(context: null);

        // Act & Assert
        Assert.Same(AuthorizedPrincipal.Process, source.Current);
    }

    /// <summary>A route that verified a capability states it for itself, and that statement is what the use case behind it reads.</summary>
    [Fact]
    public void Current_ACapabilityStatedByARoute_ReportsItOverWhateverTheTransportSaid()
    {
        // Arrange
        var source = SourceOver(RequestBy(AuthenticatedCallerHolding(MailFathomPermission.MailRead)));
        var capability = AuthorizedPrincipal.SignedCapability("/attachments/an-object/0");

        // Act
        source.Assume(capability);

        // Assert
        Assert.Same(capability, source.Current);
    }

    /// <summary>Composes the adapter over one request, and over endpoints that configure a credential or do not.</summary>
    /// <remarks>Both endpoints default to configuring none, which is the posture whose grant the tests above are about; a test that needs the ordinary posture says so.</remarks>
    private static TransportAuthorizedPrincipalSource SourceOver(
        HttpContext? context,
        bool mcpConfiguresACredential = false,
        bool adminConfiguresACredential = false)
    {
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        httpContextAccessor.HttpContext.Returns(context);

        var mcpEndpoint = new McpEndpointOptions();
        var adminEndpoint = new AdminEndpointOptions();
        if (mcpConfiguresACredential)
        {
            mcpEndpoint.Authentication.Add(new TransportAuthenticationOptions());
        }

        if (adminConfiguresACredential)
        {
            adminEndpoint.Authentication.Add(new TransportAuthenticationOptions());
        }

        return new TransportAuthorizedPrincipalSource(
            httpContextAccessor,
            Options.Create(mcpEndpoint),
            Options.Create(adminEndpoint));
    }

    private static DefaultHttpContext RequestBy(ClaimsPrincipal caller) => new() { User = caller };

    private static DefaultHttpContext RequestTo(string path) => new() { Request = { Path = path } };

    /// <summary>Composes the principal an API key scheme produces, which names the entry and carries the grant it resolved to.</summary>
    private static ClaimsPrincipal AuthenticatedCallerHolding(params MailFathomPermission[] granted) =>
        new(new ClaimsIdentity(
            [
                new Claim(ApiKeyAuthentication.ApiKeyNameClaimType, ConfiguredKeyName),
                .. TransportGrant.ClaimsFor(granted),
            ],
            "test",
            ApiKeyAuthentication.ApiKeyNameClaimType,
            ApiKeyAuthentication.RoleClaimType));
}
