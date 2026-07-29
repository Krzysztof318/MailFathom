// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.Persistence;
using MailMcp.Host.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace MailMcp.Host.UnitTests;

/// <summary>Covers the apply policy: the host proves the schema it will write through and never changes it.</summary>
public sealed class DatabaseSchemaStartupGateTests
{
    [Fact]
    public async Task StartAsync_EveryMigrationApplied_StartsSoTheWorkersMayRun()
    {
        // Arrange
        var inspector = Substitute.For<IDatabaseSchemaInspector>();
        inspector.ReadPendingMigrationIdentifiersAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>([]));

        // Act, Assert
        await CreateGate(inspector).StartAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartAsync_MigrationsPending_FailsStartupNamingThemInsteadOfApplyingThem()
    {
        // Arrange
        var inspector = Substitute.For<IDatabaseSchemaInspector>();
        inspector.ReadPendingMigrationIdentifiersAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(["20260729_Initial", "20260730_AddSearch"]));

        // Act
        var exception = await Assert.ThrowsAsync<DatabaseSchemaOutOfDateException>(() =>
            CreateGate(inspector).StartAsync(CancellationToken.None));

        // Assert
        Assert.Equal(["20260729_Initial", "20260730_AddSearch"], exception.PendingMigrationIdentifiers);
        Assert.Contains("20260729_Initial", exception.Message, StringComparison.Ordinal);
        Assert.Contains("mailmcp-migrations", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartAsync_MigrationHistoryUnreadable_FailsStartupRatherThanAssumingTheSchemaIsCurrent()
    {
        // Arrange
        var inspector = Substitute.For<IDatabaseSchemaInspector>();
        inspector.ReadPendingMigrationIdentifiersAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new DatabaseSchemaStateUnreadableException("unreadable", new InvalidOperationException()));

        // Act, Assert
        await Assert.ThrowsAsync<DatabaseSchemaStateUnreadableException>(() =>
            CreateGate(inspector).StartAsync(CancellationToken.None));
    }

    [Fact]
    public async Task StartAsync_CallerCancelled_PropagatesTheTokenToTheInspector()
    {
        // Arrange
        var inspector = Substitute.For<IDatabaseSchemaInspector>();
        inspector.ReadPendingMigrationIdentifiersAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>([]));
        using var cancellation = new CancellationTokenSource();

        // Act
        await CreateGate(inspector).StartAsync(cancellation.Token);

        // Assert
        await inspector.Received(1).ReadPendingMigrationIdentifiersAsync(cancellation.Token);
    }

    private static DatabaseSchemaStartupGate CreateGate(IDatabaseSchemaInspector inspector)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => inspector);

        return new DatabaseSchemaStartupGate(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<DatabaseSchemaStartupGate>.Instance);
    }
}
