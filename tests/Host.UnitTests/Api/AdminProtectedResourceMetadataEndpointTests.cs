// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Host.Api;
using MailFathom.Host.Configuration.Access;
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
public sealed class AdminProtectedResourceMetadataEndpointTests
{
    private const string Resource = "https://mail.example.test:8443/api/admin";

    [Fact]
    public void For_ConfiguredSettings_PublishesEachFieldFromItsOwnSource()
    {
        // Arrange
        var oauthSettings = Configured();

        // Act
        var document = ProtectedResourceMetadataDocument.For(oauthSettings);

        // Assert
        Assert.Equal(Resource, document.Resource);
        Assert.Equal(["https://sso.example.test/realms/mailfathom"], document.AuthorizationServers);
        Assert.Equal(["mailfathom.admin", "mailfathom.read"], document.ScopesSupported);
        Assert.Equal(["header"], document.BearerMethodsSupported);
        Assert.Equal("MailFathom", document.ResourceName);
    }

    /// <summary>A credential in a query string reaches every access log on the path, so the header is the only method offered.</summary>
    [Fact]
    public void For_AnySettings_OffersTheHeaderAsTheOnlyWayToPresentAToken()
    {
        // Act
        var document = ProtectedResourceMetadataDocument.For(Configured());

        // Assert
        Assert.Equal(["header"], document.BearerMethodsSupported);
    }

    /// <summary>The names RFC 9728 fixes, which a client matches on and a rename inside this repository must not move.</summary>
    [Fact]
    public void Serialized_TheDocument_CarriesTheNamesRfc9728Defines()
    {
        // Arrange
        var document = ProtectedResourceMetadataDocument.For(Configured());

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
