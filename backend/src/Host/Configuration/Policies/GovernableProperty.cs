// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Reflection;
using MailFathom.Infrastructure.Policies;

namespace MailFathom.Host.Configuration.Policies;

/// <summary>One property of a governed record, or one block of them, as a settings policy names it.</summary>
/// <param name="Path">The record's own key names joined by colons, in the casing the type declares.</param>
/// <param name="PropertyClass">Which of a policy's statements the property takes.</param>
/// <param name="Shape">Whether this is one value, a block of properties, or a secret block.</param>
/// <param name="Accessors">The properties read in turn to reach this one from the record, empty where the property is a column beside the record rather than a key of its document.</param>
internal sealed record GovernableProperty(
    string Path,
    SettingsPolicyPropertyClass PropertyClass,
    GovernablePropertyShape Shape,
    IReadOnlyList<PropertyInfo> Accessors)
{
    /// <summary>Gets the type the property's value binds as, or <see langword="null" /> for a column beside the record.</summary>
    internal Type? ValueType => this.Accessors is [.., var last]
        ? Nullable.GetUnderlyingType(last.PropertyType) ?? last.PropertyType
        : null;

    /// <summary>Reads the property out of a bound record.</summary>
    /// <param name="record">The record, bound as the type this property was described from.</param>
    /// <returns>The object the property is declared on and the value it holds there, or neither where a block on the way to it is absent.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="record" /> is <see langword="null" />.</exception>
    internal (object? Owner, object? Value) ReadFrom(object record)
    {
        ArgumentNullException.ThrowIfNull(record);

        object? owner = null;
        object? value = record;

        foreach (var accessor in this.Accessors)
        {
            if (value is null)
            {
                return (null, null);
            }

            owner = value;
            value = accessor.GetValue(owner);
        }

        return (owner, value);
    }
}
