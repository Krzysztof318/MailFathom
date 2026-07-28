// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MailMcp.Infrastructure.UnitTests;

/// <summary>Keeps <c>dotnet ef</c> working without a running host, which composes the connection string at startup.</summary>
public sealed class MailMcpDbContextDesignTimeFactoryTests
{
    [Fact]
    public void BuildOptions_NoDesignTimeConnectionString_FallsBackToTheLocalDevelopmentDatabase()
    {
        // Act
        var options = MailMcpDbContextDesignTimeFactory.BuildOptions(configuredConnectionString: null);

        // Assert
        using var context = new MailMcpDbContext(options, PostgresTextSearchConfiguration.Default);
        Assert.Equal(
            MailMcpDbContextDesignTimeFactory.LocalDevelopmentConnectionString,
            context.Database.GetConnectionString());
    }

    [Fact]
    public void BuildOptions_ConfiguredDesignTimeConnectionString_UsesIt()
    {
        // Act
        var options = MailMcpDbContextDesignTimeFactory.BuildOptions("Host=db.test;Database=mailmcp;Username=dev");

        // Assert
        using var context = new MailMcpDbContext(options, PostgresTextSearchConfiguration.Default);
        Assert.Equal("Host=db.test;Database=mailmcp;Username=dev", context.Database.GetConnectionString());
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
