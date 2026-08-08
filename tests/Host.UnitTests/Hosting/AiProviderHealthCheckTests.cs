// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.AiProviders;
using MailFathom.Host.Hosting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting;

/// <summary>Covers what a probe learns about a declared AI provider, and what it must never learn.</summary>
public sealed class AiProviderHealthCheckTests
{
    /// <summary>A freshly started instance whose first unit of work has not arrived is the ordinary case, not an alert.</summary>
    [Fact]
    public async Task CheckHealthAsync_AProviderNothingHasCalled_IsHealthy()
    {
        // Arrange
        var check = CheckReading(AiProviderRole.Chat, AiProviderHealthState.Unobserved);

        // Act
        var result = await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_AProviderThatAnswered_IsHealthy()
    {
        // Arrange
        var check = CheckReading(AiProviderRole.Chat, AiProviderHealthState.Serving);

        // Act
        var result = await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    /// <summary>
    /// Neither provider serves a request path, so a failing one may never take the instance out of traffic. Degraded is
    /// the worst verdict either can reach, whichever kind of failure ended the last call.
    /// </summary>
    [Theory]
    [InlineData(AiProviderHealthState.Unavailable)]
    [InlineData(AiProviderHealthState.Misconfigured)]
    public async Task CheckHealthAsync_AFailingProvider_IsNeverWorseThanDegraded(AiProviderHealthState state)
    {
        // Arrange
        var check = CheckReading(AiProviderRole.Embedding, state);

        // Act
        var result = await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HealthStatus.Degraded, result.Status);
    }

    /// <summary>A check answers for the role it was built with and consults no other, which is what keeps the two states apart.</summary>
    [Fact]
    public async Task CheckHealthAsync_AChatCheck_ReadsTheChatStateAlone()
    {
        // Arrange
        var healthReader = Substitute.For<IAiProviderHealthReader>();
        healthReader
            .Read(AiProviderRole.Chat)
            .Returns(new AiProviderHealth(AiProviderRole.Chat, AiProviderHealthState.Unavailable, ObservedAt: null));
        healthReader
            .Read(AiProviderRole.Embedding)
            .Returns(new AiProviderHealth(AiProviderRole.Embedding, AiProviderHealthState.Serving, ObservedAt: null));

        var check = new AiProviderHealthCheck(healthReader, AiProviderRole.Chat);

        // Act
        var result = await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HealthStatus.Degraded, result.Status);
        healthReader.DidNotReceive().Read(AiProviderRole.Embedding);
    }

    /// <summary>The name is the only thing that tells two otherwise identical checks apart wherever a report lists them.</summary>
    [Fact]
    public void RegistrationFor_TheTwoRoles_NamesThemApartAndReachesTheReadinessProbeAlone()
    {
        // Act
        var embedding = AiProviderHealthCheck.RegistrationFor(AiProviderRole.Embedding);
        var chat = AiProviderHealthCheck.RegistrationFor(AiProviderRole.Chat);

        // Assert
        Assert.NotEqual(embedding.Name, chat.Name);
        Assert.Equal(HealthStatus.Degraded, chat.FailureStatus);
        Assert.Equal([HealthProbe.Readiness.Tag], chat.Tags);
        Assert.True(HealthProbe.Readiness.Selects(chat));
        Assert.False(HealthProbe.Liveness.Selects(chat));
        Assert.False(HealthProbe.Startup.Selects(chat));
    }

    [Fact]
    public void Constructor_WithoutAHealthReader_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => new AiProviderHealthCheck(null!, AiProviderRole.Chat));
    }

    private static AiProviderHealthCheck CheckReading(AiProviderRole role, AiProviderHealthState state)
    {
        var healthReader = Substitute.For<IAiProviderHealthReader>();
        healthReader.Read(role).Returns(new AiProviderHealth(role, state, ObservedAt: null));

        return new AiProviderHealthCheck(healthReader, role);
    }
}
