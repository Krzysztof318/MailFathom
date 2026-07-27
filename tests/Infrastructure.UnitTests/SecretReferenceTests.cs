// Copyright © 2026 Krzysztof Kasprowicz

using System.Text;
using MailMcp.Infrastructure.Secrets;
using Xunit;

namespace MailMcp.Infrastructure.UnitTests;

public sealed class SecretReferenceTests
{
    [Theory]
    [InlineData("systemd-credential:imap-primary-password", "systemd-credential", "imap-primary-password")]
    [InlineData("file:/run/secrets/imap", "file", "/run/secrets/imap")]
    [InlineData("env:MAILMCP_IMAP_PASSWORD", "env", "MAILMCP_IMAP_PASSWORD")]
    [InlineData("plaintext:dev-password", "plaintext", "dev-password")]
    public void TryParse_SupportedScheme_ParsesSchemeAndTarget(
        string configuredValue,
        string expectedScheme,
        string expectedTarget)
    {
        // Act
        var parsed = SecretReference.TryParse(configuredValue, out var reference, out _);

        // Assert
        Assert.True(parsed);
        Assert.Equal(expectedScheme, reference!.Scheme.Name);
        Assert.Equal(expectedTarget, reference.Target);
    }

    [Fact]
    public void TryParse_TargetContainsColon_KeepsEverythingAfterTheFirstColon()
    {
        // Act
        Assert.True(SecretReference.TryParse(@"file:C:\secrets\imap", out var reference, out _));

        // Assert
        Assert.Equal(@"C:\secrets\imap", reference!.Target);
    }

    [Fact]
    public void TryParse_MixedCaseScheme_ParsesScheme()
    {
        // Act
        Assert.True(SecretReference.TryParse("File:/run/secrets/imap", out var reference, out _));

        // Assert
        Assert.Equal(SecretReferenceScheme.File, reference!.Scheme);
    }

    [Fact]
    public void TryParse_WhitespaceAroundTheScheme_TrimsTheSchemeOnly()
    {
        // Act
        Assert.True(SecretReference.TryParse(" file :/run/secrets/imap", out var reference, out _));

        // Assert
        Assert.Equal(SecretReferenceScheme.File, reference!.Scheme);
        Assert.Equal("/run/secrets/imap", reference.Target);
    }

    [Fact]
    public void TryParse_PlaintextTargetWithLeadingAndTrailingSpaces_KeepsEverySpace()
    {
        // Act
        Assert.True(SecretReference.TryParse("plaintext: secret ", out var reference, out _));

        // Assert
        Assert.Equal(" secret ", reference!.Target);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParse_NullOrWhitespace_FailsWithReferenceMissing(string? configuredValue)
    {
        // Act
        var parsed = SecretReference.TryParse(configuredValue, out var reference, out var failure);

        // Assert
        Assert.False(parsed);
        Assert.Null(reference);
        Assert.Equal(SecretResolutionFailure.ReferenceMissing, failure);
    }

    [Fact]
    public void TryParse_NoColon_FailsWithSchemeMissing()
    {
        // Act
        var parsed = SecretReference.TryParse("a-perfectly-ordinary-password", out _, out var failure);

        // Assert
        Assert.False(parsed);
        Assert.Equal(SecretResolutionFailure.SchemeMissing, failure);
    }

    [Fact]
    public void TryParse_EmptyScheme_FailsWithSchemeMissing()
    {
        // Act
        var parsed = SecretReference.TryParse(":/run/secrets/imap", out _, out var failure);

        // Assert
        Assert.False(parsed);
        Assert.Equal(SecretResolutionFailure.SchemeMissing, failure);
    }

    [Fact]
    public void TryParse_UnregisteredScheme_ParsesBecauseSupportIsADispatchQuestion()
    {
        // Act
        var parsed = SecretReference.TryParse("azure-key-vault:imap", out var reference, out _);

        // Assert
        Assert.True(parsed);
        Assert.Equal("azure-key-vault", reference!.Scheme.Name);
    }

    [Fact]
    public void TryParse_EmptyTarget_FailsWithTargetMissing()
    {
        // Act
        var parsed = SecretReference.TryParse("file:", out _, out var failure);

        // Assert
        Assert.False(parsed);
        Assert.Equal(SecretResolutionFailure.TargetMissing, failure);
    }

    [Fact]
    public void TryParse_UrlTarget_KeepsTheWholeUrl()
    {
        // Act
        Assert.True(SecretReference.TryParse("azure-key-vault:https://vault.example/secrets/imap", out var reference, out _));

        // Assert
        Assert.Equal("https://vault.example/secrets/imap", reference!.Target);
    }

    [Fact]
    public void ToString_SecretReference_ContainsNeitherTheTargetNorAPlaintextSecret()
    {
        // Arrange
        Assert.True(SecretReference.TryParse("plaintext:top-secret", out var reference, out _));

        // Act
        var printed = reference!.ToString();

        // Assert
        Assert.DoesNotContain("top-secret", printed, StringComparison.Ordinal);
        Assert.Equal("plaintext:***", printed);
    }

    [Fact]
    public void Create_UnknownSchemeName_NormalizesItToLowerCase()
    {
        // Act
        var scheme = SecretReferenceScheme.Create(" Azure-Key-Vault ");

        // Assert
        Assert.Equal("azure-key-vault", scheme.Name);
        Assert.Equal("azure-key-vault", scheme.ToString());
    }

    [Fact]
    public void Create_BlankSchemeName_Throws()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => SecretReferenceScheme.Create("  "));
    }

    [Fact]
    public void ToString_ResolvedSecret_DoesNotContainTheMaterial()
    {
        // Arrange
        using var secret = ResolvedSecret.FromText("top-secret");

        // Act
        var printed = secret.ToString();

        // Assert
        Assert.Equal("***", printed);
    }

    [Fact]
    public void RevealAsString_ResolvedSecret_ReturnsTheUtf8TextView()
    {
        // Arrange
        using var secret = ResolvedSecret.FromBytes("hasło-poczty"u8);

        // Act
        var revealed = secret.RevealAsString();

        // Assert
        Assert.Equal("hasło-poczty", revealed);
    }

    [Theory]
    [InlineData("secret\n")]
    [InlineData("secret\r\n")]
    public void RevealAsString_MaterialEndsWithNewline_StripsOneTrailingNewline(string material)
    {
        // Arrange
        using var secret = ResolvedSecret.FromBytes(Encoding.UTF8.GetBytes(material));

        // Act
        var revealed = secret.RevealAsString();

        // Assert
        Assert.Equal("secret", revealed);
    }

    [Fact]
    public void RevealBytes_BinaryMaterial_ReturnsEveryByteUnchanged()
    {
        // Arrange
        byte[] bundle = [0x30, 0x82, 0x00, 0xFF, 0x0A, 0x00];
        using var secret = ResolvedSecret.FromBytes(bundle);

        // Act
        var revealed = secret.RevealBytes().ToArray();

        // Assert
        Assert.Equal(bundle, revealed);
    }

    [Fact]
    public void RevealBytes_MaterialEndsWithNewline_DoesNotStripIt()
    {
        // Arrange
        using var secret = ResolvedSecret.FromBytes("der\n"u8);

        // Act
        var revealed = secret.RevealBytes().ToArray();

        // Assert
        Assert.Equal("der\n"u8.ToArray(), revealed);
    }

    [Fact]
    public void Dispose_ResolvedSecret_ZeroesTheMaterial()
    {
        // Arrange
        var secret = ResolvedSecret.FromText("top-secret");

        // Act
        secret.Dispose();

        // Assert
        Assert.True(secret.IsMaterialErased);
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        // Arrange
        var secret = ResolvedSecret.FromText("top-secret");

        // Act
        secret.Dispose();
        secret.Dispose();

        // Assert
        Assert.True(secret.IsMaterialErased);
    }

    [Fact]
    public void RevealBytes_AfterDispose_Throws()
    {
        // Arrange
        var secret = ResolvedSecret.FromText("top-secret");
        secret.Dispose();

        // Act, Assert
        Assert.Throws<ObjectDisposedException>(() => secret.RevealAsString());
        Assert.Throws<ObjectDisposedException>(() => secret.RevealBytes().Length);
    }

    [Fact]
    public void ToString_SecretResolutionResult_DoesNotContainTheMaterial()
    {
        // Arrange
        using var secret = ResolvedSecret.FromText("top-secret");
        var result = SecretResolutionResult.Resolved(secret, SecretMaterialSource.SchemeAdapter);

        // Act
        var printed = result.ToString();

        // Assert
        Assert.DoesNotContain("top-secret", printed, StringComparison.Ordinal);
    }

    [Fact]
    public void Failed_SecretResolutionResult_CarriesNoMaterialAndNoSource()
    {
        // Act
        var result = SecretResolutionResult.Failed(SecretResolutionFailure.MaterialNotFound);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Null(result.Secret);
        Assert.Null(result.Source);
        Assert.Equal(SecretResolutionFailure.MaterialNotFound, result.Failure);
    }
}
