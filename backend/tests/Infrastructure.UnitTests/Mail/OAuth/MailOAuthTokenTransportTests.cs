// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Infrastructure.Mail.OAuth;
using MailFathom.Infrastructure.Persistence.Connections;
using MailFathom.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Mail.OAuth;

/// <summary>Covers how many times one token request reaches an authorization server that is refusing.</summary>
/// <remarks>
/// <para>
/// The answer has to be once, and nothing about the registration says so on its own. The host's service defaults add the
/// standard resilience handler to every client the factory builds, <see cref="MailOAuthAccessTokenSource" /> already runs
/// the exchange under the <c>MailAuthorizationServerInvocation</c> pipeline, and three attempts wrapped by three is nine
/// token requests into a server that is already struggling. Infrastructure removes the handler for this one client to
/// prevent that.
/// </para>
/// <para>
/// The removal is order-dependent, which is the whole reason this is asserted rather than read. It takes out what was
/// registered before it, so it works only while the composition root adds the service defaults ahead of the
/// infrastructure. Nothing in either file fails if that order is swapped; this does.
/// </para>
/// </remarks>
public sealed class MailOAuthTokenTransportTests
{
    private static readonly Uri TokenEndpoint = new("https://sso.example.test/oauth2/token");

    /// <summary>The transport carries no retry of its own, so a refusal is one request rather than a burst of them.</summary>
    [Fact]
    public async Task MailOAuthTransport_ComposedUnderTheServiceDefaults_ReachesARefusingServerOnce()
    {
        // Arrange
        using var authorizationServer = FakeHttpMessageHandler.AlwaysResponding(
            () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        await using var provider = ComposedServices(authorizationServer);
        using var transport = provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(MailOAuthAccessTokenSource.TransportName);

        // Act
        using var response = await transport.GetAsync(TokenEndpoint, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Single(authorizationServer.RecordedRequests);
    }

    /// <summary>The defaults the test composes are live, so the assertion above is a removal rather than a handler that was never added.</summary>
    [Fact]
    public async Task AnOrdinaryClient_ComposedUnderTheSameDefaults_RetriesARefusal()
    {
        // Arrange
        using var refusingServer = FakeHttpMessageHandler.AlwaysResponding(
            () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var services = ComposedServiceCollection();
        services.AddHttpClient("ordinary")
            .ConfigurePrimaryHttpMessageHandler(() => refusingServer);

        await using var provider = services.BuildServiceProvider();
        using var transport = provider.GetRequiredService<IHttpClientFactory>().CreateClient("ordinary");

        // Act
        using var response = await transport.GetAsync(TokenEndpoint, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.True(refusingServer.RecordedRequests.Count > 1);
    }

    private static ServiceProvider ComposedServices(FakeHttpMessageHandler authorizationServer)
    {
        var services = ComposedServiceCollection();

        // The registration's own primary handler opens a socket, so the test replaces it. Assignment rather than
        // appending is what lets it: the same call in the registration sets the handler that this one then replaces.
        services.AddHttpClient(MailOAuthAccessTokenSource.TransportName)
            .ConfigurePrimaryHttpMessageHandler(() => authorizationServer);

        return services.BuildServiceProvider();
    }

    /// <summary>Composes the two registrations whose order decides the outcome, as the host composes them.</summary>
    /// <remarks>
    /// One deviation from the host, and it is timing rather than structure: the retry delay is removed so that a client
    /// still carrying the handler is observed retrying in milliseconds. Left at the standard backoff, the control below
    /// would spend the better part of a minute proving the same thing, and a suite that must stay fast would have paid
    /// that on every run.
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
        services.AddInfrastructure(
            static _ => new PostgresConnectionSettings(
                "Host=postgres.example.test;Database=mailfathom",
                ConnectionStringSecret: null,
                Password: null),
            PostgresTextSearchConfiguration.Create("simple"),
            MailAnsweringBudget.Default);

        return services;
    }
}
