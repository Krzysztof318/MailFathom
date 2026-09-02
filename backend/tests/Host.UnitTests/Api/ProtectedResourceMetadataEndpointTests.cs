// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Domain.Access;
using MailFathom.Host.Api;
using MailFathom.Host.Configuration.Access;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Security.Transport;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers the RFC 9728 document the administrative endpoint publishes for a client holding nothing yet.</summary>
/// <remarks>
/// Every field here is read by a client that cannot ask a second question: <c>mfctl login</c> takes the issuer, the
/// resource identifier, and the scopes from this document and goes straight to the authorization server. A swapped
/// source or a mistyped JSON name therefore compiles, serializes, and fails only against a real deployment, as a token
/// the endpoint refuses for a reason the client never sees. The names are asserted from the serialized form rather than
/// from the record's properties, because the wire names are what the specification fixes and what a client matches on.
/// </remarks>
public sealed class ProtectedResourceMetadataEndpointTests
{
    private const string Resource = "https://mail.example.test:8443/api/admin";

    [Fact]
    public void For_ConfiguredSettings_PublishesEachFieldFromItsOwnSource()
    {
        // Arrange
        var oauthSettings = Configured();

        // Act
        var document = ProtectedResourceMetadataDocument.For(
            PublishedOAuthMetadata.For(
                [new TransportAuthenticationOptions { OAuth = oauthSettings }],
                AdminEndpointOptions.GrantedSurface));

        // Assert
        Assert.Equal(Resource, document.Resource);
        Assert.Equal(["https://sso.example.test/realms/mailfathom"], document.AuthorizationServers);
        Assert.Equal(["mailfathom.admin", "mailfathom.read"], document.ScopesSupported);
        Assert.Equal(["header"], document.BearerMethodsSupported);
        Assert.Equal("MailFathom", document.ResourceName);
    }

    /// <summary>
    /// One document describes one resource, so several configured entries publish what all of them accept between
    /// them: every issuer a token may come from, and every scope any entry asks for. Reading only the first entry would
    /// under-publish the second — its clients would discover neither its authorization server nor the scope it requires,
    /// which is a sign-in that fails against a document that never mentioned them.
    /// </summary>
    [Fact]
    public void For_SeveralEntries_PublishesEveryIssuerAndEveryScopeBetweenThem()
    {
        // Arrange
        var partners = new OAuthValidationOptions { Resource = Resource };
        partners.RequiredScopes.Add("partners.read");

        // A scope both entries ask for, because the document lists what is supported rather than how often it was asked.
        partners.RequiredScopes.Add("mailfathom.read");
        partners.AuthorizationServers.Add(new AuthorizationServerOptions
        {
            Name = "partners",
            Issuer = "https://sso.partner.test/realms/mailfathom",
        });

        // Act
        var document = ProtectedResourceMetadataDocument.For(
            PublishedOAuthMetadata.For(
                [Entry(), new TransportAuthenticationOptions { OAuth = partners }],
                AdminEndpointOptions.GrantedSurface));

        // Assert
        Assert.Equal(Resource, document.Resource);
        Assert.Equal(
            ["https://sso.example.test/realms/mailfathom", "https://sso.partner.test/realms/mailfathom"],
            document.AuthorizationServers);
        Assert.Equal(["mailfathom.admin", "mailfathom.read", "partners.read"], document.ScopesSupported);
    }

    /// <summary>
    /// <c>mfctl</c> asks for exactly what this document lists and adds nothing to it, so a deployment that wants the
    /// sign-in to outlive its first access token says so here. The field is what a client should ask for rather than
    /// what a token is checked against, which is what RFC 9728 defines it as.
    /// </summary>
    [Fact]
    public void For_AnEntryAdvertisingOfflineAccess_PublishesItForTheClientToAskFor()
    {
        // Arrange
        var oauthSettings = Configured();
        oauthSettings.AdvertisedScopes.Add("offline_access");

        // Act
        var document = ProtectedResourceMetadataDocument.For(
            PublishedOAuthMetadata.For(
                [new TransportAuthenticationOptions { OAuth = oauthSettings }],
                AdminEndpointOptions.GrantedSurface));

        // Assert
        Assert.Equal(["mailfathom.admin", "mailfathom.read", "offline_access"], document.ScopesSupported);
    }

    /// <summary>A credential in a query string reaches every access log on the path, so the header is the only method offered.</summary>
    [Fact]
    public void For_AnySettings_OffersTheHeaderAsTheOnlyWayToPresentAToken()
    {
        // Act
        var document = ProtectedResourceMetadataDocument.For(
            PublishedOAuthMetadata.For([Entry()], AdminEndpointOptions.GrantedSurface));

        // Assert
        Assert.Equal(["header"], document.BearerMethodsSupported);
    }

    /// <summary>
    /// The document is composed against this endpoint's own half of the vocabulary, and it is the only place that
    /// argument is supplied. Composing it against the other half would tell an operator to create mail scopes in their
    /// authorization server for the administrative surface, and leave every token they then minted holding nothing.
    /// </summary>
    [Fact]
    public void For_AnEntryNarrowedByTokenScopes_PublishesTheAdministrativeHalfOfTheVocabulary()
    {
        // Arrange
        var entry = new TransportAuthenticationOptions { OAuth = Configured(), PermissionsFromTokenScopes = true };
        entry.GrantTheWholeSurface();

        // Act
        var document = ProtectedResourceMetadataDocument.For(
            PublishedOAuthMetadata.For([entry], AdminEndpointOptions.GrantedSurface));

        // Assert
        Assert.Equal(
            MailFathomPermission.PublishedFor(ProtectedSurface.Administration).Select(permission => permission.Name),
            document.ScopesSupported.Where(scope => scope.StartsWith("mailfathom.admin.", StringComparison.Ordinal)));
    }

    /// <summary>The names RFC 9728 fixes, which a client matches on and a rename inside this repository must not move.</summary>
    [Fact]
    public void Serialized_TheDocument_CarriesTheNamesRfc9728Defines()
    {
        // Arrange
        var document = ProtectedResourceMetadataDocument.For(
            PublishedOAuthMetadata.For([Entry()], AdminEndpointOptions.GrantedSurface));

        // Act
        using var serialized = JsonDocument.Parse(JsonSerializer.Serialize(document));

        // Assert
        Assert.Equal(
            [
                "authorization_servers",
                "bearer_methods_supported",
                "resource",
                "resource_name",
                "scopes_supported",
            ],
            serialized.RootElement.EnumerateObject().Select(field => field.Name).Order(StringComparer.Ordinal));

        Assert.Equal(Resource, serialized.RootElement.GetProperty("resource").GetString());
        Assert.Equal(
            "https://sso.example.test/realms/mailfathom",
            serialized.RootElement.GetProperty("authorization_servers")[0].GetString());
    }

    /// <summary>
    /// The document has to answer where its own resource identifier places it, because that is the address the client
    /// composes from the route prefix it is already calling rather than reading out of a challenge.
    /// </summary>
    [Fact]
    public void PathFor_TheConfiguredResource_IsWhereAClientComposingItFromTheRoutePrefixLooks()
    {
        // Act
        var path = ProtectedResourceMetadataAddress.PathFor(Configured().CanonicalResource());

        // Assert
        Assert.Equal("/.well-known/oauth-protected-resource/api/admin", path);
    }

    /// <summary>Wraps the configured OAuth block in the entry that carries it, which is the unit the document is composed from.</summary>
    private static TransportAuthenticationOptions Entry() => new() { OAuth = Configured() };

    private static OAuthValidationOptions Configured()
    {
        var oauthSettings = new OAuthValidationOptions { Resource = Resource };

        oauthSettings.RequiredScopes.Add("mailfathom.admin");
        oauthSettings.RequiredScopes.Add("mailfathom.read");
        oauthSettings.AuthorizationServers.Add(new AuthorizationServerOptions
        {
            Name = "corporate",
            Issuer = "https://sso.example.test/realms/mailfathom",
        });

        return oauthSettings;
    }
}
