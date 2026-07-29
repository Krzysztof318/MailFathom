// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MailMcp.Infrastructure.UnitTests;

/// <summary>Keeps <c>dotnet ef</c> working without a running host, which composes the connection string at startup.</summary>
public sealed class MailMcpDbContextDesignTimeFactoryTests
{
    [Fact]
    public void BuildOptions_NoConnectionStringAtAll_FallsBackToTheLocalDevelopmentDatabase()
    {
        // Act
        var options = MailMcpDbContextDesignTimeFactory.BuildOptions(
            orchestratedConnectionString: null,
            designTimeConnectionString: null);

        // Assert
        using var context = new MailMcpDbContext(options, PostgresTextSearchConfiguration.Default);
        Assert.Equal(
            MailMcpDbContextDesignTimeFactory.LocalDevelopmentConnectionString,
            context.Database.GetConnectionString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildOptions_BlankOrchestratedConnectionString_UsesTheDesignTimeOverride(string? orchestrated)
    {
        // Act
        var options = MailMcpDbContextDesignTimeFactory.BuildOptions(
            orchestrated,
            "Host=db.test;Database=mailmcp;Username=dev");

        // Assert
        using var context = new MailMcpDbContext(options, PostgresTextSearchConfiguration.Default);
        Assert.Equal("Host=db.test;Database=mailmcp;Username=dev", context.Database.GetConnectionString());
    }

    [Fact]
    public void BuildOptions_OrchestrationIssuedAConnectionString_PrefersItOverTheDesignTimeOverride()
    {
        // Act
        var options = MailMcpDbContextDesignTimeFactory.BuildOptions(
            "Host=orchestrated;Database=mailmcp;Username=orchestrated",
            "Host=stale;Database=mailmcp;Username=stale");

        // Assert
        using var context = new MailMcpDbContext(options, PostgresTextSearchConfiguration.Default);
        Assert.Equal(
            "Host=orchestrated;Database=mailmcp;Username=orchestrated",
            context.Database.GetConnectionString());
    }

    [Fact]
    public void ReadTextSearchConfiguration_NoneConfigured_UsesTheDefaultTheModelWouldUse()
    {
        // Act
        var configuration = MailMcpDbContextDesignTimeFactory.ReadTextSearchConfiguration(null);

        // Assert
        Assert.Equal(PostgresTextSearchConfiguration.Default.Value, configuration.Value);
    }

    [Fact]
    public void ReadTextSearchConfiguration_DeploymentConfiguredOne_GeneratesTheMigrationForIt()
    {
        // Act
        var configuration = MailMcpDbContextDesignTimeFactory.ReadTextSearchConfiguration("english");

        // Assert
        Assert.Equal("english", configuration.Value);
    }

    [Fact]
    public void ReadTextSearchConfiguration_UnsupportedName_FailsRatherThanCompilingItIntoTheSchema()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() =>
            MailMcpDbContextDesignTimeFactory.ReadTextSearchConfiguration("klingon"));
    }

    [Fact]
    public void CreateDbContext_DesignTimeTooling_ProducesAUsableModelWithoutAHost()
    {
        // Arrange
        var factory = new MailMcpDbContextDesignTimeFactory();

        // Act
        using var context = factory.CreateDbContext([]);

        // Assert
        Assert.NotEmpty(context.Model.GetEntityTypes());
    }
}
