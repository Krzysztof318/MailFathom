// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using Xunit;

namespace MailFathom.AppHost.UnitTests;

/// <summary>Covers the name every ephemeral container and volume an integration run creates is built from.</summary>
/// <remarks>
/// What these assert is what the removal at the end of a run depends on. A prefix that did not start with the shared
/// part would leave a run's resources outside every filter that finds them, and an identifier a container runtime
/// refuses would fail the run at a point that says nothing about why.
/// </remarks>
public sealed class OrchestrationContractTests
{
    private const string GeneratedIdentifierSeparator = "-";

    /// <summary>A run that states no identifier still names its resources apart from every other run's.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveEphemeralResourceNamePrefix_NoIdentifierStated_GeneratesOneUsableInAContainerName(string? runIdentifier)
    {
        // Act
        var prefix = OrchestrationContract.ResolveEphemeralResourceNamePrefix(runIdentifier);

        // Assert
        var identifier = IdentifierOf(prefix);

        Assert.StartsWith(
            OrchestrationContract.EphemeralResourceNamePrefix + GeneratedIdentifierSeparator,
            prefix,
            StringComparison.Ordinal);
        Assert.Equal(8, identifier.Length);
        Assert.All(identifier, character => Assert.True(
            char.IsAsciiDigit(character) || character is >= 'a' and <= 'f',
            $"'{character}' is not a lowercase hexadecimal character, so the name is not one a container runtime accepts."));
    }

    /// <summary>Two runs that state nothing collide over nothing, which is the whole point of generating an identifier.</summary>
    [Fact]
    public void ResolveEphemeralResourceNamePrefix_CalledTwiceWithNoIdentifier_ProducesDifferentPrefixes()
    {
        // Act
        var first = OrchestrationContract.ResolveEphemeralResourceNamePrefix(null);
        var second = OrchestrationContract.ResolveEphemeralResourceNamePrefix(null);

        // Assert
        Assert.NotEqual(first, second);
    }

    /// <summary>A caller that states an identifier gets that one, so it can remove exactly what the run created.</summary>
    [Theory]
    [InlineData("a1b2c3d4", "a1b2c3d4")]
    [InlineData("  a1b2c3d4  ", "a1b2c3d4")]
    [InlineData("a", "a")]
    [InlineData("0123456789abcdef", "0123456789abcdef")]
    public void ResolveEphemeralResourceNamePrefix_UsableIdentifierStated_UsesItAfterTheSharedPrefix(
        string runIdentifier,
        string expectedIdentifier)
    {
        // Act
        var prefix = OrchestrationContract.ResolveEphemeralResourceNamePrefix(runIdentifier);

        // Assert
        Assert.Equal(
            $"{OrchestrationContract.EphemeralResourceNamePrefix}{GeneratedIdentifierSeparator}{expectedIdentifier}",
            prefix);
    }

    /// <summary>An identifier a container name cannot carry is refused rather than replaced.</summary>
    /// <remarks>
    /// Replacing it would name the resources under something the caller never learned, so the removal at the end of the
    /// run would match nothing and report success over everything it leaked.
    /// </remarks>
    [Theory]
    [InlineData("Bad Id!")]
    [InlineData("ABC123")]
    [InlineData("a1b2-c3d4")]
    [InlineData("a1b2_c3d4")]
    [InlineData("a1b2.c3d4")]
    [InlineData("zażółć")]
    [InlineData("01234567890123456")]
    public void ResolveEphemeralResourceNamePrefix_UnusableIdentifierStated_FailsNamingTheVariable(string runIdentifier)
    {
        // Act
        var failure = Assert.Throws<InvalidOperationException>(
            () => OrchestrationContract.ResolveEphemeralResourceNamePrefix(runIdentifier));

        // Assert
        Assert.Contains(OrchestrationContract.EphemeralRunIdentifierVariable, failure.Message, StringComparison.Ordinal);
    }

    private static string IdentifierOf(string prefix) =>
        prefix[(OrchestrationContract.EphemeralResourceNamePrefix.Length + GeneratedIdentifierSeparator.Length)..];
}
