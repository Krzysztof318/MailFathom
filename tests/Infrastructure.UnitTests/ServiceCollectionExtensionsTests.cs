// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Secrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests;

public sealed class ServiceCollectionExtensionsTests
{
    /// <summary>A numeric configuration value binds to an undefined member, which must fail rather than fall through to the strictest mode by accident.</summary>
    [Fact]
    public void AddSecretResolution_UndefinedInterpretation_FailsInsteadOfStartingInAModeNobodySelected()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(
            () => services.AddSecretResolution((SecretValueInterpretation)99));
    }

    [Theory]
    [InlineData(SecretValueInterpretation.ReferenceOnly)]
    [InlineData(SecretValueInterpretation.ReferenceOrInline)]
    [InlineData(SecretValueInterpretation.InlineOnly)]
    public void AddSecretResolution_DefinedInterpretation_RegistersTheDeploymentsMode(
        SecretValueInterpretation interpretation)
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddSecretResolution(interpretation);

        // Assert
        using var provider = services.BuildServiceProvider();
        Assert.Equal(interpretation, provider.GetRequiredService<SecretResolutionOptions>().Interpretation);
    }

    /// <summary>
    /// The container must be the only owner of the data source. One built inside the startup provider is invisible to
    /// the container, so a host that resolved no context would shut down leaving its connection pool open.
    /// </summary>
    [Fact]
    public async Task AddInfrastructure_AfterStartup_HandsTheContainerADataSourceItCreatedItself()
    {
        // Arrange
        await using var provider = BuildConfiguredProvider();
        var connectionStringProvider = provider.GetServices<IHostedService>()
            .OfType<IHostedLifecycleService>()
            .Single();

        // Act
        await connectionStringProvider.StartingAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(provider.GetRequiredService<NpgsqlDataSource>(), provider.GetRequiredService<NpgsqlDataSource>());
        Assert.IsNotAssignableFrom<IDisposable>(connectionStringProvider);
        Assert.IsNotAssignableFrom<IAsyncDisposable>(connectionStringProvider);
    }

    [Fact]
    public async Task AddInfrastructure_DataSourceRequestedBeforeStartup_ThrowsInsteadOfUsingAnUncomposedConnectionString()
    {
        // Arrange
        await using var provider = BuildConfiguredProvider();

        // Act, Assert
        Assert.Throws<InvalidOperationException>(provider.GetRequiredService<NpgsqlDataSource>);
    }

    private static ServiceProvider BuildConfiguredProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSecretResolution(SecretValueInterpretation.ReferenceOnly);
        services.AddInfrastructure(_ => new PostgresConnectionSettings(
            "Host=localhost;Database=mailfathom;Username=mailfathom",
            ConnectionStringSecret: null,
            Password: null),
            PostgresTextSearchConfiguration.Default);

        return services.BuildServiceProvider();
    }
}
