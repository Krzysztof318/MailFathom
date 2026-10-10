// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Access;

/// <summary>What a principal holds: every permission it was granted, each paired with the scopes it is held at.</summary>
/// <remarks>
/// <para>
/// A user's grant is the union of what their own assignments and their groups' assignments give, each assignment
/// contributing every name of its role at its scope, so one permission may be held at several scopes and is held at
/// each of them. There is no deny: nothing here subtracts a scope, and the only way a grant gets smaller is
/// <see cref="NarrowedTo" />, which keeps names and never touches the scope a name is held at.
/// </para>
/// <para>
/// The scope is read by administrative permissions alone. A mail permission reaches its holder's own mail whatever
/// scope the assignment granting it names, so a mail use case asks <see cref="Permissions" /> and an administrative
/// one asks <see cref="ScopesOf" /> as well.
/// </para>
/// </remarks>
public sealed class ScopedGrant
{
    private readonly IReadOnlyDictionary<MailFathomPermission, IReadOnlySet<AssignmentScope>> scopesByPermission;

    private ScopedGrant(IReadOnlyDictionary<MailFathomPermission, IReadOnlySet<AssignmentScope>> scopesByPermission)
    {
        this.scopesByPermission = scopesByPermission;
        this.Permissions = scopesByPermission.Keys.ToHashSet();
    }

    /// <summary>Gets the grant holding nothing.</summary>
    public static ScopedGrant None { get; } = new(new Dictionary<MailFathomPermission, IReadOnlySet<AssignmentScope>>());

    /// <summary>Gets every permission held, at whichever scope.</summary>
    public IReadOnlySet<MailFathomPermission> Permissions { get; }

    /// <summary>Builds the union of what several assignments give.</summary>
    /// <param name="held">Each permission an assignment gives, paired with the scope that assignment names, in any order and with any repetition.</param>
    /// <returns>The grant holding each permission at every scope it was paired with.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="held" /> or a scope in it is <see langword="null" />.</exception>
    /// <remarks>An unspecified permission is dropped rather than held, because the struct default names no capability.</remarks>
    public static ScopedGrant Of(IEnumerable<(MailFathomPermission Permission, AssignmentScope Scope)> held)
    {
        ArgumentNullException.ThrowIfNull(held);

        var pairs = held.ToArray();

        if (pairs.Any(pair => pair.Scope is null))
        {
            throw new ArgumentNullException(nameof(held), "A held permission is held at a scope.");
        }

        return new ScopedGrant(pairs
            .Where(pair => pair.Permission.IsSpecified)
            .GroupBy(pair => pair.Permission, pair => pair.Scope)
            .ToDictionary(
                permissions => permissions.Key,
                scopes => (IReadOnlySet<AssignmentScope>)scopes.ToHashSet()));
    }

    /// <summary>Builds a grant holding every one of some permissions over the whole deployment.</summary>
    /// <param name="permissions">The permissions.</param>
    /// <returns>The grant.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="permissions" /> is <see langword="null" />.</exception>
    /// <remarks>What a principal holds where nothing narrower than the deployment was ever written for it to be held at.</remarks>
    public static ScopedGrant AtDeployment(IEnumerable<MailFathomPermission> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        return Of(permissions.Select(permission => (permission, AssignmentScope.Deployment)));
    }

    /// <summary>Reports the scopes one permission is held at.</summary>
    /// <param name="permission">The permission.</param>
    /// <returns>The scopes, empty where it is not held.</returns>
    public IReadOnlySet<AssignmentScope> ScopesOf(MailFathomPermission permission) =>
        this.scopesByPermission.TryGetValue(permission, out var scopes) ? scopes : new HashSet<AssignmentScope>();

    /// <summary>Reports whether one permission is held at a scope covering a target.</summary>
    /// <param name="permission">The permission.</param>
    /// <param name="target">Where the thing the operation names sits in the deployment.</param>
    /// <returns><see langword="true" /> when any scope the permission is held at covers the target.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="target" /> is <see langword="null" />.</exception>
    public bool Covers(MailFathomPermission permission, AdministrativeTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        return this.ScopesOf(permission).Any(target.IsCoveredBy);
    }

    /// <summary>Keeps only the permissions a narrowing names, each at every scope it was held at.</summary>
    /// <param name="permissions">The names the narrowing keeps: a credential's list, a token's scopes, or a surface's half.</param>
    /// <returns>The narrowed grant.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="permissions" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A narrowing names permissions and never scopes, because a second scoping rule beside the assignment would be a
    /// second rule every check has to keep consistent with the first. A name the narrowing lists and the grant does not
    /// hold grants nothing, so a narrowing can only take away.
    /// </remarks>
    public ScopedGrant NarrowedTo(IEnumerable<MailFathomPermission> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        var kept = permissions.ToHashSet();

        return new ScopedGrant(this.scopesByPermission
            .Where(held => kept.Contains(held.Key))
            .ToDictionary(held => held.Key, held => held.Value));
    }
}
