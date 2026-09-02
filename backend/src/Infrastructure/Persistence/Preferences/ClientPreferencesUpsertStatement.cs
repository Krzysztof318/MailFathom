// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore.Metadata;

namespace MailFathom.Infrastructure.Persistence.Preferences;

/// <summary>Composes the one statement a person's client preferences are written by.</summary>
/// <remarks>
/// <para>
/// It is composed here rather than inside the store so that what it says is readable without a database, which is the
/// only place the guarantees below are visible at all: the conflict target is what makes a second device's write
/// replace the first rather than violate the key, and selecting from the owner table is what makes an owner this
/// deployment no longer holds affect no row instead of raising a foreign-key violation.
/// </para>
/// <para>
/// Every identifier comes from the model, so a renamed column stops the build rather than producing a statement
/// PostgreSQL refuses inside a request. Everything else in it is a parameter, so there is nothing here for a value a
/// caller supplied to reach; the cast on the document is what tells PostgreSQL the text parameter is the
/// <c>jsonb</c> the column holds.
/// </para>
/// </remarks>
internal static class ClientPreferencesUpsertStatement
{
    /// <summary>Composes the statement, with the owner, the document, and the instant as its three parameters in that order.</summary>
    /// <param name="model">The model the schema is generated from.</param>
    /// <returns>The statement, carrying <c>{0}</c> for the owner, <c>{1}</c> for the document, and <c>{2}</c> for the instant.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="model" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the model does not map the preferences row or the owner row.</exception>
    public static string Compose(IModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var preferences = PersistedSchemaNames.EntityTypeOf<ClientPreferencesEntity>(model);
        var owners = PersistedSchemaNames.EntityTypeOf<OwnerAccountEntity>(model);

        var ownerColumn = PersistedSchemaNames.QuotedColumn(preferences, nameof(ClientPreferencesEntity.OwnerId));
        var documentColumn = PersistedSchemaNames.QuotedColumn(preferences, nameof(ClientPreferencesEntity.Document));
        var createdColumn = PersistedSchemaNames.QuotedColumn(preferences, nameof(ClientPreferencesEntity.CreatedAt));
        var updatedColumn = PersistedSchemaNames.QuotedColumn(preferences, nameof(ClientPreferencesEntity.UpdatedAt));

        return $$"""
            INSERT INTO {{PersistedSchemaNames.QuotedTable(preferences)}}
                ({{ownerColumn}}, {{documentColumn}}, {{createdColumn}}, {{updatedColumn}})
            SELECT {0}, {1}::jsonb, {2}, {2}
            FROM {{PersistedSchemaNames.QuotedTable(owners)}}
            WHERE {{PersistedSchemaNames.QuotedColumn(owners, nameof(OwnerAccountEntity.Id))}} = {0}
            ON CONFLICT ({{ownerColumn}}) DO UPDATE SET
                {{documentColumn}} = EXCLUDED.{{documentColumn}},
                {{updatedColumn}} = EXCLUDED.{{updatedColumn}}
            """;
    }
}
