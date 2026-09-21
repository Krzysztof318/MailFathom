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
/// Asserts the shape a Discover run's journal is declared with. The model is built in memory by the real PostgreSQL
/// provider and no connection is opened, so what this states is the declaration a schema is generated from.
/// </summary>
public sealed class DiscoveryRunEventModelTests
{
    /// <summary>The payload column stores the document it was handed rather than a normalization of it.</summary>
    /// <remarks>
    /// An event is polymorphic and its type discriminator is read nowhere but first, while <c>jsonb</c> reorders the
    /// keys of everything it stores — so a column declared that way hands a block event back as a document no build can
    /// identify, and every run that composed one is lost on the way out. Asserted off the model because the difference
    /// appears only after a round trip through PostgreSQL, which is a dispatched suite rather than this gate.
    /// </remarks>
    [Fact]
    public void DiscoveryRunEventModel_Payload_IsStoredAsWrittenRatherThanNormalized()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var payload = EntityTypeOf<DiscoveryRunEventEntity>(context)
            .FindProperty(nameof(DiscoveryRunEventEntity.Payload));

        // Assert
        Assert.NotNull(payload);
        Assert.Equal("json", payload.GetColumnType());
        Assert.False(payload.IsNullable);
    }

    /// <summary>
    /// Reads the design-time model rather than <c>DbContext.Model</c>, because the runtime model is trimmed to what a
    /// query needs and throws for the configuration a schema is generated from.
    /// </summary>
    private static IEntityType EntityTypeOf<TEntity>(MailFathomDbContext context)
        where TEntity : class =>
        context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(TEntity))!;

    private static MailFathomDbContext CreateContext() =>
        new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);
}
