// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Common;
using MailFathom.Infrastructure.DataEncryption;
using MailFathom.Infrastructure.Secrets.Resolution;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.DataEncryption;

/// <summary>Covers turning what an operator provisioned into a key, and refusing what is not one.</summary>
/// <remarks>
/// The refusals are what move a mistyped key from the first read of a sealed value to startup, so each is asserted
/// against the reason it reports rather than merely against failing.
/// </remarks>
public sealed class DataEncryptionKeyTests
{
    [Fact]
    public void Decode_Base64OfThirtyTwoBytes_YieldsAKeyNamedByItsIdentifier()
    {
        // Arrange
        using var material = TextMaterial(Convert.ToBase64String(new byte[AesGcmEnvelope.KeySizeInBytes]));

        // Act
        using var key = DataEncryptionKey.Decode("2026-08", material, out var failure);

        // Assert
        Assert.NotNull(key);
        Assert.Null(failure);
        Assert.Equal("2026-08", key.KeyId);
    }

    [Fact]
    public void Decode_MaterialCarryingATrailingNewline_YieldsAKey()
    {
        // Arrange — every channel that delivers the key writes it as a text file, and a text file routinely ends with a
        // newline. Refusing one would make the documented provisioning steps fail for a reason nobody could see.
        using var material = TextMaterial(Convert.ToBase64String(new byte[AesGcmEnvelope.KeySizeInBytes]) + "\n");

        // Act
        using var key = DataEncryptionKey.Decode("2026-08", material, out var failure);

        // Assert
        Assert.NotNull(key);
        Assert.Null(failure);
    }

    [Fact]
    public void Decode_MaterialThatIsNotBase64_ReportsThat()
    {
        // Arrange
        using var material = TextMaterial("not base64 at all!!");

        // Act
        using var key = DataEncryptionKey.Decode("2026-08", material, out var failure);

        // Assert
        Assert.Null(key);
        Assert.Equal(DataEncryptionKeyMaterialFailure.NotBase64, failure);
    }

    [Fact]
    public void Decode_ThirtyThreeBytesOfBase64_ReportsTheWrongLength()
    {
        // Arrange — this is the mistake the documentation exists to prevent: `openssl rand -base64 33` is the command
        // beside this one in a Compose deployment, and it is right for the database passwords and wrong for a key.
        using var material = TextMaterial(Convert.ToBase64String(new byte[AesGcmEnvelope.KeySizeInBytes + 1]));

        // Act
        using var key = DataEncryptionKey.Decode("2026-08", material, out var failure);

        // Assert
        Assert.Null(key);
        Assert.Equal(DataEncryptionKeyMaterialFailure.WrongLength, failure);
    }

    [Fact]
    public void Decode_Base64OfTooFewBytes_ReportsTheWrongLength()
    {
        // Arrange
        using var material = TextMaterial(Convert.ToBase64String(new byte[16]));

        // Act
        using var key = DataEncryptionKey.Decode("2026-08", material, out var failure);

        // Assert
        Assert.Null(key);
        Assert.Equal(DataEncryptionKeyMaterialFailure.WrongLength, failure);
    }

    [Fact]
    public void Decode_TheCallerOwnedMaterial_IsLeftUsable()
    {
        // Arrange — startup decodes every key and reports on all of them, so decoding one must not erase the material
        // the caller still owns.
        using var material = TextMaterial(Convert.ToBase64String(new byte[AesGcmEnvelope.KeySizeInBytes]));

        // Act
        using var key = DataEncryptionKey.Decode("2026-08", material, out _);

        // Assert
        Assert.NotNull(key);
        Assert.NotEmpty(material.RevealBytes().ToArray());
    }

    private static ResolvedSecret TextMaterial(string material) =>
        ResolvedSecret.FromBytes(Encoding.UTF8.GetBytes(material));
}
