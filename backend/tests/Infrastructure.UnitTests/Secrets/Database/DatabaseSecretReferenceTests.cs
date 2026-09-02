// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Secrets.Database;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Secrets.Database;

public sealed class DatabaseSecretReferenceTests
{
    private static readonly Guid StoredSecretId = new("019925df-96f4-7c6d-8f91-b9f6cf27f5b2");

    [Fact]
    public void ConfigurationValue_AStoredSecret_UsesTheDatabaseSchemeAndCanonicalIdentifier()
    {
        // Arrange
        var reference = DatabaseSecretReference.Create(StoredSecretId);

        // Assert
        Assert.Equal($"database:{StoredSecretId:D}", reference.ConfigurationValue);
    }

    [Fact]
    public void TryParse_ItsConfigurationValue_RoundTripsTheReference()
    {
        // Arrange
        var reference = DatabaseSecretReference.Create(StoredSecretId);

        // Act
        var parsed = DatabaseSecretReference.TryParse(reference.ConfigurationValue, out var roundTrip);

        // Assert
        Assert.True(parsed);
        Assert.Equal(reference, roundTrip);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("file:019925df-96f4-7c6d-8f91-b9f6cf27f5b2")]
    [InlineData("database:not-an-identifier")]
    [InlineData("database:00000000-0000-0000-0000-000000000000")]
    public void TryParse_AnythingButADatabaseReference_YieldsTheUnspecifiedDefault(string? configuredValue)
    {
        // Act
        var parsed = DatabaseSecretReference.TryParse(configuredValue, out var reference);

        // Assert
        Assert.False(parsed);
        Assert.False(reference.IsSpecified);
    }

    [Fact]
    public void ToString_AStoredSecret_PrintsNeitherTheIdentifierNorTheTarget()
    {
        // Arrange
        var reference = DatabaseSecretReference.Create(StoredSecretId);

        // Assert
        Assert.Equal("database:***", reference.ToString());
    }
}
