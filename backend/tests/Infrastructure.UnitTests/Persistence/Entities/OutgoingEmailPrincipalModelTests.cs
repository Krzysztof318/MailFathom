// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Delivery;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Entities;

/// <summary>
/// Asserts the shape of the column a send's principal is stored in. The model is built in memory by the real PostgreSQL
/// provider and no connection is opened, so this states what the schema is generated from; whether a value then
/// survives the round trip is an integration question.
/// </summary>
public sealed class OutgoingEmailPrincipalModelTests
{
    /// <summary>The column's width and the value's own are one decision, and a migration is generated from this half of it.</summary>
    /// <remarks>
    /// The value object refuses anything but a fixed-width fingerprint, so a column that stopped stating the width
    /// would not lose data — it would quietly become an unbounded one in the next migration generated from this model,
    /// which is a schema change nobody decided and which no test of the value object could report.
    /// </remarks>
    [Fact]
    public void OutgoingEmailModel_ThePrincipalColumn_IsBoundedByTheFingerprintWidth()
    {
        // Act
        var principal = PrincipalProperty();

        // Assert
        Assert.Equal(OutgoingEmailPrincipal.FingerprintLength, principal.GetMaxLength());
    }

    /// <summary>A row written before the column existed carries nothing, so the column has to admit that rather than refuse it.</summary>
    /// <remarks>
    /// The migration is additive over a table a deployment already holds sends in, so requiring a value would have
    /// meant inventing one for every existing row — and any value invented there would have named a caller that never
    /// queued it. Absent is the one honest answer, and it matches nobody.
    /// </remarks>
    [Fact]
    public void OutgoingEmailModel_ThePrincipalColumn_AdmitsARowWrittenBeforeItExisted()
    {
        // Act
        var principal = PrincipalProperty();

        // Assert
        Assert.True(principal.IsNullable);
    }

    private static IProperty PrincipalProperty()
    {
        using var context = CreateContext();

        var property = context.GetService<IDesignTimeModel>()
            .Model
            .FindEntityType(typeof(OutgoingEmailEntity))!
            .FindProperty(nameof(OutgoingEmailEntity.PrincipalFingerprint));

        Assert.NotNull(property);

        return property;
    }

    private static MailFathomDbContext CreateContext() =>
        new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);
}
