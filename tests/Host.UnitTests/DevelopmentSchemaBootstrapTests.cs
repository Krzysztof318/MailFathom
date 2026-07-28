// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Host.Configuration;
using MailMcp.Host.Hosting;
using MailMcp.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace MailMcp.Host.UnitTests;

/// <summary>Covers the gate that keeps an unreviewed schema out of every environment but a developer's own.</summary>
public sealed class DevelopmentSchemaBootstrapTests
{
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Test")]
    public async Task StartAsync_BootstrapEnabledOutsideDevelopment_FailsStartupWithoutTouchingTheDatabase(string environmentName)
    {
        // Arrange
        var schemaCreator = Substitute.For<IDevelopmentSchemaCreator>();
        var bootstrap = CreateBootstrap(environmentName, schemaCreator, createSchemaFromModelOnStartup: true);

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => bootstrap.StartAsync(CancellationToken.None));

        // Assert
        Assert.Contains(environmentName, failure.Message, StringComparison.Ordinal);
        await schemaCreator.DidNotReceiveWithAnyArgs().CreateSchemaAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_BootstrapDisabledOutsideDevelopment_StartsWithoutCreatingAnything()
    {
        // Arrange
        var schemaCreator = Substitute.For<IDevelopmentSchemaCreator>();
        var bootstrap = CreateBootstrap("Production", schemaCreator, createSchemaFromModelOnStartup: false);

        // Act
        await bootstrap.StartAsync(CancellationToken.None);

        // Assert
        await schemaCreator.DidNotReceiveWithAnyArgs().CreateSchemaAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_BootstrapDisabledInDevelopment_CreatesNothingUntilAnOperatorOptsIn()
    {
        // Arrange
        var schemaCreator = Substitute.For<IDevelopmentSchemaCreator>();
        var bootstrap = CreateBootstrap(Environments.Development, schemaCreator, createSchemaFromModelOnStartup: false);

        // Act
        await bootstrap.StartAsync(CancellationToken.None);

        // Assert
        await schemaCreator.DidNotReceiveWithAnyArgs().CreateSchemaAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_BootstrapEnabledInDevelopment_CreatesTheSchemaFromTheModel()
    {
        // Arrange
        var schemaCreator = Substitute.For<IDevelopmentSchemaCreator>();
        var bootstrap = CreateBootstrap(Environments.Development, schemaCreator, createSchemaFromModelOnStartup: true);
        using var cancellation = new CancellationTokenSource();

        // Act
        await bootstrap.StartAsync(cancellation.Token);

        // Assert
        await schemaCreator.Received(1).CreateSchemaAsync(cancellation.Token);
    }

    private static DevelopmentSchemaBootstrap CreateBootstrap(
        string environmentName,
        IDevelopmentSchemaCreator schemaCreator,
        bool createSchemaFromModelOnStartup)
    {
        var services = new ServiceCollection();
        services.AddSingleton(schemaCreator);

        return new DevelopmentSchemaBootstrap(
            new StubHostEnvironment(environmentName),
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new PersistenceOptions
            {
                CreateSchemaFromModelOnStartup = createSchemaFromModelOnStartup,
            }),
            new RecordingLogger<DevelopmentSchemaBootstrap>());
    }

    private sealed class StubHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "MailMcp.Host.UnitTests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
