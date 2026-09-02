// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Infrastructure.DataEncryption;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.DataEncryption;

/// <summary>Covers the identity a sealed value is authenticated against, which the database holds for the life of the row.</summary>
/// <remarks>
/// The assertions run against <see cref="DataEncryptionPurpose.All" /> rather than over the type's members by
/// reflection, so a member added without being listed is a failure here rather than a value nothing can enumerate.
/// </remarks>
public sealed class DataEncryptionPurposeTests
{
    [Fact]
    public void All_EveryPurpose_DeclaresADistinctIdentity()
    {
        // Arrange
        var identities = DataEncryptionPurpose.All.Select(purpose => purpose.Identity);

        // Act
        var distinctIdentities = identities.Distinct(StringComparer.Ordinal);

        // Assert
        Assert.Equal(DataEncryptionPurpose.All.Count, distinctIdentities.Count());
    }

    [Fact]
    public void Identity_MailboxRefreshToken_IsTheIdentityStoredValuesWereSealedUnder()
    {
        // Assert — the literal is the point of the test. Changing it makes every sealed value fail to open, so it is
        // asserted here rather than derived from the member, which a rename would carry along silently.
        Assert.Equal("mailbox-refresh-token", DataEncryptionPurpose.MailboxRefreshToken.Identity);
    }

    [Fact]
    public void Identity_StoredSecret_IsTheIdentityDatabaseBackedSecretsAreSealedUnder()
    {
        // Assert — changing this literal would make every secret already stored in the database fail to open.
        Assert.Equal("stored-secret", DataEncryptionPurpose.StoredSecret.Identity);
    }

    [Fact]
    public void TryParse_ADeclaredIdentity_YieldsThatPurpose()
    {
        // Act
        var parsed = DataEncryptionPurpose.TryParse("mailbox-refresh-token", out var purpose);

        // Assert
        Assert.True(parsed);
        Assert.Equal(DataEncryptionPurpose.MailboxRefreshToken, purpose);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("mailbox-access-token")]
    [InlineData("Mailbox-Refresh-Token")]
    public void TryParse_AnIdentityNothingDeclares_YieldsTheUnspecifiedDefault(string? identity)
    {
        // Act
        var parsed = DataEncryptionPurpose.TryParse(identity, out var purpose);

        // Assert — the comparison is exact because the identity is authenticated material rather than operator input,
        // so a difference in case is a different identity and not a spelling to forgive.
        Assert.False(parsed);
        Assert.False(purpose.IsSpecified);
    }

    [Fact]
    public void Identity_TheUnspecifiedDefault_RefusesToNameAPurpose()
    {
        // Arrange
        var unspecified = default(DataEncryptionPurpose);

        // Assert
        Assert.False(unspecified.IsSpecified);
        Assert.Throws<InvalidOperationException>(() => unspecified.Identity);
        Assert.Equal("(unspecified)", unspecified.ToString());
    }

    [Fact]
    public void Serialization_ADeclaredPurpose_RoundTripsThroughItsIdentity()
    {
        // Act
        var json = JsonSerializer.Serialize(DataEncryptionPurpose.MailboxRefreshToken);

        // Assert
        Assert.Equal("\"mailbox-refresh-token\"", json);
        Assert.Equal(
            DataEncryptionPurpose.MailboxRefreshToken,
            JsonSerializer.Deserialize<DataEncryptionPurpose>(json));
    }

    [Fact]
    public void Serialization_AsAPropertyName_RoundTripsThroughItsIdentity()
    {
        // Arrange
        var byPurpose = new Dictionary<DataEncryptionPurpose, int> { [DataEncryptionPurpose.MailboxRefreshToken] = 1 };

        // Act
        var json = JsonSerializer.Serialize(byPurpose);

        // Assert
        Assert.Equal("{\"mailbox-refresh-token\":1}", json);
        Assert.Equal(
            byPurpose,
            JsonSerializer.Deserialize<Dictionary<DataEncryptionPurpose, int>>(json));
    }

    [Fact]
    public void Serialization_TheUnspecifiedDefault_IsRefused() =>
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(default(DataEncryptionPurpose)));

    [Theory]
    [InlineData("\"mailbox-access-token\"")]
    [InlineData("7")]
    public void Deserialization_AValueNothingDeclares_IsRefused(string json) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DataEncryptionPurpose>(json));
}
