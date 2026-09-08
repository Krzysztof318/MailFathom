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
/// replace the first rather than violate the key, and selecting from the user table is what makes a user this
/// deployment no longer holds affect no row instead of raising a foreign-key violation.
/// </para>
/// <para>
/// Every identifier comes from the model, so a renamed column stops the build rather than producing a statement
/// PostgreSQL refuses inside a request. Everything else in it is a parameter, so there is nothing here for octets a
/// caller supplied to reach.
/// </para>
/// </remarks>
internal static class UserPortraitUpsertStatement
{
    /// <summary>Composes the statement, with the user, the octets, and the instant as its three parameters in that order.</summary>
    /// <param name="model">The model the schema is generated from.</param>
    /// <returns>The statement, carrying <c>{0}</c> for the user, <c>{1}</c> for the octets, and <c>{2}</c> for the instant.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="model" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the model does not map the portrait row or the user row.</exception>
    public static string Compose(IModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var portraits = PersistedSchemaNames.EntityTypeOf<UserPortraitEntity>(model);
        var users = PersistedSchemaNames.EntityTypeOf<UserAccountEntity>(model);

        var userColumn = PersistedSchemaNames.QuotedColumn(portraits, nameof(UserPortraitEntity.UserId));
        var contentColumn = PersistedSchemaNames.QuotedColumn(portraits, nameof(UserPortraitEntity.Content));
        var createdColumn = PersistedSchemaNames.QuotedColumn(portraits, nameof(UserPortraitEntity.CreatedAt));
        var updatedColumn = PersistedSchemaNames.QuotedColumn(portraits, nameof(UserPortraitEntity.UpdatedAt));

        return $$"""
            INSERT INTO {{PersistedSchemaNames.QuotedTable(portraits)}}
                ({{userColumn}}, {{contentColumn}}, {{createdColumn}}, {{updatedColumn}})
            SELECT {0}, {1}, {2}, {2}
            FROM {{PersistedSchemaNames.QuotedTable(users)}}
            WHERE {{PersistedSchemaNames.QuotedColumn(users, nameof(UserAccountEntity.Id))}} = {0}
            ON CONFLICT ({{userColumn}}) DO UPDATE SET
                {{contentColumn}} = EXCLUDED.{{contentColumn}},
                {{updatedColumn}} = EXCLUDED.{{updatedColumn}}
            """;
    }
}
