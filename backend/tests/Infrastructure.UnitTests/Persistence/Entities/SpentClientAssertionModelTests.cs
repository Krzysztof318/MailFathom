// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Entities;

/// <summary>
/// Asserts the shape of the model the served client assertions are recorded in. The model is built in memory by the
/// real PostgreSQL provider and no connection is opened, so this states what the key and the index are declared to be;
/// what PostgreSQL then does when two replicas present one identifier at once is an integration question.
/// </summary>
public sealed class SpentClientAssertionModelTests
{
    /// <summary>
    /// The key is the anti-replay rule rather than a storage detail: the spend is an insert that either happens or
    /// conflicts, so a key over anything but the credential and the identifier together would either admit a replay or
    /// refuse an identifier its client was entitled to choose.
    /// </summary>
    [Fact]
    public void SpentClientAssertionModel_TheKeyThatRefusesAReplay_CoversTheCredentialAndTheIdentifierTogether()
    {
        // Arrange
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);

        // Act
        var key = SpentAssertionEntityType(context).FindPrimaryKey();

        // Assert
        Assert.NotNull(key);
        Assert.Equal(["CredentialKey", "Identifier"], key.Properties.Select(property => property.Name));
    }

    /// <summary>
    /// The removal is the only reader of the expiry, and its name is stated so the mapping and
    /// <see cref="PersistenceConstraintNames" /> cannot drift apart. Without the index the removal would scan the whole
    /// table rather than the rows that have expired since the last one.
    /// </summary>
    [Fact]
    public void SpentClientAssertionModel_TheExpiryTheRemovalWalks_IsIndexedUnderTheStatedName()
    {
        // Arrange
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);

        // Act
        var index = SpentAssertionEntityType(context).GetIndexes().SingleOrDefault(
            candidate => candidate.Properties.Select(property => property.Name).SequenceEqual(["ExpiresAt"]));

        // Assert
        Assert.NotNull(index);
        Assert.Equal(PersistenceConstraintNames.SpentClientAssertionExpiryIndexName, index.GetDatabaseName());
    }

    /// <summary>
    /// Both columns are bounded, because both hold a value a client chose: an identifier the client minted, and — for a
    /// user's registered key — the fingerprint it named. An unbounded column would let a verified client write as much
    /// per request as it liked into a table this deployment sweeps rather than caps.
    /// </summary>
    [Fact]
    public void SpentClientAssertionModel_TheTwoColumnsAClientSuppliesValuesFor_AreBounded()
    {
        // Arrange
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);
        var entityType = SpentAssertionEntityType(context);

        // Act
        var credentialKeyLength = entityType.FindProperty(nameof(SpentClientAssertionEntity.CredentialKey))!.GetMaxLength();
        var identifierLength = entityType.FindProperty(nameof(SpentClientAssertionEntity.Identifier))!.GetMaxLength();

        // Assert
        Assert.Equal(SpentClientAssertionEntity.CredentialKeyLengthLimit, credentialKeyLength);
        Assert.Equal(SpentClientAssertionEntity.IdentifierLengthLimit, identifierLength);
    }

    /// <summary>Reads the design-time model, for the reason the stored email's own model tests do.</summary>
    private static IEntityType SpentAssertionEntityType(MailFathomDbContext context) =>
        context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(SpentClientAssertionEntity))!;
}
