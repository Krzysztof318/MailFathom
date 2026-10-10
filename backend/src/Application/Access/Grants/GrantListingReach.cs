// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.Access.Grants;

/// <summary>What a listing of groups or role assignments answers within: the whole deployment, or the organizations and users some scopes name.</summary>
/// <remarks>
/// A listing never refuses over a scope. It answers with what the caller's scopes cover, so an organization's
/// administrator lists that organization's groups and the assignments inside it and nobody else's, and a caller holding
/// the listing's permission over one user lists the assignments made to that user at that user.
/// </remarks>
public sealed record GrantListingReach
{
    private GrantListingReach(bool wholeDeployment, IReadOnlySet<Guid> organizations, IReadOnlySet<Guid> users)
    {
        this.WholeDeployment = wholeDeployment;
        this.Organizations = organizations;
        this.Users = users;
    }

    /// <summary>Gets the reach covering everything the deployment holds.</summary>
    public static GrantListingReach Deployment { get; } = new(wholeDeployment: true, new HashSet<Guid>(), new HashSet<Guid>());

    /// <summary>Gets whether the listing answers with everything, which is when a deployment scope was among those it was built from.</summary>
    public bool WholeDeployment { get; }

    /// <summary>Gets the organizations whose groups, and the assignments inside which, the listing answers with.</summary>
    public IReadOnlySet<Guid> Organizations { get; }

    /// <summary>Gets the users the listing answers with the assignments made to and at, wherever they belong.</summary>
    public IReadOnlySet<Guid> Users { get; }

    /// <summary>Builds the reach of the scopes a caller holds a listing's permission at.</summary>
    /// <param name="scopes">The scopes.</param>
    /// <returns>The reach covering everything when a scope is the deployment's, and what the organizations and users named cover otherwise.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scopes" /> is <see langword="null" />.</exception>
    public static GrantListingReach Within(IEnumerable<AssignmentScope> scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);

        var held = scopes.ToArray();

        return held.Any(scope => scope.Kind == AssignmentScopeKind.Deployment)
            ? Deployment
            : new GrantListingReach(
                wholeDeployment: false,
                held.Where(scope => scope.Kind == AssignmentScopeKind.Organization).Select(scope => scope.Target).ToHashSet(),
                held.Where(scope => scope.Kind == AssignmentScopeKind.User).Select(scope => scope.Target).ToHashSet());
    }
}
