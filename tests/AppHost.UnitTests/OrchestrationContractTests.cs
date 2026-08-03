// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Xunit;

namespace MailFathom.AppHost.UnitTests;

/// <summary>Covers the two values the app model states rather than derives: the ephemeral resource name, and the development data-encryption key.</summary>
/// <remarks>
/// What the naming assertions establish is what the removal at the end of a run depends on. A prefix that did not start
/// with the shared part would leave a run's resources outside every filter that finds them, and an identifier a
/// container runtime refuses would fail the run at a point that says nothing about why.
/// <para>
/// The key assertion establishes something the compiler cannot: the constant is written into a <c>plaintext:</c> secret
/// reference, so a mistyped character or a change to the text behind it would first be reported by a developer's own
/// orchestration failing its startup validation, long after the edit.
/// </para>
/// </remarks>
public sealed class OrchestrationContractTests
{
    private const string GeneratedIdentifierSeparator = "-";

    /// <summary>The key length AES-256 takes, stated here because this project compiles one source file and reaches no shared constant.</summary>
    private const int DataEncryptionKeySizeInBytes = 32;

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
    /// <remarks>
    /// The one assertion here that reads a random value rather than its shape. Four bytes make a repeat a
    /// one-in-four-billion event, so this is not the nondeterminism the unit-test policy excludes; uniqueness is the
    /// property the identifier exists for, and a test that never observed two values could not state it.
    /// </remarks>
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

    /// <summary>The development key has to be what the host's own startup validation accepts, or no local run seals anything.</summary>
    [Fact]
    public void DataEncryptionKeyMaterial_TheStatedDevelopmentKey_IsBase64OfExactlyTheAcceptedKeyLength()
    {
        // Act
        var decoded = Convert.FromBase64String(OrchestrationContract.DataEncryptionKeyMaterial);

        // Assert
        Assert.Equal(DataEncryptionKeySizeInBytes, decoded.Length);
    }

    /// <summary>The ring's active identifier has to name a key the ring configures, which locally means the one key there is.</summary>
    /// <remarks>
    /// The app model writes this constant into both <c>DataEncryption__ActiveKeyId</c> and
    /// <c>DataEncryption__Keys__0__KeyId</c>, so the two agreeing is what stops a local run from being refused for
    /// naming an active key nothing configures.
    /// </remarks>
    [Fact]
    public void DataEncryptionKeyId_TheStatedIdentifier_IsOneTheConfigurationValidatorAccepts()
    {
        // Assert — the accepted shape is an alphanumeric first character followed by up to 63 more of the same plus
        // `.`, `_`, and `-`. It is restated rather than shared because this project compiles one source file.
        Assert.Matches("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", OrchestrationContract.DataEncryptionKeyId);
    }

    private static string IdentifierOf(string prefix) =>
        prefix[(OrchestrationContract.EphemeralResourceNamePrefix.Length + GeneratedIdentifierSeparator.Length)..];
}
