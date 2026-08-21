// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MailFathom.AppHost;
using MailFathom.IntegrationTests.Orchestration;
using Xunit;

namespace MailFathom.IntegrationTests.Hosting;

/// <summary>Proves that the contact routes are served, are behind the administrative credential, and reach the book.</summary>
/// <remarks>
/// <para>
/// What only a composed host can establish is that these routes exist at all and inherit the group's requirement: the
/// unit suite maps the group and reads its metadata, which says nothing about a process that never bound the listener
/// or a route the composition dropped. The credential half is asserted the way
/// <see cref="ComposedAdminEndpointSecurityTests" /> asserts it for the surface, because a book of identified third
/// parties served to an unauthenticated caller is the failure this endpoint's placement exists to prevent.
/// </para>
/// <para>
/// The round trip is the second thing nothing else reaches. Recording a person, reading them back by identity and by
/// address, and erasing them exercises the store, the unique index over addresses, and the erasure's cascade through a
/// real database — and it leaves the book as it found it, which is what lets it share the schema with everything else in
/// the collection.
/// </para>
/// <para>
/// Nothing here carries <c>[RequiresIntegrationCoverage]</c>, for the reason the two security suites beside it state:
/// the classes exercised are either unit-covered already or belong to <c>Host</c>, which is outside the coverage
/// denominator.
/// </para>
/// </remarks>
[Collection(ComposedHostCollectionDefinition.Name)]
public sealed class ComposedContactEndpointTests
{
    /// <summary>The route the book is listed and recorded at, which is the cheapest thing the group answers here.</summary>
    private const string ContactsRoute = "/api/admin/contacts";

    private readonly MailFathomOrchestrationFixture orchestration;

    /// <summary>Initializes the tests against the assembly's orchestration.</summary>
    /// <param name="orchestration">The orchestration fixture, which starts the host on first request.</param>
    public ComposedContactEndpointTests(MailFathomOrchestrationFixture orchestration) =>
        this.orchestration = orchestration;

    /// <summary>
    /// A contact book is the most concentrated personal data this deployment holds, so the listing being refused without
    /// a credential is the claim this endpoint's placement rests on — and it is refused before any handler answers.
    /// </summary>
    [Fact]
    public async Task ContactRoutes_ARequestCarryingNoCredential_AreRefusedBeforeTheBookIsRead()
    {
        // Arrange
        using var client = await this.orchestration.OpenAdminEndpointClientAsync(TestContext.Current.CancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(ContactsRoute, UriKind.Relative));

        // Act
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", Assert.Single(response.Headers.WwwAuthenticate).Scheme);
        Assert.DoesNotContain(
            "contacts",
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The whole path, once: the route is served, the record reaches PostgreSQL, both lookups answer from it, and the
    /// erasure takes the person and their addresses away. One test rather than five, because each would otherwise pay
    /// for the same composition — and because what it establishes is that they work together.
    /// </summary>
    [Fact]
    public async Task ContactRoutes_ARecordedPerson_IsReadBackByIdentityAndAddressAndThenErased()
    {
        // Arrange
        using var client = await this.orchestration.OpenAdminEndpointClientAsync(TestContext.Current.CancellationToken);

        // An address of this test's own, so the book's uniqueness rule cannot collide with another class's contact.
        var address = $"composed-contact-{Guid.NewGuid():N}@example.test";

        // Act
        using var recorded = await this.SendAsync(
            client,
            HttpMethod.Post,
            ContactsRoute,
            JsonContent.Create(new
            {
                displayName = "Composed Endpoint Contact",
                addresses = new[] { address },
                preferredAddress = address,
                note = "Recorded by the composed-host suite.",
            }));

        Assert.Equal(HttpStatusCode.OK, recorded.StatusCode);

        using var written = JsonDocument.Parse(
            await recorded.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal("Written", written.RootElement.GetProperty("outcome").GetString());

        var identity = written.RootElement.GetProperty("contact").GetProperty("id").GetGuid();

        using var byIdentity = await this.SendAsync(client, HttpMethod.Get, $"{ContactsRoute}/{identity:D}");
        using var byAddress = await this.SendAsync(
            client,
            HttpMethod.Get,
            $"{ContactsRoute}/by-address?address={Uri.EscapeDataString(address.ToUpperInvariant())}");
        using var erased = await this.SendAsync(client, HttpMethod.Delete, $"{ContactsRoute}/{identity:D}");
        using var afterwards = await this.SendAsync(client, HttpMethod.Get, $"{ContactsRoute}/{identity:D}");

        // Assert
        Assert.Equal(identity, await ContactIdentityOf(byIdentity));

        // The lookup by address is asked in a casing the record was not written in, because two spellings of one address
        // are one address everywhere in the book — including in the index the lookup is served from.
        Assert.Equal(identity, await ContactIdentityOf(byAddress));

        using var erasure = JsonDocument.Parse(
            await erased.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.True(erasure.RootElement.GetProperty("wasHeld").GetBoolean());
        Assert.Equal(1, erasure.RootElement.GetProperty("addressesErased").GetInt32());

        using var gone = JsonDocument.Parse(
            await afterwards.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal(JsonValueKind.Null, gone.RootElement.GetProperty("contact").ValueKind);
    }

    /// <summary>Reads the identity a lookup answered with, failing the test where it answered with nobody.</summary>
    private static async Task<Guid> ContactIdentityOf(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var lookup = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        return lookup.RootElement.GetProperty("contact").GetProperty("id").GetGuid();
    }

    /// <summary>Sends one credentialed request to the administrative endpoint.</summary>
    private async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string route,
        HttpContent? content = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(route, UriKind.Relative)) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", OrchestrationContract.AdminApiKey);

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
