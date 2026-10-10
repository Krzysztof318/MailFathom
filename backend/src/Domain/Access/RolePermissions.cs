// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Access;

/// <summary>The list of permissions one role grants — names written out, and patterns reaching whatever is published beneath them — and the stored entries on it that grant nothing in this build.</summary>
/// <remarks>
/// <para>
/// An entry is a published name or a <see cref="PermissionSubtree" />. A name is exactly what somebody wrote. A pattern
/// is kept as written and resolved against the published set whenever it is asked, so a role listing one holds a
/// permission a later release publishes in its reach from that release on — which is what the pattern is for, and why
/// <see cref="WidensOnUpgrade" /> is something every write giving such a role has to ask about.
/// </para>
/// <para>
/// Writing is the strict half: a name nothing publishes and a pattern reaching nothing are both refused, because a
/// grant naming a capability that does not exist means nothing and an operator would believe they had made it.
/// </para>
/// <para>
/// Reading is the forgiving half. A stored name may stop being published, and a stored pattern may come to reach
/// nothing — a later release retired what it reached, or the row was edited in the database — and refusing the role
/// over it would revoke every other entry it lists from everybody who holds it. Such an entry is kept apart in
/// <see cref="Unpublished" /> instead: reported so somebody can remove it, and granted to nobody.
/// </para>
/// </remarks>
public sealed record RolePermissions
{
    private RolePermissions(
        IReadOnlyList<MailFathomPermission> names,
        IReadOnlyList<PermissionSubtree> patterns,
        IReadOnlyList<string> unpublishedNames)
    {
        var reached = patterns.SelectMany(pattern => pattern.CoveredPermissions()).ToHashSet();

        this.Names = names;
        this.Patterns = patterns;
        this.Granted = [.. MailFathomPermission.All.Where(permission => names.Contains(permission) || reached.Contains(permission))];
        this.Unpublished =
        [
            .. unpublishedNames
                .Concat(patterns.Where(pattern => pattern.CoveredPermissions().Count == 0).Select(pattern => pattern.Written))
                .Order(StringComparer.Ordinal),
        ];
    }

    /// <summary>Gets the published permissions the role lists by name, in the order <see cref="MailFathomPermission.All" /> declares them.</summary>
    public IReadOnlyList<MailFathomPermission> Names { get; }

    /// <summary>Gets every pattern the role lists, as written and in ordinal order, one reaching nothing in this build included.</summary>
    public IReadOnlyList<PermissionSubtree> Patterns { get; }

    /// <summary>Gets the published permissions the role grants now — the names it lists and everything its patterns reach — in the order <see cref="MailFathomPermission.All" /> declares them.</summary>
    public IReadOnlyList<MailFathomPermission> Granted { get; }

    /// <summary>Gets the stored entries that grant nothing in this build, in ordinal order: a name it does not publish, and a pattern reaching nothing it publishes.</summary>
    /// <remarks>Always empty for a list built to be written.</remarks>
    public IReadOnlyList<string> Unpublished { get; }

    /// <summary>Gets the entries the role is stored and read back as: each name, then each pattern reaching something, exactly as written.</summary>
    /// <remarks>What <see cref="Unpublished" /> reports is left out, so writing a list that was read drops what grants nothing rather than carrying it forward.</remarks>
    public IReadOnlyList<string> Written =>
    [
        .. this.Names.Select(permission => permission.Name),
        .. this.Patterns.Where(pattern => pattern.CoveredPermissions().Count > 0).Select(pattern => pattern.Written),
    ];

    /// <summary>Gets whether the role may come to grant more than it grants now without anybody writing to it.</summary>
    /// <remarks>
    /// True of a role listing any pattern, one reaching nothing today included, because what a later release publishes
    /// is what decides its reach. A role of names alone is exactly as wide as somebody wrote it after every upgrade.
    /// </remarks>
    public bool WidensOnUpgrade => this.Patterns.Count > 0;

    /// <summary>Builds the list a role is written with from names alone.</summary>
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

        return new RolePermissions([.. MailFathomPermission.All.Where(written.Contains)], [], []);
    }

    /// <summary>Builds the list a role is written with from the entries somebody wrote.</summary>
    /// <param name="entries">The written entries: published names and patterns.</param>
    /// <param name="permissions">The list when every entry grants something; otherwise <see langword="null" />.</param>
    /// <param name="unpublished">The written entries that grant nothing, in ordinal order, which is what a refusal names: a name nothing publishes, and a pattern reaching nothing published.</param>
    /// <returns><see langword="true" /> when every entry grants something; otherwise <see langword="false" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="entries" /> is <see langword="null" />.</exception>
    /// <remarks>A name is compared exactly, as <see cref="MailFathomPermission.TryParse" /> compares it, because the same string travels as an OAuth scope.</remarks>
    public static bool TryCreate(
        IEnumerable<string> entries,
        out RolePermissions? permissions,
        out IReadOnlyList<string> unpublished)
    {
        var read = Read(entries);

        permissions = read.Unpublished.Count == 0 ? read : null;
        unpublished = read.Unpublished;

        return permissions is not null;
    }

    /// <summary>Reads the entries a stored role lists.</summary>
    /// <param name="storedEntries">The entries as they are stored.</param>
    /// <returns>The list, with every entry that grants nothing in this build kept apart rather than granted.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="storedEntries" /> is <see langword="null" />.</exception>
    public static RolePermissions Read(IEnumerable<string> storedEntries)
    {
        ArgumentNullException.ThrowIfNull(storedEntries);

        var entries = storedEntries.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

        var names = entries
            .Select(entry => MailFathomPermission.TryParse(entry, out var permission) ? permission : default)
            .Where(permission => permission.IsSpecified)
            .ToArray();

        var patterns = entries
            .Select(entry => PermissionSubtree.TryParse(entry, out var pattern) ? pattern : default)
            .Where(pattern => pattern.IsSpecified)
            .ToArray();

        return new RolePermissions(
            [.. MailFathomPermission.All.Where(names.Contains)],
            patterns,
            [.. entries.Where(entry => !MailFathomPermission.TryParse(entry, out _) && !PermissionSubtree.TryParse(entry, out _))]);
    }

    /// <summary>Reports what one stored entry grants in this build.</summary>
    /// <param name="storedEntry">The entry as it is stored: a name or a pattern.</param>
    /// <returns>The published name it is, every published permission the pattern it is reaches, or nothing.</returns>
    /// <remarks>The rule <see cref="Read" /> applies to a whole list, asked of one row, which is how a store reads a grant out of a join.</remarks>
    public static IReadOnlyList<MailFathomPermission> GrantedBy(string? storedEntry) =>
        MailFathomPermission.TryParse(storedEntry, out var permission) ? [permission]
        : PermissionSubtree.TryParse(storedEntry, out var pattern) ? pattern.CoveredPermissions()
        : [];

    /// <summary>Lists every entry a role can be stored with that grants one permission: its name, and each pattern reaching it.</summary>
    /// <param name="permission">The permission.</param>
    /// <returns>The entries, the name first.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="permission" /> is the unspecified struct default.</exception>
    /// <remarks>What a store compares a stored entry against to find the roles giving one name, a pattern counting as holding what it reaches.</remarks>
    public static IReadOnlyList<string> EntriesGranting(MailFathomPermission permission)
    {
        var patterns = PermissionSubtree.WritingsReaching(permission);

        return [permission.Name, .. patterns];
    }
}
