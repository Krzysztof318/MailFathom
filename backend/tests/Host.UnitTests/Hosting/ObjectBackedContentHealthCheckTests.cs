// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.Host.Hosting;
using MailFathom.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting;

/// <summary>Covers what a readiness probe learns about mail this deployment holds where it can no longer reach it.</summary>
/// <remarks>
/// The condition is a deployment that stored content in an object endpoint and then lost the configuration naming one.
/// Nothing above notices — the row is intact, the metadata answers, and the failure arrives only when somebody asks
/// for the content of one of those particular messages — so this is the check that says so first.
/// </remarks>
public sealed class ObjectBackedContentHealthCheckTests
{
    /// <summary>A deployment that never wrote to an endpoint is not missing one, whatever its configuration says.</summary>
    [Fact]
    public async Task CheckHealthAsync_NoPayloadHeldInAnObjectEndpoint_IsHealthy()
    {
        // Arrange
        var check = CheckOver(InventoryAnswering(holdsObjectBackedContent: false));

        // Act
        var result = await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    /// <summary>
    /// An instance that cannot read the mail it holds is failing the thing mail is stored in rather than serving a
    /// narrower service, so degraded would keep it in the load balancer answering requests it is about to fail.
    /// </summary>
    [Fact]
    public async Task CheckHealthAsync_PayloadsHeldInAnObjectEndpointThisInstanceNamesNone_IsUnhealthyRatherThanDegraded()
    {
        // Arrange
        var check = CheckOver(InventoryAnswering(holdsObjectBackedContent: true));

        // Act
        var result = await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    /// <summary>
    /// A database nothing can be read from is not a deployment holding no object-backed content, so the read is left to
    /// fail rather than answered as an absence. The registration is what reports it, which is what keeps a readiness
    /// scrape arriving before the schema gate has proven a schema from reading as everything being well.
    /// </summary>
    [Fact]
    public async Task CheckHealthAsync_AnInventoryThatCannotBeRead_FailsRatherThanAnsweringThatNothingIsHeld()
    {
        // Arrange
        var inventory = Substitute.For<IObjectBackedContentInventory>();
        inventory.HoldsObjectBackedContentAsync(Arg.Any<CancellationToken>())
            .Returns<Task<bool>>(_ => throw new InvalidOperationException("relation does not exist"));

        var check = CheckOver(inventory);

        // Act, Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken));
    }

    /// <summary>A scrape the caller abandoned says nothing about the stored mail, so it must not take an instance out of traffic.</summary>
    [Fact]
    public async Task CheckHealthAsync_AScrapeTheCallerCancelled_PropagatesRatherThanReportingUnhealthy()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var inventory = Substitute.For<IObjectBackedContentInventory>();
        inventory.HoldsObjectBackedContentAsync(Arg.Any<CancellationToken>())
            .Returns<Task<bool>>(_ => throw new OperationCanceledException());

        var check = CheckOver(inventory);

        // Act, Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => check.CheckHealthAsync(new HealthCheckContext(), cancellation.Token));
    }

    /// <summary>
    /// The probe response is one word by design, so the log is where the reason lives. An instance that comes up
    /// stranded says so on its first scrape rather than staying silent because nothing changed, and says it once
    /// rather than on every scrape of the condition.
    /// </summary>
    [Fact]
    public async Task CheckHealthAsync_PayloadsHeldInAnObjectEndpointThisInstanceNamesNone_LogsItAtErrorOnce()
    {
        // Arrange
        using var loggerFactory = new RecordingLoggerFactory();
        var check = CheckOver(
            InventoryAnswering(holdsObjectBackedContent: true),
            loggerFactory.CreateLogger<ObjectBackedContentHealthCheck>());

        // Act
        await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);
        await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);
        await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(loggerFactory.Records);

        Assert.Equal(LogLevel.Error, record.Level);
        Assert.Contains("ContentStorage:ObjectStorage", record.Message, StringComparison.Ordinal);
    }

    /// <summary>A record saying the condition began is worth little without the one saying it ended.</summary>
    [Fact]
    public async Task CheckHealthAsync_AnEndpointConfiguredAgain_LogsBothTransitionsAndNothingBetween()
    {
        // Arrange
        using var loggerFactory = new RecordingLoggerFactory();
        var stranded = true;
        var inventory = Substitute.For<IObjectBackedContentInventory>();
        inventory.HoldsObjectBackedContentAsync(Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult(stranded));

        var check = CheckOver(inventory, loggerFactory.CreateLogger<ObjectBackedContentHealthCheck>());

        // Act
        var first = await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);
        await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);
        stranded = false;
        var recovered = await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);
        await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, first.Status);
        Assert.Equal(HealthStatus.Healthy, recovered.Status);
        Assert.Equal(
            [LogLevel.Error, LogLevel.Information],
            loggerFactory.Records.Select(record => record.Level));
    }

    /// <summary>The inventory is read in a scope of its own, because this check outlives one and the inventory does not.</summary>
    [Fact]
    public async Task CheckHealthAsync_EveryScrape_ReadsTheInventoryInAScopeOfItsOwn()
    {
        // Arrange
        var scopes = 0;
        var check = CheckOver(InventoryAnswering(holdsObjectBackedContent: false), onScopeCreated: () => scopes++);

        // Act
        await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);
        await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, scopes);
    }

    /// <summary>
    /// Restarting this process cannot restore a configuration key, so the check carries the readiness tag alone: a
    /// liveness failure would turn one missing section into a restart loop across every replica.
    /// </summary>
    [Fact]
    public void Registration_TheCheck_IsReadinessOnlyAndUnhealthyOnFailure()
    {
        // Act
        var registration = ObjectBackedContentHealthCheck.Registration();

        // Assert
        Assert.Equal(ObjectBackedContentHealthCheck.Name, registration.Name);
        Assert.Equal(HealthStatus.Unhealthy, registration.FailureStatus);
        Assert.Equal([HealthProbe.Readiness.Tag], registration.Tags);
    }

    [Fact]
    public void Construction_MissingCollaborator_IsRefused()
    {
        // Arrange
        var scopeFactory = Substitute.For<IServiceScopeFactory>();

        // Act, Assert
        Assert.Throws<ArgumentNullException>(
            () => new ObjectBackedContentHealthCheck(null!, NullLogger<ObjectBackedContentHealthCheck>.Instance));
        Assert.Throws<ArgumentNullException>(() => new ObjectBackedContentHealthCheck(scopeFactory, null!));
    }

    private static IObjectBackedContentInventory InventoryAnswering(bool holdsObjectBackedContent)
    {
        var inventory = Substitute.For<IObjectBackedContentInventory>();
        inventory.HoldsObjectBackedContentAsync(Arg.Any<CancellationToken>()).Returns(holdsObjectBackedContent);

        return inventory;
    }

    private static ObjectBackedContentHealthCheck CheckOver(
        IObjectBackedContentInventory inventory,
        ILogger<ObjectBackedContentHealthCheck>? logger = null,
        Action? onScopeCreated = null)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => inventory);

        var provider = services.BuildServiceProvider();
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(_ =>
        {
            onScopeCreated?.Invoke();

            return provider.CreateScope();
        });

        return new ObjectBackedContentHealthCheck(
            scopeFactory,
            logger ?? NullLogger<ObjectBackedContentHealthCheck>.Instance);
    }
}
