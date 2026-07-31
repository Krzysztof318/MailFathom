// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailMcp.Infrastructure.Persistence;
using Xunit;

namespace MailMcp.Infrastructure.UnitTests;

public sealed class PostgresTextSearchConfigurationTests
{
    /// <summary>The default stems nothing, which is the reading a mailbox of unknown languages can rely on.</summary>
    [Fact]
    public void Default_UnconfiguredDeployment_IsTheNonStemmingConfiguration()
    {
        // Act, Assert
        Assert.Equal("simple", PostgresTextSearchConfiguration.Default.Value);
    }

    [Theory]
    [InlineData("simple")]
    [InlineData("english")]
    [InlineData("german")]
    [InlineData("polish")]
    [InlineData("English")]
    [InlineData("ENGLISH")]
    [InlineData("")]
    [InlineData(" english ")]
    [InlineData("english; DROP TABLE stored_emails")]
    [InlineData("pg_catalog.english")]
    public void Create_ConfiguredName_AcceptsOnlyAConfigurationPostgreSqlShipsUnderThatExactName(string configuredName)
    {
        // Arrange
        var isSupported = PostgresTextSearchConfiguration.SupportedNames.Contains(configuredName, StringComparer.Ordinal);

        // Act, Assert
        Assert.Equal(isSupported, PostgresTextSearchConfiguration.IsSupported(configuredName));

        if (isSupported)
        {
            Assert.Equal(configuredName, PostgresTextSearchConfiguration.Create(configuredName).Value);

            return;
        }

        // An unknown name is refused rather than passed through: it is compiled into a generated column, so accepting
        // one would either fail schema creation far from the mistake or index the mailbox under the wrong language.
        var failure = Assert.Throws<ArgumentException>(() => PostgresTextSearchConfiguration.Create(configuredName));
        Assert.Contains("is not a supported PostgreSQL text search configuration", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>A missing value is not a configuration, so the caller has to choose the default rather than be given one.</summary>
    [Fact]
    public void IsSupported_NoConfiguredName_ReportsUnsupported()
    {
        // Act, Assert
        Assert.False(PostgresTextSearchConfiguration.IsSupported(null));
    }

    /// <summary>The reported set names every alternative, because that message is what an operator fixes the setting from.</summary>
    [Fact]
    public void SupportedNames_ReportedToAnOperator_ContainsTheShippedConfigurations()
    {
        // Act
        var supportedNames = PostgresTextSearchConfiguration.SupportedNames;

        // Assert
        Assert.Contains("simple", supportedNames);
        Assert.Contains("english", supportedNames);
        Assert.Equal(supportedNames.Distinct(StringComparer.Ordinal).Count(), supportedNames.Count);
        // PostgreSQL folds an unquoted identifier to lower case, so a name written any other way would not be the one
        // an operator finds in pg_ts_config.
        Assert.All(supportedNames, name => Assert.DoesNotContain(name, char.IsUpper));
    }
}
