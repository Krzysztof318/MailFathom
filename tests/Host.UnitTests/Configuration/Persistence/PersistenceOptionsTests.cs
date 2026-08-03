// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using MailFathom.Host.Configuration.Persistence;
using MailFathom.Infrastructure.Persistence.Connections;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Persistence;

public sealed class PersistenceOptionsTests
{
    /// <summary>A deployment that configures nothing gets the configuration that stems nothing.</summary>
    [Fact]
    public void Validate_UnconfiguredDeployment_AcceptsTheDefaultTextSearchConfiguration()
    {
        // Arrange
        var options = new PersistenceOptions();

        // Act
        var results = Validate(options);

        // Assert
        Assert.Empty(results);
        Assert.Equal(PostgresTextSearchConfiguration.Default.Value, options.TextSearchConfiguration);
    }

    [Fact]
    public void Validate_SupportedTextSearchConfiguration_ReportsNoError()
    {
        // Arrange
        var options = new PersistenceOptions { TextSearchConfiguration = "english" };

        // Act
        var results = Validate(options);

        // Assert
        Assert.Empty(results);
    }

    /// <summary>The name is compiled into a generated column, so an unknown one fails startup instead of the schema.</summary>
    [Theory]
    [InlineData("klingon")]
    [InlineData("English")]
    [InlineData("")]
    public void Validate_UnknownTextSearchConfiguration_FailsStartupAndNamesTheAlternatives(string configuredName)
    {
        // Arrange
        var options = new PersistenceOptions { TextSearchConfiguration = configuredName };

        // Act
        var results = Validate(options);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal([nameof(PersistenceOptions.TextSearchConfiguration)], result.MemberNames);
        Assert.Contains("Supported configurations are", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("simple", result.ErrorMessage, StringComparison.Ordinal);
    }

    private static ValidationResult[] Validate(PersistenceOptions options) =>
        [.. options.Validate(new ValidationContext(options))];
}
