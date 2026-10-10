// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;

namespace MailFathom.TestSupport;

/// <summary>Builds the authorization a use case asks, over a principal a test states.</summary>
/// <remarks>
/// Every use case behind a protected surface now takes one, so a test that composes one by hand would be writing the
/// same three lines wherever a caller is arranged. What a test states here is the principal alone, because that is the
/// whole of what the application layer learns about the outside of a request; a substitute would leave the same fact
/// spelled differently in each suite.
/// </remarks>
internal static class AccessAuthorizations
{
    /// <summary>Builds the authorization of an admitted caller granted exactly the permissions named, acting for the deployment's user.</summary>
    /// <param name="grantedPermissions">What the entry that admitted the caller resolved to, which is empty for a caller granted nothing.</param>
    /// <returns>The authorization a use case reached by that caller consults.</returns>
    /// <remarks>
    /// An ordinary caller acts for somebody, so this states a user rather than leaving the principal without one: a
    /// helper that omitted it would arrange the deployment administrator in every test about reading a mailbox, and
    /// those tests would then be proving a refusal instead of what they were written for. A test about a principal
    /// acting for nobody arranges <see cref="ForAdministratorGranted" /> by name.
    /// </remarks>
    internal static AccessAuthorization ForCallerGranted(params MailFathomPermission[] grantedPermissions) =>
        ForUserGranted(SyntheticUser.Deployment, grantedPermissions);

    /// <summary>Builds the authorization of an admitted caller acting for one named user.</summary>
    /// <param name="user">The user whose mail the caller was admitted to act on.</param>
    /// <param name="grantedPermissions">What the entry that admitted the caller resolved to.</param>
    /// <returns>The authorization a use case reached by that caller consults.</returns>
    internal static AccessAuthorization ForUserGranted(
        UserId user,
        params MailFathomPermission[] grantedPermissions) =>
        ForPrincipal(AuthorizedPrincipal.CallerActingFor(user, "test-caller", grantedPermissions));

    /// <summary>Builds the authorization of the deployment administrator, which is a caller acting for no user.</summary>
    /// <param name="grantedPermissions">What the entry that admitted the administrator resolved to.</param>
    /// <returns>The authorization a use case reached by that caller consults.</returns>
    internal static AccessAuthorization ForAdministratorGranted(params MailFathomPermission[] grantedPermissions) =>
        ForPrincipal(AuthorizedPrincipal.Caller("test-administrator", grantedPermissions));

    /// <summary>Gets the organization every target a scoped administrator is arranged over belongs to.</summary>
    internal static Guid ScopedOrganization { get; } = new("0198f0aa-0000-7000-8000-00000000f001");

    /// <summary>Gets the user every mail account a scoped administrator is arranged over is assigned to alone, and the user a user-targeted read names.</summary>
    internal static UserId ScopedHolder { get; } = UserId.Create(new Guid("0198f0aa-0000-7000-8000-00000000f002"));

    /// <summary>Gets the one mail account a scoped administrator is arranged over, which belongs to <see cref="ScopedOrganization" /> and is assigned to <see cref="ScopedHolder" /> alone.</summary>
    internal static MailAccountId ScopedAccount { get; } = MailAccountId.Create("0198f0aa-0000-7000-8000-00000000f0aa");

    /// <summary>Gets one scope of each kind that covers the targets <see cref="ForAdministratorScopedAt" /> places: the deployment, <see cref="ScopedOrganization" />, and <see cref="ScopedHolder" />.</summary>
    internal static IReadOnlyList<AssignmentScope> ScopesCoveringTheirTarget { get; } =
    [
        AssignmentScope.Deployment,
        AssignmentScope.Organization(ScopedOrganization),
        AssignmentScope.User(ScopedHolder),
    ];

    /// <summary>Gets one narrower scope of each kind that covers none of those targets: another organization, and another user.</summary>
    internal static IReadOnlyList<AssignmentScope> ScopesOutsideTheirTarget { get; } =
    [
        AssignmentScope.Organization(new Guid("0198f0aa-0000-7000-8000-00000000f003")),
        AssignmentScope.User(UserId.Create(new Guid("0198f0aa-0000-7000-8000-00000000f004"))),
    ];

    /// <summary>Builds the authorization of an administrator holding some permissions at one scope, over targets placed in <see cref="ScopedOrganization" />.</summary>
    /// <param name="scope">The scope the administrator's role was assigned at.</param>
    /// <param name="grantedPermissions">The permissions that role carries.</param>
    /// <returns>The authorization a use case reached by that administrator consults.</returns>
    /// <remarks>
    /// <see cref="ScopedAccount" /> and <see cref="ScopedHolder" /> are placed in <see cref="ScopedOrganization" />, the
    /// account assigned to the holder alone, so each scope in <see cref="ScopesCoveringTheirTarget" /> reaches both while
    /// each in <see cref="ScopesOutsideTheirTarget" /> reaches neither. Every other account and user is placed nowhere,
    /// which only the deployment covers, so an operation that checked a target other than the one it was asked about
    /// is refused at every narrower scope rather than served.
    /// </remarks>
    internal static AccessAuthorization ForAdministratorScopedAt(
        AssignmentScope scope,
        params MailFathomPermission[] grantedPermissions) =>
        new(
            new StatedPrincipalSource(AuthorizedPrincipal.Caller(
                "test-administrator",
                ScopedGrant.Of(grantedPermissions.Select(permission => (permission, scope))))),
            new ScopedOrganizationTargets());

    /// <summary>Builds the authorization of an administrator holding each permission at the scopes a test states, over targets the test places itself.</summary>
    /// <param name="grant">What the administrator holds, each permission with the scopes it is held at.</param>
    /// <param name="targets">Places each mail account or user the test names.</param>
    /// <returns>The authorization a use case reached by that administrator consults.</returns>
    /// <remarks>
    /// For a test that needs more than <see cref="ForAdministratorScopedAt" /> arranges: several accounts in different
    /// organizations, a mailbox two users share, or an account named by the identifier a suite's own catalogue serves.
    /// </remarks>
    internal static AccessAuthorization ForAdministratorScoped(ScopedGrant grant, IAdministrativeTargets targets) =>
        new(new StatedPrincipalSource(AuthorizedPrincipal.Caller("test-administrator", grant)), targets);

    /// <summary>Builds the authorization of work reached under a stated principal, or under none.</summary>
    /// <param name="principal">Whoever the work is running for, or <see langword="null" /> for an entrypoint that stated nothing.</param>
    /// <returns>The authorization a use case reached that way consults.</returns>
    internal static AccessAuthorization ForPrincipal(AuthorizedPrincipal? principal) =>
        new(new StatedPrincipalSource(principal));

    /// <summary>Places <see cref="ScopedAccount" /> and <see cref="ScopedHolder" /> in <see cref="ScopedOrganization" />, and every other target nowhere.</summary>
    private sealed class ScopedOrganizationTargets : IAdministrativeTargets
    {
        public Task<IReadOnlyDictionary<MailAccountId, AdministrativeTarget>> PlaceMailAccountsAsync(
            IReadOnlyCollection<MailAccountId> accounts,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<MailAccountId, AdministrativeTarget>>(accounts.Distinct().ToDictionary(
                account => account,
                account => account == ScopedAccount
                    ? AdministrativeTarget.MailAccount(ScopedOrganization, [ScopedHolder])
                    : AdministrativeTarget.Unplaced));

        public Task<AdministrativeTarget> PlaceUserAsync(UserId user, CancellationToken cancellationToken) =>
            Task.FromResult(user == ScopedHolder
                ? AdministrativeTarget.User(user, ScopedOrganization)
                : AdministrativeTarget.Unplaced);
    }

    /// <summary>Reports the one principal a test stated, for the whole of that test's unit of work.</summary>
    private sealed class StatedPrincipalSource(AuthorizedPrincipal? principal) : IAuthorizedPrincipalSource
    {
        public AuthorizedPrincipal? Current => principal;
    }
}
