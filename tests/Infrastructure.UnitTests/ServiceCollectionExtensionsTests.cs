// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Infrastructure.Secrets;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailMcp.Infrastructure.UnitTests;

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
}
