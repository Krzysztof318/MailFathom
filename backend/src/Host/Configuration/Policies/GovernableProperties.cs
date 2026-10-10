// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections;
using System.Reflection;
using MailFathom.Infrastructure.Policies;
using MailFathom.Infrastructure.Secrets.Discovery;

namespace MailFathom.Host.Configuration.Policies;

/// <summary>Every property of one kind of governed record that a settings policy may name, derived from the record's own type.</summary>
/// <remarks>
/// <para>
/// Derived rather than listed, which is the point of it: a property added to a user's record or to a mail account's
/// declaration is governed as an ordinary one from the build that adds it, with nothing here edited, and only one
/// that belongs to another class carries a mark on its declaration. A list kept beside the type is a list that falls
/// behind it, and what falls behind is a setting no policy can reach.
/// </para>
/// <para>
/// A setting is a public property the configuration binder writes: one with a public setter, or one without whose
/// value is a block or a collection the binder fills in place. A property without a setter that holds anything else
/// is a view computed from the settings rather than one of them, so it is passed over. What is left has to be one of
/// four shapes — a value, a list, a block, or a secret block — and a setting of a shape this cannot class refuses to
/// be described at all, so a property a policy would silently never govern fails the test that describes both records
/// rather than reaching a deployment.
/// </para>
/// <para>
/// A secret block is recognized by binding as <see cref="ConfiguredSecret" />, for the reason that type gives: a mark
/// can be omitted on a secret added later and nothing would notice, while the type cannot.
/// </para>
/// </remarks>
internal sealed class GovernableProperties
{
    private static readonly HashSet<Type> ValueTypes =
    [
        typeof(string),
        typeof(decimal),
        typeof(Guid),
        typeof(DateOnly),
        typeof(TimeOnly),
        typeof(DateTime),
        typeof(DateTimeOffset),
        typeof(TimeSpan),
        typeof(Uri),
    ];

    private readonly Dictionary<string, GovernableProperty> governed = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> outside = new(StringComparer.OrdinalIgnoreCase);

    private GovernableProperties()
    {
    }

    /// <summary>Gets every governable property and block, in no particular order.</summary>
    internal IReadOnlyCollection<GovernableProperty> All => this.governed.Values;

    /// <summary>Describes the governable properties of one record type.</summary>
    /// <param name="record">The type the record's document binds as.</param>
    /// <param name="identityColumns">The identity properties held as columns beside the document, which the type therefore does not declare.</param>
    /// <returns>The properties a policy may name for that kind of record.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="record" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the record carries a setting of a shape no policy statement can govern, or a block that contains itself.</exception>
    internal static GovernableProperties Of(Type record, params ReadOnlySpan<string> identityColumns)
    {
        ArgumentNullException.ThrowIfNull(record);

        var properties = new GovernableProperties();

        properties.Describe(record, prefix: string.Empty, SettingsPolicyPropertyClass.Ordinary, accessors: [], enclosing: []);

        foreach (var column in identityColumns)
        {
            // A column the type also declares is already described with its accessors, and stays so.
            properties.governed.TryAdd(
                column,
                new GovernableProperty(column, SettingsPolicyPropertyClass.Identity, GovernablePropertyShape.Value, Accessors: []));
        }

        return properties;
    }

    /// <summary>Finds the property or block a path names.</summary>
    /// <param name="path">The path, compared without regard to case.</param>
    /// <returns>The property, or <see langword="null" /> when the path names nothing a policy governs.</returns>
    internal GovernableProperty? Find(string path) => this.governed.GetValueOrDefault(path);

    /// <summary>Finds why a path names something no policy governs.</summary>
    /// <param name="path">The path, compared without regard to case.</param>
    /// <returns>The reason its declaration gives, or <see langword="null" /> when the path names no such property.</returns>
    internal string? FindReasonOutside(string path) => this.outside.GetValueOrDefault(path);

    /// <summary>Finds the list a path reaches inside, which no path may.</summary>
    /// <param name="path">A path that names nothing.</param>
    /// <returns>The list one of its leading segments names, or <see langword="null" /> when none does.</returns>
    internal GovernableProperty? FindListReachedInto(string path)
    {
        var segments = path.Split(':');

        return Enumerable.Range(1, segments.Length - 1)
            .Select(length => this.Find(string.Join(':', segments.Take(length))))
            .FirstOrDefault(property => property is { Shape: GovernablePropertyShape.List });
    }

    /// <summary>Reports whether a property is one only an administrator writes, or a block holding one.</summary>
    /// <param name="property">The property or block an editing list names.</param>
    /// <returns><see langword="true" /> when naming it would name an administrator-only property.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="property" /> is <see langword="null" />.</exception>
    internal bool HoldsAdministratorOnly(GovernableProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);

        return this.governed.Values.Any(candidate =>
            candidate.PropertyClass == SettingsPolicyPropertyClass.AdministratorOnly
            && (ReferenceEquals(candidate, property)
                || candidate.Path.StartsWith($"{property.Path}:", StringComparison.OrdinalIgnoreCase)));
    }

    private void Describe(
        Type type,
        string prefix,
        SettingsPolicyPropertyClass inherited,
        PropertyInfo[] accessors,
        Type[] enclosing)
    {
        if (enclosing.Contains(type))
        {
            throw new InvalidOperationException(
                $"{type.Name} is a block of settings that contains itself, so its properties have no finite set of paths a settings policy could name.");
        }

        var settings = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetIndexParameters().Length == 0 && property.GetMethod is { IsPublic: true });

        foreach (var property in settings)
        {
            var path = $"{prefix}{property.Name}";

            if (property.GetCustomAttribute<OutsideSettingsPolicyAttribute>() is { } ungoverned)
            {
                this.outside.Add(path, ungoverned.Reason);

                continue;
            }

            if (ShapeOf(type, property) is not { } shape)
            {
                continue;
            }

            // A block's class reaches everything beneath it, so a property inside an administrator-only block is
            // administrator-only without carrying a mark of its own.
            var propertyClass = shape == GovernablePropertyShape.Secret
                ? SettingsPolicyPropertyClass.Identity
                : property.GetCustomAttribute<SettingsPolicyClassAttribute>()?.PropertyClass ?? inherited;

            PropertyInfo[] reached = [.. accessors, property];

            this.governed.Add(path, new GovernableProperty(path, propertyClass, shape, reached));

            if (shape == GovernablePropertyShape.Block)
            {
                this.Describe(property.PropertyType, $"{path}:", propertyClass, reached, [.. enclosing, type]);
            }
        }
    }

    /// <summary>Classes one public property, or answers nothing for a view computed from the settings.</summary>
    private static GovernablePropertyShape? ShapeOf(Type declaring, PropertyInfo property)
    {
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        var isWritten = property.SetMethod is { IsPublic: true };

        if (type == typeof(ConfiguredSecret))
        {
            return GovernablePropertyShape.Secret;
        }

        if (type.IsPrimitive || type.IsEnum || ValueTypes.Contains(type))
        {
            return isWritten ? GovernablePropertyShape.Value : null;
        }

        if (typeof(IEnumerable).IsAssignableFrom(type))
        {
            // The binder adds to a collection it is handed and cannot replace one behind an interface it cannot set.
            return isWritten || type is { IsClass: true, IsAbstract: false } ? GovernablePropertyShape.List : null;
        }

        if (type is { IsClass: true, IsAbstract: false })
        {
            return GovernablePropertyShape.Block;
        }

        return isWritten
            ? throw new InvalidOperationException(
                $"{declaring.Name}.{property.Name} is a setting of a governed record whose type, {type.Name}, is neither a value, a list, a block of settings, nor a secret block, so no settings policy could govern it. Give it one of those shapes, or mark it with {nameof(OutsideSettingsPolicyAttribute)} and the reason no policy governs it.")
            : null;
    }
}
