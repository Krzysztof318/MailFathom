// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Hosting;
using MailFathom.Infrastructure.ObjectStorage;
using MailFathom.TestSupport;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting;

/// <summary>Covers what a readiness probe learns about the bucket a deployment stores its message content in.</summary>
public sealed class ObjectStorageHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_ABucketThatAnswers_IsHealthy()
    {
        // Arrange
        var probe = Substitute.For<IObjectStorageEndpointProbe>();
        var check = new ObjectStorageHealthCheck(probe, NullLogger<ObjectStorageHealthCheck>.Instance);

        // Act
        var result = await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        await probe.Received(1).VerifyAvailableAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// An instance whose selected content backend cannot be written to cannot store the next message it synchronizes,
    /// nor read the ones it already put there. Degraded would keep it in the load balancer answering requests it is
    /// about to fail.
    /// </summary>
    [Fact]
    public async Task CheckHealthAsync_ABucketThatDoesNotAnswer_IsUnhealthyRatherThanDegraded()
    {
        // Arrange
        var check = CheckOverAProbeFailing(Refused(), NullLogger<ObjectStorageHealthCheck>.Instance);

        // Act
        var result = await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    /// <summary>A probe that failed in a way its port does not declare is still a bucket that did not answer.</summary>
    [Fact]
    public async Task CheckHealthAsync_AProbeThatFailedUndeclared_IsUnhealthyRatherThanThrowing()
    {
        // Arrange
        var check = CheckOverAProbeFailing(
            new InvalidOperationException("the probe broke"),
            NullLogger<ObjectStorageHealthCheck>.Instance);

        // Act
        var result = await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    /// <summary>A scrape the caller abandoned says nothing about the bucket, so it must not take an instance out of traffic.</summary>
    [Fact]
    public async Task CheckHealthAsync_AScrapeTheCallerCancelled_PropagatesRatherThanReportingUnhealthy()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var check = CheckOverAProbeFailing(
            new OperationCanceledException(),
            NullLogger<ObjectStorageHealthCheck>.Instance);

        // Act, Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => check.CheckHealthAsync(new HealthCheckContext(), cancellation.Token));
    }

    /// <summary>A cancellation the caller did not ask for is the bucket failing to answer inside its budget, not a scrape somebody abandoned.</summary>
    [Fact]
    public async Task CheckHealthAsync_AProbeCancelledByItsOwnBudget_IsUnhealthyRatherThanPropagating()
    {
        // Arrange
        var check = CheckOverAProbeFailing(
            new OperationCanceledException(),
            NullLogger<ObjectStorageHealthCheck>.Instance);

        // Act
        var result = await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    /// <summary>
    /// The probe response is one word by design, so the log is where the reason lives. An instance that came up with no
    /// bucket says so on its first scrape rather than staying silent because nothing changed, and says it once rather
    /// than on every scrape of the outage.
    /// </summary>
    [Fact]
    public async Task CheckHealthAsync_ABucketThatDoesNotAnswer_LogsTheFailureAtErrorOnce()
    {
        // Arrange
        using var loggerFactory = new RecordingLoggerFactory();
        var check = CheckOverAProbeFailing(Refused(), loggerFactory.CreateLogger<ObjectStorageHealthCheck>());

        // Act
        await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);
        await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);
        await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(loggerFactory.Records);

        Assert.Equal(LogLevel.Error, record.Level);
        Assert.IsType<ObjectStorageUnavailableException>(record.Failure);
        Assert.Contains("ContentStorage:ObjectStorage", record.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("objects.example.test", record.Message, StringComparison.Ordinal);
    }

    /// <summary>The first observation of either kind is a transition, so a bucket answering from the very first scrape says so once.</summary>
    [Fact]
    public async Task CheckHealthAsync_ABucketAnsweringFromTheFirstScrape_LogsItOnceAtInformation()
    {
        // Arrange
        using var loggerFactory = new RecordingLoggerFactory();
        var check = new ObjectStorageHealthCheck(
            Substitute.For<IObjectStorageEndpointProbe>(),
            loggerFactory.CreateLogger<ObjectStorageHealthCheck>());

        // Act
        await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);
        await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(loggerFactory.Records);

        Assert.Equal(LogLevel.Information, record.Level);
        Assert.Null(record.Failure);
    }

    /// <summary>A record saying the outage began is worth little without the one saying it ended.</summary>
    [Fact]
    public async Task CheckHealthAsync_ABucketThatCameBack_LogsBothTransitionsAndNothingBetween()
    {
        // Arrange
        using var loggerFactory = new RecordingLoggerFactory();
        var probe = Substitute.For<IObjectStorageEndpointProbe>();
        var answering = false;
        probe.VerifyAvailableAsync(Arg.Any<CancellationToken>())
            .Returns(_ => answering ? Task.CompletedTask : Task.FromException(Refused()));

        var check = new ObjectStorageHealthCheck(probe, loggerFactory.CreateLogger<ObjectStorageHealthCheck>());

        // Act
        var refused = await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);
        await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);
        answering = true;
        var recovered = await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);
        await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, refused.Status);
        Assert.Equal(HealthStatus.Healthy, recovered.Status);
        Assert.Equal(
            [LogLevel.Error, LogLevel.Information],
            loggerFactory.Records.Select(record => record.Level));
    }

    /// <summary>
    /// Restarting this process cannot make a bucket reachable, so the check carries the readiness tag alone: a liveness
    /// failure would turn one endpoint's outage into a restart loop across every replica.
    /// </summary>
    [Fact]
    public void Registration_TheCheck_IsReadinessOnlyAndUnhealthyOnFailure()
    {
        // Act
        var registration = ObjectStorageHealthCheck.Registration();

        // Assert
        Assert.Equal(ObjectStorageHealthCheck.Name, registration.Name);
        Assert.Equal(HealthStatus.Unhealthy, registration.FailureStatus);
        Assert.Equal([HealthProbe.Readiness.Tag], registration.Tags);
    }

    [Fact]
    public void Construction_MissingCollaborator_IsRefused()
    {
        // Arrange
        var probe = Substitute.For<IObjectStorageEndpointProbe>();

        // Act, Assert
        Assert.Throws<ArgumentNullException>(
            () => new ObjectStorageHealthCheck(null!, NullLogger<ObjectStorageHealthCheck>.Instance));
        Assert.Throws<ArgumentNullException>(() => new ObjectStorageHealthCheck(probe, null!));
    }

    private static ObjectStorageHealthCheck CheckOverAProbeFailing(
        Exception failure,
        ILogger<ObjectStorageHealthCheck> logger)
    {
        var probe = Substitute.For<IObjectStorageEndpointProbe>();
        probe.VerifyAvailableAsync(Arg.Any<CancellationToken>()).Returns(_ => Task.FromException(failure));

        return new ObjectStorageHealthCheck(probe, logger);
    }

    private static ObjectStorageUnavailableException Refused() => ObjectStorageUnavailableException.From(
        ObjectStorageFailure.TransientTransportFailure,
        new HttpRequestException("no route to objects.example.test:9000"));
}
