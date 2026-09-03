// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore.Metadata;

namespace MailFathom.Infrastructure.Persistence.Portraits;

/// <summary>Composes the one statement a person's portrait is written by.</summary>
/// <remarks>
/// <para>
/// It is composed here rather than inside the store so that what it says is readable without a database, which is the
/// only place the guarantees below are visible at all: the conflict target is what makes a second device's write
/// replace the first rather than violate the key, and selecting from the owner table is what makes an owner this
/// deployment no longer holds affect no row instead of raising a foreign-key violation.
/// </para>
/// <para>
/// Every identifier comes from the model, so a renamed column stops the build rather than producing a statement
/// PostgreSQL refuses inside a request. Everything else in it is a parameter, so there is nothing here for octets a
/// caller supplied to reach.
/// </para>
/// </remarks>
internal static class OwnerPortraitUpsertStatement
{
    /// <summary>Composes the statement, with the owner, the octets, and the instant as its three parameters in that order.</summary>
    /// <param name="model">The model the schema is generated from.</param>
    /// <returns>The statement, carrying <c>{0}</c> for the owner, <c>{1}</c> for the octets, and <c>{2}</c> for the instant.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="model" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the model does not map the portrait row or the owner row.</exception>
    public static string Compose(IModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var portraits = PersistedSchemaNames.EntityTypeOf<OwnerPortraitEntity>(model);
        var owners = PersistedSchemaNames.EntityTypeOf<OwnerAccountEntity>(model);

        var ownerColumn = PersistedSchemaNames.QuotedColumn(portraits, nameof(OwnerPortraitEntity.OwnerId));
        var contentColumn = PersistedSchemaNames.QuotedColumn(portraits, nameof(OwnerPortraitEntity.Content));
        var createdColumn = PersistedSchemaNames.QuotedColumn(portraits, nameof(OwnerPortraitEntity.CreatedAt));
        var updatedColumn = PersistedSchemaNames.QuotedColumn(portraits, nameof(OwnerPortraitEntity.UpdatedAt));

        return $$"""
            INSERT INTO {{PersistedSchemaNames.QuotedTable(portraits)}}
                ({{ownerColumn}}, {{contentColumn}}, {{createdColumn}}, {{updatedColumn}})
            SELECT {0}, {1}, {2}, {2}
            FROM {{PersistedSchemaNames.QuotedTable(owners)}}
            WHERE {{PersistedSchemaNames.QuotedColumn(owners, nameof(OwnerAccountEntity.Id))}} = {0}
            ON CONFLICT ({{ownerColumn}}) DO UPDATE SET
                {{contentColumn}} = EXCLUDED.{{contentColumn}},
                {{updatedColumn}} = EXCLUDED.{{updatedColumn}}
            """;
    }
}
