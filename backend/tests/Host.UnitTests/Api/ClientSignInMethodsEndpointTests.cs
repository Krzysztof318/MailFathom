// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Host.Api;
using MailFathom.Host.Configuration.Access;
using MailFathom.Host.UnitTests.TestDoubles;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers the document a sign-in screen is composed from, which answers a browser holding nothing yet.</summary>
/// <remarks>
/// A client built against this cannot ask a second question: what it reads here decides whether a password form is
/// drawn, what words each provider control carries, and the identifier every authorization request starts with. So the
/// route it answers on and the names on the wire are asserted rather than assumed — both are matched by a client that
/// compiles against neither.
/// </remarks>
public sealed class ClientSignInMethodsEndpointTests
{
    private const string Keycloak = "https://sso.example.test/realms/mailfathom";

    /// <summary>Beneath the client prefix, which is what confines it to the client listeners: surface isolation reads the path.</summary>
    [Fact]
    public void MapClientSignInMethods_AnywhereItIsMapped_AnswersBeneathTheClientPrefix()
    {
        // Arrange
        var endpoints = BuildRouteBuilder();

        // Act
        endpoints.MapClientSignInMethods(new PublishedSignInMethods(AcceptsPassword: true, []));

        // Assert
        var mapped = Assert.Single(endpoints.Materialize()) as RouteEndpoint;
        Assert.Equal("api/client/sign-in-methods", mapped?.RoutePattern.RawText?.TrimStart('/'));
        Assert.Equal(
            ["GET"],
            mapped?.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods);
    }

    /// <summary>The names a client matches on, which the web defaults fix and a rename inside this repository must not move.</summary>
    [Fact]
    public void Serialized_TheDocument_CarriesTheNamesAClientReads()
    {
        // Arrange
        var published = new ClientSignInMethodsResponse(
            AcceptsPassword: false,
            [ClientSignInMethodResponse.For(new PublishedSignInMethod(Keycloak, "keycloak", "Keycloak", "mailfathom-client"))]);

        // Act
        using var serialized = JsonDocument.Parse(JsonSerializer.Serialize(published, JsonSerializerOptions.Web));

        // Assert
        Assert.Equal(
            ["acceptsPassword", "authorizationServers"],
            serialized.RootElement.EnumerateObject().Select(field => field.Name).Order(StringComparer.Ordinal));

        var server = serialized.RootElement.GetProperty("authorizationServers")[0];

        Assert.False(serialized.RootElement.GetProperty("acceptsPassword").GetBoolean());
        Assert.Equal(
            ["clientId", "displayName", "issuer", "name"],
            server.EnumerateObject().Select(field => field.Name).Order(StringComparer.Ordinal));
        Assert.Equal(Keycloak, server.GetProperty("issuer").GetString());
        Assert.Equal("mailfathom-client", server.GetProperty("clientId").GetString());
    }

    /// <summary>Nothing in the document is a secret, which is what lets it answer a caller holding nothing.</summary>
    [Fact]
    public void For_APublishedServer_DescribesItAsThePublicNamesItAlreadyIs()
    {
        // Act
        var described = ClientSignInMethodResponse.For(
            new PublishedSignInMethod(Keycloak, AuthorizationServerOptions.SelfName, "Nordwind", "mailfathom-client"));

        // Assert
        Assert.Equal(
            new ClientSignInMethodResponse(Keycloak, AuthorizationServerOptions.SelfName, "Nordwind", "mailfathom-client"),
            described);
    }

    private static TestEndpointRouteBuilder BuildRouteBuilder()
    {
        var services = new ServiceCollection();
        services.AddRouting();
        services.AddLogging();

        return new TestEndpointRouteBuilder(services.BuildServiceProvider());
    }
}
