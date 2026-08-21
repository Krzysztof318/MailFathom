// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using MailFathom.Infrastructure.SensitiveContent.PersonalData;
using MailFathom.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.SensitiveContent.PersonalData;

/// <summary>Covers how many times one scan reaches an analyzer that is refusing.</summary>
/// <remarks>
/// <para>
/// The answer has to be one handler's worth of attempts. The host's service defaults add the standard resilience handler to
/// every client the factory builds, and this registration adds one of its own bounded by the configured scan budget;
/// leaving both would multiply attempts by attempts on the one client that carries mail content in the clear, which is the
/// nested retry storm the root instructions refuse. Infrastructure removes the inherited handler before adding its
/// replacement to prevent that, and nothing about the registration says so on its own.
/// </para>
/// <para>
/// What breaks the property is the removal going missing, not the order the two registrations run in. Both were measured
/// against this composition: with the removal in place the analyzer is reached four times to an ordinary client's four, and
/// the surviving handler is this registration's own rather than the inherited one — configured to retry once, the analyzer
/// is reached twice while the ordinary client stays at four. With the removal deleted it is reached sixteen times. Swapping
/// the two registrations changes none of that, which is why the assertion states the single-layer property rather than the
/// composition order that was assumed to carry it.
/// </para>
/// </remarks>
public sealed class PersonalDataAnalyzerTransportTests
{
    /// <summary>Any route resolved against the registration's base address; the scripted handler answers whatever it is asked.</summary>
    private static readonly Uri HealthRoute = new("health", UriKind.Relative);

    /// <summary>The transport carries one layer of retry, so a refusal costs one handler's attempts rather than their square.</summary>
    [Fact]
    public async Task PersonalDataAnalyzerTransport_ComposedUnderTheServiceDefaults_RetriesARefusalInOneLayer()
    {
        // Arrange
        using var analyzer = FakeHttpMessageHandler.AlwaysResponding(
            () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var ordinaryServer = FakeHttpMessageHandler.AlwaysResponding(
            () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var services = ComposedServiceCollection();
        services.AddHttpClient(PersonalDataAnalyzerProfile.TransportName)
            .ConfigurePrimaryHttpMessageHandler(() => analyzer);
        services.AddHttpClient("ordinary")
            .ConfigurePrimaryHttpMessageHandler(() => ordinaryServer);

        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        using var analyzerTransport = factory.CreateClient(PersonalDataAnalyzerProfile.TransportName);
        using var ordinaryTransport = factory.CreateClient("ordinary");
        ordinaryTransport.BaseAddress = analyzerTransport.BaseAddress;

        // Act
        using var analyzerResponse = await analyzerTransport.GetAsync(HealthRoute, TestContext.Current.CancellationToken);
        using var ordinaryResponse = await ordinaryTransport.GetAsync(HealthRoute, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.ServiceUnavailable, analyzerResponse.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ordinaryResponse.StatusCode);

        // The control is what makes the count beside it a removal rather than a handler that was never added: the defaults
        // this test composes are live, so an ordinary client under them retries. Two layers on the analyzer would square
        // that number rather than raise it, so the comparison holds whatever the standard handler's attempt count is.
        Assert.True(ordinaryServer.RecordedRequests.Count > 1);
        Assert.Equal(ordinaryServer.RecordedRequests.Count, analyzer.RecordedRequests.Count);
    }

    /// <summary>Composes the service defaults and the registration under test, as the host composes them.</summary>
    /// <remarks>
    /// One deviation from the host, and it is timing rather than structure: the retry delay is removed from both handlers so
    /// that a client carrying two of them is observed retrying in milliseconds rather than over the better part of a minute
    /// of jittered backoff, on every run of the suite. The analyzer's handler is reached by the name the registration
    /// configures it under, and only the delay between attempts is moved — the timeouts derived from the scan budget stay
    /// as the registration set them, which is what the theory in <c>ServiceCollectionExtensionsTests</c> asserts.
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

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(PersonalDataScanningPlans.Profile);
        services.AddSingleton(PersonalDataScanningPlans.Default);
        services.AddPersonalDataContentScanning();

        services.Configure<HttpStandardResilienceOptions>(
            $"{PersonalDataAnalyzerProfile.TransportName}-standard",
            static options =>
            {
                options.Retry.Delay = TimeSpan.Zero;
                options.Retry.UseJitter = false;
            });

        return services;
    }
}
