// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailFathom.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests;

/// <summary>Keeps <c>dotnet ef</c> working without a running host, which composes the connection string at startup.</summary>
public sealed class MailFathomDbContextDesignTimeFactoryTests
{
    [Fact]
    public void BuildOptions_NoConnectionStringAtAll_FallsBackToTheLocalDevelopmentDatabase()
    {
        // Act
        var options = MailFathomDbContextDesignTimeFactory.BuildOptions(
            orchestratedConnectionString: null,
            designTimeConnectionString: null);

        // Assert
        using var context = new MailFathomDbContext(options, PostgresTextSearchConfiguration.Default);
        Assert.Equal(
            MailFathomDbContextDesignTimeFactory.LocalDevelopmentConnectionString,
            context.Database.GetConnectionString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildOptions_BlankOrchestratedConnectionString_UsesTheDesignTimeOverride(string? orchestrated)
    {
        // Act
        var options = MailFathomDbContextDesignTimeFactory.BuildOptions(
            orchestrated,
            "Host=db.test;Database=mailfathom;Username=dev");

        // Assert
        using var context = new MailFathomDbContext(options, PostgresTextSearchConfiguration.Default);
        Assert.Equal("Host=db.test;Database=mailfathom;Username=dev", context.Database.GetConnectionString());
    }

    [Fact]
    public void BuildOptions_OrchestrationIssuedAConnectionString_PrefersItOverTheDesignTimeOverride()
    {
        // Act
        var options = MailFathomDbContextDesignTimeFactory.BuildOptions(
            "Host=orchestrated;Database=mailfathom;Username=orchestrated",
            "Host=stale;Database=mailfathom;Username=stale");

        // Assert
        using var context = new MailFathomDbContext(options, PostgresTextSearchConfiguration.Default);
        Assert.Equal(
            "Host=orchestrated;Database=mailfathom;Username=orchestrated",
            context.Database.GetConnectionString());
    }

    [Fact]
    public void ReadTextSearchConfiguration_NoneConfigured_UsesTheDefaultTheModelWouldUse()
    {
        // Act
        var configuration = MailFathomDbContextDesignTimeFactory.ReadTextSearchConfiguration(null);

        // Assert
        Assert.Equal(PostgresTextSearchConfiguration.Default.Value, configuration.Value);
    }

    [Fact]
    public void ReadTextSearchConfiguration_DeploymentConfiguredOne_GeneratesTheMigrationForIt()
    {
        // Act
        var configuration = MailFathomDbContextDesignTimeFactory.ReadTextSearchConfiguration("english");

        // Assert
        Assert.Equal("english", configuration.Value);
    }

    [Fact]
    public void ReadTextSearchConfiguration_UnsupportedName_FailsRatherThanCompilingItIntoTheSchema()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() =>
            MailFathomDbContextDesignTimeFactory.ReadTextSearchConfiguration("klingon"));
    }

    [Fact]
    public void CreateDbContext_DesignTimeTooling_ProducesAUsableModelWithoutAHost()
    {
        // Arrange
        var factory = new MailFathomDbContextDesignTimeFactory();

        // Act
        using var context = factory.CreateDbContext([]);

        // Assert
        Assert.NotEmpty(context.Model.GetEntityTypes());
    }
}
