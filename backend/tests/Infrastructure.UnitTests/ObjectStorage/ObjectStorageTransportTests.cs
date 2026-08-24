// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using MailFathom.Infrastructure.Certificates;
using MailFathom.Infrastructure.ObjectStorage;
using MailFathom.Infrastructure.Secrets.References;
using MailFathom.Infrastructure.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.ObjectStorage;

/// <summary>Covers the bounds every request to the object-storage endpoint travels under.</summary>
/// <remarks>
/// <para>
/// The removal of the standard resilience handler is order-dependent, which is the whole reason it is asserted rather
/// than read: it takes out what was registered before it, so it holds only while the composition root adds the service
/// defaults ahead of this registration. Nothing in either file fails if that order is swapped; this does.
/// </para>
/// <para>
/// Every call already runs under the <c>ObjectStorageInvocation</c> pipeline, so a handler kept here would put three
/// attempts inside three against an endpoint that is already refusing.
/// </para>
/// </remarks>
public sealed class ObjectStorageTransportTests
{
    private static readonly Uri ObjectAddress = new("https://objects.example.test:9000/payloads/probe");

    private static readonly ObjectStorageEndpoint Endpoint = ObjectStorageEndpoint.Create(
        new Uri("https://objects.example.test:9000/"),
        "payloads",
        "mailfathom",
        "eu-central-1",
        usePathStyleAddressing: true,
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(45));

    private static readonly ContentObjectReclamationBounds ReclamationBounds =
        ContentObjectReclamationBounds.Create(TimeSpan.FromHours(24), maximumObjectsPerRun: 100_000);

    /// <summary>The transport carries no retry of its own, so a refusal is one request rather than a burst of them.</summary>
    [Fact]
    public async Task ObjectStorageTransport_ComposedUnderTheServiceDefaults_ReachesARefusingEndpointOnce()
    {
        // Arrange
        using var endpointServer = FakeHttpMessageHandler.AlwaysResponding(
            () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        await using var provider = ComposedServices(endpointServer);
        using var transport = provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(ObjectStorageEndpoint.TransportName);

        // Act
        using var response = await transport.GetAsync(ObjectAddress, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Single(endpointServer.RecordedRequests);
    }

    /// <summary>The defaults the test composes are live, so the assertion above is a removal rather than a handler that was never added.</summary>
    [Fact]
    public async Task AnOrdinaryClient_ComposedUnderTheSameDefaults_RetriesARefusal()
    {
        // Arrange
        using var refusingServer = FakeHttpMessageHandler.AlwaysResponding(
            () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var services = ComposedServiceCollection();
        services.AddHttpClient("ordinary").ConfigurePrimaryHttpMessageHandler(() => refusingServer);

        await using var provider = services.BuildServiceProvider();
        using var transport = provider.GetRequiredService<IHttpClientFactory>().CreateClient("ordinary");

        // Act
        using var response = await transport.GetAsync(ObjectAddress, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(refusingServer.RecordedRequests.Count > 1);
    }

    /// <summary>
    /// The whole-request budget lives in the registration and nowhere else, because a client rejects a property change
    /// once a request has gone through it and the failure would surface at the second caller.
    /// </summary>
    [Fact]
    public async Task ObjectStorageTransport_TheNamedClient_CarriesTheConfiguredRequestBudget()
    {
        // Arrange
        using var endpointServer = FakeHttpMessageHandler.AlwaysResponding(
            () => new HttpResponseMessage(HttpStatusCode.OK));

        await using var provider = ComposedServices(endpointServer);

        // Act
        using var transport = provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(ObjectStorageEndpoint.TransportName);

        // Assert
        Assert.Equal(Endpoint.RequestTimeout, transport.Timeout);

        // No base address: the AWS client composes the whole address itself from the configured endpoint and the
        // addressing style, and one set here would be silently ignored on every request it makes.
        Assert.Null(transport.BaseAddress);
    }

    /// <summary>
    /// A moved endpoint answering with a redirect would carry a signed request, and on a write the message itself, to
    /// whatever host it named.
    /// </summary>
    [Fact]
    public async Task ObjectStorageTransport_AnEndpointAnsweringWithARedirect_IsNotFollowed()
    {
        // Arrange
        using var endpointServer = FakeHttpMessageHandler.AlwaysResponding(() =>
        {
            var redirect = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect);
            redirect.Headers.Location = new Uri("https://elsewhere.example.test/payloads/probe");

            return redirect;
        });

        await using var provider = ComposedServices(endpointServer);
        using var transport = provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(ObjectStorageEndpoint.TransportName);

        // Act
        using var response = await transport.GetAsync(ObjectAddress, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
        Assert.Single(endpointServer.RecordedRequests);
    }

    [Fact]
    public void AddObjectStorage_WithoutItsInputs_IsRefused()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act, Assert
        Assert.Throws<ArgumentNullException>(
            () => ObjectStorageServiceCollectionExtensions.AddObjectStorage(
                null!,
                Endpoint,
                ReclamationBounds,
                configuredTrustAnchor: null));
        Assert.Throws<ArgumentNullException>(
            () => services.AddObjectStorage(endpoint: null!, ReclamationBounds, configuredTrustAnchor: null));
        Assert.Throws<ArgumentNullException>(
            () => services.AddObjectStorage(Endpoint, reclamationBounds: null!, configuredTrustAnchor: null));
    }

    private static ServiceProvider ComposedServices(FakeHttpMessageHandler endpointServer)
    {
        var services = ComposedServiceCollection();

        // The registration's own primary handler opens a socket, so the test replaces it. Assignment rather than
        // appending is what lets it: the same call in the registration sets the handler that this one then replaces.
        services.AddHttpClient(ObjectStorageEndpoint.TransportName)
            .ConfigurePrimaryHttpMessageHandler(() => endpointServer);

        return services.BuildServiceProvider();
    }

    /// <summary>Composes the two registrations whose order decides the outcome, as the host composes them.</summary>
    /// <remarks>
    /// One deviation from the host, and it is timing rather than structure: the retry delay is removed so that a client
    /// still carrying the handler is observed retrying in milliseconds rather than over the better part of a minute.
    /// </remarks>
    private static ServiceCollection ComposedServiceCollection()
    {
        var services = new ServiceCollection();

        services.ConfigureHttpClientDefaults(builder => builder
            .AddStandardResilienceHandler()
            .Configure(static options =>
            {
                options.Retry.Delay = TimeSpan.Zero;
                options.Retry.UseJitter = false;
            }));

        services.AddSingleton<ISecretReferenceResolver, ProvisionedMaterialResolver>();
        services.AddSingleton<TrustAnchorLoader>();
        services.AddObjectStorage(Endpoint, ReclamationBounds, configuredTrustAnchor: null);

        return services;
    }
}
