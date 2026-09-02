// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace MailFathom.Infrastructure.Persistence;

/// <summary>Names tables and columns for the few statements this layer composes rather than translates.</summary>
/// <remarks>
/// A composed statement has to write identifiers as text, and text repeated beside a mapping is text that can drift
/// from it. Taking them from the model instead makes the statement and the schema one description: a renamed column
/// stops the build rather than producing a statement PostgreSQL refuses at run time. Values never pass through here —
/// they are parameters, which is what keeps a composed statement free of an injection seam.
/// </remarks>
internal static class PersistedSchemaNames
{
    /// <summary>Finds the entity type one mapped class is described by.</summary>
    /// <param name="model">The model the schema is generated from.</param>
    /// <typeparam name="TEntity">The mapped class.</typeparam>
    /// <returns>The entity type.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="model" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the model maps no such class.</exception>
    public static IEntityType EntityTypeOf<TEntity>(IModel model)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(model);

        return model.FindEntityType(typeof(TEntity))
            ?? throw new InvalidOperationException(
                $"The model holds no {typeof(TEntity).Name}, so no statement can be composed against its table.");
    }

    /// <summary>Quotes a table name the model states, schema included where one is mapped.</summary>
    /// <param name="entityType">The entity type whose table is named.</param>
    /// <returns>The quoted table name.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="entityType" /> is <see langword="null" />.</exception>
    public static string QuotedTable(IEntityType entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);

        return entityType.GetSchema() is { Length: > 0 } schema
            ? $"\"{schema}\".\"{entityType.GetTableName()}\""
            : $"\"{entityType.GetTableName()}\"";
    }

    /// <summary>Quotes the column one mapped property is stored in.</summary>
    /// <param name="entityType">The entity type the property belongs to.</param>
    /// <param name="propertyName">The mapped property.</param>
    /// <returns>The quoted column name.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="entityType" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the entity type maps no such property.</exception>
    public static string QuotedColumn(IEntityType entityType, string propertyName)
    {
        ArgumentNullException.ThrowIfNull(entityType);

        var property = entityType.FindProperty(propertyName)
            ?? throw new InvalidOperationException(
                $"{entityType.GetTableName()} maps no {propertyName}, so no statement can name the column it is stored in.");

        return $"\"{property.GetColumnName()}\"";
    }
}
