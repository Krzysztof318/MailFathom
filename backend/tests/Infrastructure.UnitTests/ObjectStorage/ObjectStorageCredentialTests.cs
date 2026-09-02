// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.ObjectStorage;
using MailFathom.Infrastructure.Secrets.Resolution;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.ObjectStorage;

/// <summary>Covers the credential one request is signed with, and the buffers it erases when that request ends.</summary>
public sealed class ObjectStorageCredentialTests
{
    /// <summary>Both halves are secret-bearing, and both are read from material rather than from an appsettings string.</summary>
    [Fact]
    public void Create_ResolvedMaterial_RevealsBothHalves()
    {
        // Arrange
        var accessKeyIdMaterial = ResolvedSecret.FromText("AKIAEXAMPLEIDENTIFIER\n");
        var secretAccessKeyMaterial = ResolvedSecret.FromText("an-example-signing-secret\n");

        // Act
        using var credential = ObjectStorageCredential.Create(accessKeyIdMaterial, secretAccessKeyMaterial);

        // Assert
        Assert.Equal("AKIAEXAMPLEIDENTIFIER", credential.AccessKeyId);
        Assert.Equal("an-example-signing-secret", credential.SecretAccessKey);
    }

    /// <summary>The window a process dump could hold the key in is one operation, which is what disposal bounds it to.</summary>
    [Fact]
    public void Dispose_AReleasedCredential_ErasesBothBuffers()
    {
        // Arrange
        var accessKeyIdMaterial = ResolvedSecret.FromText("AKIAEXAMPLEIDENTIFIER");
        var secretAccessKeyMaterial = ResolvedSecret.FromText("an-example-signing-secret");
        var credential = ObjectStorageCredential.Create(accessKeyIdMaterial, secretAccessKeyMaterial);

        // Act
        credential.Dispose();

        // Assert
        Assert.Throws<ObjectDisposedException>(() => accessKeyIdMaterial.RevealAsString());
        Assert.Throws<ObjectDisposedException>(() => secretAccessKeyMaterial.RevealAsString());
    }

    /// <summary>A blank half is a credential the endpoint could not admit, and it must never be presented as one.</summary>
    [Theory]
    [InlineData("", "an-example-signing-secret")]
    [InlineData("   ", "an-example-signing-secret")]
    [InlineData("AKIAEXAMPLEIDENTIFIER", "")]
    [InlineData("AKIAEXAMPLEIDENTIFIER", "   ")]
    public void Create_ABlankHalf_IsRefusedAndReleasesBothBuffers(string accessKeyId, string secretAccessKey)
    {
        // Arrange
        var accessKeyIdMaterial = ResolvedSecret.FromText(accessKeyId);
        var secretAccessKeyMaterial = ResolvedSecret.FromText(secretAccessKey);

        // Act, Assert
        Assert.Throws<ArgumentException>(
            () => ObjectStorageCredential.Create(accessKeyIdMaterial, secretAccessKeyMaterial));
        Assert.Throws<ObjectDisposedException>(() => accessKeyIdMaterial.RevealAsString());
        Assert.Throws<ObjectDisposedException>(() => secretAccessKeyMaterial.RevealAsString());
    }

    [Fact]
    public void Create_MissingMaterial_IsRefused()
    {
        // Arrange
        using var material = ResolvedSecret.FromText("AKIAEXAMPLEIDENTIFIER");

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => ObjectStorageCredential.Create(null!, material));
        Assert.Throws<ArgumentNullException>(() => ObjectStorageCredential.Create(material, null!));
    }
}
