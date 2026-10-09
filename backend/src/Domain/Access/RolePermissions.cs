// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Access;

/// <summary>The explicit list of permissions one role grants, and the stored names on it this build does not publish.</summary>
/// <remarks>
/// <para>
/// A role is exactly as wide as somebody wrote it, so the list is names and never a pattern, and a release that
/// publishes a permission adds it to no list. Writing is the strict half: a name nothing publishes is refused, because
/// a grant naming a capability that does not exist means nothing and an operator would believe they had made it.
/// </para>
/// <para>
/// Reading is the forgiving half. A stored name may stop being published — a later release retired it, or the row was
/// edited in the database — and refusing the role over it would revoke every other name it lists from everybody who
/// holds it. Such a name is kept apart in <see cref="Unpublished" /> instead: reported so somebody can remove it, and
/// granted to nobody.
/// </para>
/// </remarks>
public sealed record RolePermissions
{
    private RolePermissions(IReadOnlyList<MailFathomPermission> granted, IReadOnlyList<string> unpublished)
    {
        this.Granted = granted;
        this.Unpublished = unpublished;
    }

    /// <summary>Gets the published permissions the role grants, in the order <see cref="MailFathomPermission.All" /> declares them.</summary>
    public IReadOnlyList<MailFathomPermission> Granted { get; }

    /// <summary>Gets the stored names this build does not publish, in ordinal order, which the role grants nothing for.</summary>
    /// <remarks>Always empty for a list built to be written.</remarks>
    public IReadOnlyList<string> Unpublished { get; }

    /// <summary>Builds the list a role is written with.</summary>
    /// <param name="permissions">The permissions, in any order and with any repetition.</param>
    /// <returns>The list, each permission once.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="permissions" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when a value is the unspecified struct default.</exception>
    public static RolePermissions Of(IEnumerable<MailFathomPermission> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        var written = permissions.ToArray();

        if (written.Any(permission => !permission.IsSpecified))
        {
            throw new ArgumentException("A role lists published permissions only.", nameof(permissions));
        }

        return new RolePermissions([.. MailFathomPermission.All.Where(written.Contains)], []);
    }

    /// <summary>Builds the list a role is written with from the names somebody wrote.</summary>
    /// <param name="names">The written names.</param>
    /// <param name="permissions">The list when every name is published; otherwise <see langword="null" />.</param>
    /// <param name="unpublished">The written names nothing publishes, in ordinal order, which is what a refusal names.</param>
    /// <returns><see langword="true" /> when every name is published; otherwise <see langword="false" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="names" /> is <see langword="null" />.</exception>
    /// <remarks>A name is compared exactly, as <see cref="MailFathomPermission.TryParse" /> compares it, because the same string travels as an OAuth scope.</remarks>
    public static bool TryCreate(
        IEnumerable<string> names,
        out RolePermissions? permissions,
        out IReadOnlyList<string> unpublished)
    {
        var read = Read(names);

        permissions = read.Unpublished.Count == 0 ? read : null;
        unpublished = read.Unpublished;

        return permissions is not null;
    }

    /// <summary>Reads the names a stored role lists.</summary>
    /// <param name="storedNames">The names as they are stored.</param>
    /// <returns>The list, with every name this build does not publish kept apart rather than granted.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="storedNames" /> is <see langword="null" />.</exception>
    public static RolePermissions Read(IEnumerable<string> storedNames)
    {
        ArgumentNullException.ThrowIfNull(storedNames);

        var names = storedNames.Distinct(StringComparer.Ordinal).ToArray();

        var granted = names
            .Select(name => MailFathomPermission.TryParse(name, out var permission) ? permission : default)
            .Where(permission => permission.IsSpecified)
            .ToArray();

        return new RolePermissions(
            [.. MailFathomPermission.All.Where(granted.Contains)],
            [.. names.Where(name => !MailFathomPermission.TryParse(name, out _)).Order(StringComparer.Ordinal)]);
    }
}
