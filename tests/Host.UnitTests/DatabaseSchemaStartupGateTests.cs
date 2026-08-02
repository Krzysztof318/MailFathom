// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Host.Hosting;
using MailFathom.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace MailFathom.Host.UnitTests;

/// <summary>Covers the apply policy: the host proves the schema it will write through and never changes it.</summary>
public sealed class DatabaseSchemaStartupGateTests
{
    [Fact]
    public async Task StartAsync_EveryMigrationApplied_StartsSoTheWorkersMayRun()
    {
        // Arrange
        var inspector = CreateCurrentSchemaInspector();

        // Act, Assert
        await CreateGate(inspector).StartAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartAsync_EveryMigrationApplied_ReportsTheSchemaGateToTheStartupProbe()
    {
        // Arrange
        var startupGates = new HostStartupGates(HostStartupGate.DatabaseSchema);

        // Act
        await CreateGate(CreateCurrentSchemaInspector(), startupGates: startupGates).StartAsync(CancellationToken.None);

        // Assert
        Assert.True(startupGates.Completed);
    }

    /// <summary>
    /// A gate that failed took the host down with it, so it never reports itself; what the startup probe must not do is
    /// report a host as having come up on the strength of a step that raised.
    /// </summary>
    [Fact]
    public async Task StartAsync_MigrationsPending_LeavesTheSchemaGateOutstanding()
    {
        // Arrange
        var startupGates = new HostStartupGates(HostStartupGate.DatabaseSchema);
        var inspector = Substitute.For<IDatabaseSchemaInspector>();
        inspector.ReadPendingMigrationIdentifiersAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(["20260729_Initial"]));

        // Act
        await Assert.ThrowsAsync<DatabaseSchemaOutOfDateException>(() =>
            CreateGate(inspector, startupGates: startupGates).StartAsync(CancellationToken.None));

        // Assert
        Assert.False(startupGates.Completed);
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
        Assert.Contains("mailfathom-migrations", exception.Message, StringComparison.Ordinal);
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
        var inspector = CreateCurrentSchemaInspector();
        using var cancellation = new CancellationTokenSource();

        // Act
        await CreateGate(inspector).StartAsync(cancellation.Token);

        // Assert
        await inspector.Received(1).ReadPendingMigrationIdentifiersAsync(cancellation.Token);
    }

    [Fact]
    public async Task StartAsync_LexicalIndexBuiltWithAnotherTextSearchConfiguration_FailsStartupNamingBoth()
    {
        // Arrange
        var inspector = CreateCurrentSchemaInspector("simple");

        // Act
        var exception = await Assert.ThrowsAsync<DatabaseSchemaTextSearchConfigurationMismatchException>(() =>
            CreateGate(inspector, PostgresTextSearchConfiguration.Create("english")).StartAsync(CancellationToken.None));

        // Assert
        Assert.Equal("simple", exception.SchemaConfiguration);
        Assert.Equal("english", exception.ConfiguredConfiguration);
        Assert.Contains("simple", exception.Message, StringComparison.Ordinal);
        Assert.Contains("english", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartAsync_LexicalIndexMatchesTheConfiguration_Starts()
    {
        // Arrange
        var inspector = CreateCurrentSchemaInspector("english");

        // Act, Assert
        await CreateGate(inspector, PostgresTextSearchConfiguration.Create("english")).StartAsync(CancellationToken.None);
    }

    /// <summary>A schema the inspector cannot identify ends startup rather than being read as agreement.</summary>
    [Fact]
    public async Task StartAsync_SearchVectorConfigurationUnidentifiable_FailsStartupRatherThanStartingTheWorkers()
    {
        // Arrange
        var inspector = CreateCurrentSchemaInspector();
        inspector.ReadSearchVectorTextSearchConfigurationAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new DatabaseSchemaStateUnreadableException("The lexical email index carries no stored search vector expression."));

        // Act, Assert
        await Assert.ThrowsAsync<DatabaseSchemaStateUnreadableException>(() =>
            CreateGate(inspector).StartAsync(CancellationToken.None));
    }

    [Fact]
    public async Task StartAsync_MigrationsPending_DoesNotReadTheLexicalIndexBecauseItsColumnNeedNotExistYet()
    {
        // Arrange
        var inspector = Substitute.For<IDatabaseSchemaInspector>();
        inspector.ReadPendingMigrationIdentifiersAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(["20260729_Initial"]));

        // Act
        await Assert.ThrowsAsync<DatabaseSchemaOutOfDateException>(() =>
            CreateGate(inspector).StartAsync(CancellationToken.None));

        // Assert
        await inspector.DidNotReceive().ReadSearchVectorTextSearchConfigurationAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>An inspector reporting a fully migrated schema whose lexical index names the given configuration.</summary>
    private static IDatabaseSchemaInspector CreateCurrentSchemaInspector(string schemaTextSearchConfiguration = "simple")
    {
        var inspector = Substitute.For<IDatabaseSchemaInspector>();
        inspector.ReadPendingMigrationIdentifiersAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>([]));
        inspector.ReadSearchVectorTextSearchConfigurationAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(schemaTextSearchConfiguration));

        return inspector;
    }

    private static DatabaseSchemaStartupGate CreateGate(
        IDatabaseSchemaInspector inspector,
        PostgresTextSearchConfiguration? textSearchConfiguration = null,
        HostStartupGates? startupGates = null)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => inspector);

        return new DatabaseSchemaStartupGate(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            textSearchConfiguration ?? PostgresTextSearchConfiguration.Default,
            startupGates ?? new HostStartupGates(HostStartupGate.DatabaseSchema),
            NullLogger<DatabaseSchemaStartupGate>.Instance);
    }
}
