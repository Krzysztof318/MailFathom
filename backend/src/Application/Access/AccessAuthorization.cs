// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Access;

/// <summary>What a use case asks before it does the work it was reached for.</summary>
/// <remarks>
/// <para>
/// The transport refuses what it can refuse cheaply, and this is the authority. An entrypoint added later — a rule
/// action, a worker, a command, a second protocol — reaches a use case without passing any middleware, so a check that
/// lived only there is one the new entrypoint forgets. Asking here is what makes the answer a property of the operation
/// instead of a property of the route somebody happened to arrive by.
/// </para>
/// <para>
/// Each method admits exactly one kind of principal and refuses every other, including a principal that holds more.
/// <see cref="RequireProcessIdentity" /> in particular is not "a caller with everything granted": a principal that
/// could be admitted by holding a permission would be reachable by whoever an operator granted that permission to,
/// which is the opposite of what work no caller requested runs under.
/// </para>
/// <para>
/// Every method refuses when the work was reached under no principal, so an entrypoint that never stated what admitted
/// it fails rather than defaulting to permitted.
/// </para>
/// <para>
/// <see cref="RequireUser" /> is the one method about whose mail rather than about what may be done to it. It is asked
/// beside a permission rather than instead of one, because the two axes are independent: no permission names a user,
/// and a grant however broad still reaches the mail of the one user the work was admitted for.
/// </para>
/// <para>
/// Some members report instead of refusing, for a boundary composing an answer per caller rather than performing an
/// operation for one, and none decides anything of its own. Each <c>Permits</c> member answers exactly what its
/// <c>Require</c> counterpart would have refused — <see cref="Permits" /> for <see cref="RequirePermission" />,
/// <see cref="PermitsAtAnyScope" /> for <see cref="RequirePermissionAtAnyScope" />, and the <c>PermitsOverAsync</c>
/// overloads and <see cref="CoveredMailAccountsAsync" /> for the <c>RequirePermissionOverAsync</c> overloads — so the
/// transport and the use case cannot come to disagree about what holding a permission means.
/// <see cref="HoldsOnlyBelowDeployment" /> answers what a refusal of an operation admitted only at the deployment
/// scope says on top of naming the permission: that the caller holds it, but only over an organization or a user.
/// <see cref="ScopesOf" /> is what a listing answers within, since a listing never refuses over a scope.
/// </para>
/// </remarks>
public sealed class AccessAuthorization
{
    private readonly IAuthorizedPrincipalSource principals;
    private readonly IAdministrativeTargets targets;

    /// <summary>Initializes the authorization over the principal of the unit of work in hand, placing no target.</summary>
    /// <param name="principals">Reports whoever the work is running for.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="principals" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Every target is then <see cref="AdministrativeTarget.Unplaced" />, so an administrative permission reaches a
    /// named target only where it is held over the whole deployment. That is the answer for work that reaches no
    /// administrative target at all, and the narrowest one for work that does.
    /// </remarks>
    public AccessAuthorization(IAuthorizedPrincipalSource principals)
        : this(principals, UnplacedTargets.Instance)
    {
    }

    /// <summary>Initializes the authorization over the principal of the unit of work in hand, placing what an operation names.</summary>
    /// <param name="principals">Reports whoever the work is running for.</param>
    /// <param name="targets">Places a named user or mail account in the deployment, for an operation that names one.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="principals" /> or <paramref name="targets" /> is <see langword="null" />.</exception>
    public AccessAuthorization(IAuthorizedPrincipalSource principals, IAdministrativeTargets targets)
    {
        ArgumentNullException.ThrowIfNull(principals);
        ArgumentNullException.ThrowIfNull(targets);

        this.principals = principals;
        this.targets = targets;
    }

    /// <summary>Gets what the work in hand was admitted as, or <see langword="null" /> where it was reached under no principal.</summary>
    /// <remarks>
    /// <para>
    /// It decides nothing and is never asked before an operation runs. It is here for a boundary that has to name the
    /// caller in a record of its own — which is what an operator diagnosing a refusal reads, since the MCP surface
    /// tells a refused caller nothing at all — and reading it through the same object the decision was asked of is what
    /// keeps a boundary from acquiring the principal source and deciding for itself what holding a permission means.
    /// </para>
    /// <para>
    /// What it carries is what <see cref="AuthorizedPrincipal.Identity" /> carries, which for a token is the issuer and
    /// the subject the deployment authorized — a host name and a remote party's identifier for a person. That is why
    /// <see cref="PrincipalNotAuthorizedException" /> is barred from naming it and why a boundary reading it decides
    /// for itself what its own readers may see.
    /// </para>
    /// </remarks>
    public string? PrincipalIdentity => this.principals.Current?.Identity;

    /// <summary>Requires that an admitted caller holding one named capability is what reached this use case.</summary>
    /// <param name="permission">The capability the operation is published under.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="permission" /> names no published capability, which is a defect in the calling use case rather than a refusal.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the work was reached under no principal, under a principal that is not a caller, or by a caller whose grant omits the permission.</exception>
    public void RequirePermission(MailFathomPermission permission)
    {
        if (!this.RequireCaller(permission).Holds(permission))
        {
            throw PrincipalNotAuthorizedException.MissingPermission(permission);
        }
    }

    /// <summary>Requires that an admitted caller holding either of two named capabilities is what reached this use case.</summary>
    /// <param name="first">One capability the operation is published under.</param>
    /// <param name="second">The other capability the operation is published under.</param>
    /// <exception cref="ArgumentException">Thrown when either argument names no published capability, which is a defect in the calling use case rather than a refusal.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the work was reached under no principal, under a principal that is not a caller, or by a caller whose grant omits both capabilities.</exception>
    /// <remarks>
    /// <para>
    /// This is for a use case two surfaces perform, where each publishes the act under a name of its own. The surfaces
    /// draw from disjoint halves, so a caller admitted by one of them can never hold the other's name however broadly it
    /// is granted — requiring a single permission there would mean the use case was reachable from one entrypoint and
    /// dead from the other. Writing the contact book is the case: an operator reaches it under
    /// <see cref="MailFathomPermission.AdminOperate" /> and an agent under
    /// <see cref="MailFathomPermission.MailContactsWrite" />, and the act is the same act.
    /// </para>
    /// <para>
    /// It is an alternative rather than a widening, so it is written where the act genuinely belongs to both surfaces and
    /// nowhere else. An act only one of them performs keeps <see cref="RequirePermission" />, which is why promoting a
    /// collected contact and exporting one stay named for the administrative surface alone.
    /// </para>
    /// <para>
    /// A refusal names the alternative belonging to the surface the caller's own grant is written on, so an operator
    /// diagnosing one is told the name they could have granted rather than the one from the half they cannot reach. A
    /// caller granted nothing at all has no surface to read, and is told <paramref name="first" />.
    /// </para>
    /// </remarks>
    public void RequireAnyPermission(MailFathomPermission first, MailFathomPermission second)
    {
        if (!first.IsSpecified || !second.IsSpecified)
        {
            throw new ArgumentException(
                "A use case must require published permissions rather than the unspecified default.",
                !first.IsSpecified ? nameof(first) : nameof(second));
        }

        var principal = this.RequirePrincipal();

        if (principal.Kind != AuthorizedPrincipalKind.Caller)
        {
            throw PrincipalNotAuthorizedException.WrongPrincipalKind(AuthorizedPrincipalKind.Caller);
        }

        if (principal.Holds(first) || principal.Holds(second))
        {
            return;
        }

        throw PrincipalNotAuthorizedException.MissingPermission(RefusedAlternative(principal, first, second));
    }

    /// <summary>Answers the same question <see cref="RequirePermission" /> asks, for a boundary that has to decide rather than refuse.</summary>
    /// <param name="permission">The capability being asked about.</param>
    /// <returns><see langword="true" /> when an admitted caller holding that capability is what reached this work.</returns>
    /// <remarks>
    /// <para>
    /// A transport that composes an answer per caller — a protocol listing offering only what the caller may call — needs
    /// the verdict rather than the failure, and asking it here is what keeps one definition of what holding a permission
    /// means. Every case <see cref="RequirePermission" /> refuses is answered <see langword="false" /> here, including
    /// work reached under no principal and work reached under a principal that is not a caller.
    /// </para>
    /// <para>
    /// An unspecified permission answers <see langword="false" /> rather than raising, because the caller of this method
    /// is composing an answer about something it did not choose: a boundary asking about a capability nobody declared has
    /// found an operation nobody bounded, and the safe answer to that is no.
    /// </para>
    /// </remarks>
    public bool Permits(MailFathomPermission permission) =>
        permission.IsSpecified
        && this.principals.Current is { Kind: AuthorizedPrincipalKind.Caller } caller
        && caller.Holds(permission);

    /// <summary>Reports whether the caller reaching this work holds one administrative capability only below the deployment, which a refusal of an operation admitted only at the deployment scope says on top of naming the permission.</summary>
    /// <param name="permission">The capability the refused operation required.</param>
    /// <returns><see langword="true" /> when an admitted caller holds it at an organization or a user and not over the whole deployment.</returns>
    public bool HoldsOnlyBelowDeployment(MailFathomPermission permission) =>
        permission.IsSpecified
        && this.principals.Current is { Kind: AuthorizedPrincipalKind.Caller } caller
        && caller.HoldsOnlyBelowDeployment(permission);

    /// <summary>Requires that an admitted caller holding one named capability at any scope is what reached this use case, for an operation that learns its target only by reading it.</summary>
    /// <param name="permission">The capability the operation is published under.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="permission" /> names no published capability.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the work was reached under no principal, under a principal that is not a caller, or by a caller holding the permission nowhere.</exception>
    /// <remarks>
    /// A read addressed by an identity of its own — one queued message — finds the account it belongs to only once it
    /// has read the record, so it refuses a caller holding the permission nowhere before reading, and answers one whose
    /// scope does not cover what it found exactly as it answers a record it did not find.
    /// </remarks>
    public void RequirePermissionAtAnyScope(MailFathomPermission permission)
    {
        if (!this.RequireCaller(permission).Permissions.Contains(permission))
        {
            throw PrincipalNotAuthorizedException.MissingPermission(permission);
        }
    }

    /// <summary>Answers whether the caller holds one named capability at any scope, for a transport that leaves the target to the operation.</summary>
    /// <param name="permission">The capability being asked about.</param>
    /// <returns><see langword="true" /> when an admitted caller holding that capability, however narrowly, is what reached this work.</returns>
    /// <remarks>
    /// A route naming a target cannot know before the operation reads it whether a scope narrower than the deployment
    /// covers it, so the cheap refusal it can make is of a caller holding the permission nowhere at all. The operation
    /// behind it asks <see cref="RequirePermissionOverAsync(MailFathomPermission, MailAccountId, CancellationToken)" />
    /// or its user form, which is the authority.
    /// </remarks>
    public bool PermitsAtAnyScope(MailFathomPermission permission) =>
        permission.IsSpecified
        && this.principals.Current is { Kind: AuthorizedPrincipalKind.Caller } caller
        && caller.Permissions.Contains(permission);

    /// <summary>Answers whether the caller holds one named capability over one mail account, for a boundary that has to decide rather than refuse.</summary>
    /// <param name="permission">The capability being asked about.</param>
    /// <param name="account">The account the operation names.</param>
    /// <param name="cancellationToken">Cancels placing the account.</param>
    /// <returns><see langword="true" /> when an admitted caller holds the capability at a scope covering the account.</returns>
    /// <remarks>
    /// A boundary asks this before it answers anything else about the account, so that an account outside the caller's
    /// scope is told apart from one the deployment does not hold by nothing at all — ADR 0012 answers the one exactly as
    /// the other, because a refusal naming the scope would tell an organization's administrator the account exists.
    /// </remarks>
    public Task<bool> PermitsOverAsync(
        MailFathomPermission permission,
        MailAccountId account,
        CancellationToken cancellationToken) =>
        this.PermitsOverAsync(permission, token => this.PlaceMailAccountAsync(account, token), cancellationToken);

    /// <summary>Answers which of several mail accounts the caller holds one named capability over, placing them in one read.</summary>
    /// <param name="permission">The capability being asked about.</param>
    /// <param name="accounts">The accounts the operation names.</param>
    /// <param name="cancellationToken">Cancels placing the accounts.</param>
    /// <returns>The accounts an admitted caller holds the capability over, in the order given; every one of them for a caller holding it over the deployment, and none for a caller holding it nowhere.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="accounts" /> is <see langword="null" />.</exception>
    public async Task<IReadOnlyList<MailAccountId>> CoveredMailAccountsAsync(
        MailFathomPermission permission,
        IReadOnlyCollection<MailAccountId> accounts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accounts);

        if (!this.PermitsAtAnyScope(permission) || accounts.Count == 0)
        {
            return [];
        }

        var caller = this.principals.Current!;

        if (caller.Holds(permission))
        {
            return [.. accounts];
        }

        var placements = await this.targets.PlaceMailAccountsAsync(accounts, cancellationToken);

        return [.. accounts.Where(account => caller.HoldsOver(permission, PlacementOf(placements, account)))];
    }

    /// <summary>Answers whether the caller holds one named capability over one user, for a boundary that has to decide rather than refuse.</summary>
    /// <param name="permission">The capability being asked about.</param>
    /// <param name="user">The user the operation names.</param>
    /// <param name="cancellationToken">Cancels placing the user.</param>
    /// <returns><see langword="true" /> when an admitted caller holds the capability at a scope covering the user.</returns>
    public Task<bool> PermitsOverAsync(
        MailFathomPermission permission,
        UserId user,
        CancellationToken cancellationToken) =>
        this.PermitsOverAsync(permission, token => this.targets.PlaceUserAsync(user, token), cancellationToken);

    /// <summary>Requires that an admitted caller holding one named capability over one mail account is what reached this use case.</summary>
    /// <param name="permission">The capability the operation is published under.</param>
    /// <param name="account">The account the operation names.</param>
    /// <param name="cancellationToken">Cancels placing the account.</param>
    /// <returns>A task that completes once the caller is admitted.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="permission" /> names no published capability.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the work was reached under no principal, under a principal that is not a caller, or by a caller holding the permission at no scope covering the account.</exception>
    /// <remarks>
    /// A caller holding the permission over the whole deployment is admitted without the account being placed, so an
    /// operator's grant costs no read and reaches an account the deployment no longer holds exactly as it did before a
    /// grant had a scope.
    /// </remarks>
    public Task RequirePermissionOverAsync(
        MailFathomPermission permission,
        MailAccountId account,
        CancellationToken cancellationToken) =>
        this.RequirePermissionOverAsync(
            permission,
            token => this.PlaceMailAccountAsync(account, token),
            cancellationToken);

    /// <summary>Requires that an admitted caller holding one named capability over one user is what reached this use case.</summary>
    /// <param name="permission">The capability the operation is published under.</param>
    /// <param name="user">The user the operation names.</param>
    /// <param name="cancellationToken">Cancels placing the user.</param>
    /// <returns>A task that completes once the caller is admitted.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="permission" /> names no published capability.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the work was reached under no principal, under a principal that is not a caller, or by a caller holding the permission at no scope covering the user.</exception>
    public Task RequirePermissionOverAsync(
        MailFathomPermission permission,
        UserId user,
        CancellationToken cancellationToken) =>
        this.RequirePermissionOverAsync(
            permission,
            token => this.targets.PlaceUserAsync(user, token),
            cancellationToken);

    /// <summary>Answers whether the caller holds one named capability at a scope covering everything another scope covers.</summary>
    /// <param name="permission">The capability being asked about.</param>
    /// <param name="scope">The scope an operation is bounded by: the deployment, an organization it names, or a user.</param>
    /// <param name="cancellationToken">Cancels placing the user a user scope names.</param>
    /// <returns><see langword="true" /> when an admitted caller holds the capability over the deployment, over the organization the scope names or the user it names belongs to, or over that same user.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scope" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// This is how an operation naming an organization is asked, and how one grant is held against another: whoever
    /// places a credential for a user holds each name that user holds at a scope covering the one the user holds it
    /// at, and a scope is covered exactly where the thing it names is.
    /// </remarks>
    public Task<bool> PermitsOverScopeAsync(
        MailFathomPermission permission,
        AssignmentScope scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        return this.PermitsOverAsync(permission, token => this.PlaceScopeAsync(scope, token), cancellationToken);
    }

    /// <summary>Reports the scopes the caller holds one capability at, which is what a listing answers within.</summary>
    /// <param name="permission">The capability the listing is published under.</param>
    /// <returns>The scopes, empty for a caller that does not hold it and for every principal that is not a caller.</returns>
    /// <remarks>A listing never refuses over a scope: it answers with what the scopes cover, so a caller holding the permission over one organization lists that organization's people and nobody else's.</remarks>
    public IReadOnlySet<AssignmentScope> ScopesOf(MailFathomPermission permission) =>
        this.principals.Current is { Kind: AuthorizedPrincipalKind.Caller } caller
            ? caller.Grant.ScopesOf(permission)
            : new HashSet<AssignmentScope>();

    /// <summary>Requires one named capability over the whole deployment, for an act only that scope admits reached through an operation that otherwise names a target.</summary>
    /// <param name="permission">The capability the act is published under.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="permission" /> names no published capability.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown as <see cref="RequirePermission" /> throws, and marked <see cref="PrincipalNotAuthorizedException.RefusedForTheDeploymentAlone" />.</exception>
    /// <remarks>
    /// Recording a user into no organization and moving one out of every organization are such acts. Ask this before
    /// anything the request names is read, never after: the mark tells a boundary the refusal is the same whatever the
    /// request names, which is what lets it say the caller holds the permission only below the deployment.
    /// </remarks>
    public void RequirePermissionOverTheDeployment(MailFathomPermission permission)
    {
        if (!this.RequireCaller(permission).Holds(permission))
        {
            throw PrincipalNotAuthorizedException.MissingOverTheDeployment(permission);
        }
    }

    /// <summary>Requires that the work in hand is being done for one user, and answers which.</summary>
    /// <returns>The user whose mail this unit of work may act on.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the work was reached under no principal, or under one acting for no user.</exception>
    /// <remarks>
    /// <para>
    /// This is the second axis of an access decision and it is asked separately from the first, because the two answer
    /// different questions: a permission says what the work may do and this says whose mail it may do it to. A use case
    /// that reads or writes somebody's mail asks both, and asking this one is what turns "acting for nobody" into a
    /// refusal rather than into an unbounded read.
    /// </para>
    /// <para>
    /// It admits every kind of principal that carries a user rather than naming one kind, because a capability
    /// redeemed for a user's own attachment is acting for that user exactly as the caller who minted it was. What is
    /// refused is a principal with no user at all: this process's own identity, and the deployment administrator whose
    /// acts are the deployment's rather than one person's.
    /// </para>
    /// </remarks>
    public UserId RequireUser() =>
        this.RequirePrincipal().User ?? throw PrincipalNotAuthorizedException.NoUser();

    /// <summary>Requires that this use case was reached as work no caller requested.</summary>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the work was reached under no principal, or under one that is not MailFathom's own identity.</exception>
    /// <remarks>This is how a use case that runs on a schedule, out of a queue, or as part of an account run states that "there is no caller" is a case it models rather than a null nobody checked.</remarks>
    public void RequireProcessIdentity() => this.RequireKind(AuthorizedPrincipalKind.ProcessIdentity);

    /// <summary>Requires that a capability this deployment signed is what reached this use case.</summary>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the work was reached under no principal, or under one that is not a verified capability.</exception>
    /// <remarks>
    /// The capability is the authorization, so no permission is asked for beside it. What confines the work is the
    /// verified ticket the use case is separately handed, which names one object and expires; this method establishes
    /// only that a signature rather than an unidentified caller is what got here.
    /// </remarks>
    public void RequireSignedCapability() => this.RequireKind(AuthorizedPrincipalKind.SignedCapability);

    /// <summary>Picks which of two alternatives a refusal names: the one on the surface the caller's own grant is written on.</summary>
    /// <param name="principal">The caller the work was reached under.</param>
    /// <param name="first">The alternative named when neither matches the caller's surface.</param>
    /// <param name="second">The other alternative.</param>
    /// <returns>The permission to report as missing.</returns>
    private static MailFathomPermission RefusedAlternative(
        AuthorizedPrincipal principal,
        MailFathomPermission first,
        MailFathomPermission second)
    {
        var surfaces = principal.Permissions
            .Where(permission => permission.Surface != ProtectedSurface.Mail || principal.User is not null)
            .Select(static permission => permission.Surface)
            .ToHashSet();

        return surfaces.Contains(first.Surface) || !surfaces.Contains(second.Surface) ? first : second;
    }

    /// <summary>Reads one account's placement, answering an account the placement left out as one placed nowhere.</summary>
    private static AdministrativeTarget PlacementOf(
        IReadOnlyDictionary<MailAccountId, AdministrativeTarget> placements,
        MailAccountId account) =>
        placements.GetValueOrDefault(account) ?? AdministrativeTarget.Unplaced;

    private async Task<AdministrativeTarget> PlaceMailAccountAsync(MailAccountId account, CancellationToken cancellationToken) =>
        PlacementOf(await this.targets.PlaceMailAccountsAsync([account], cancellationToken), account);

    private Task<AdministrativeTarget> PlaceScopeAsync(AssignmentScope scope, CancellationToken cancellationToken) =>
        scope.Kind switch
        {
            AssignmentScopeKind.Organization => Task.FromResult(AdministrativeTarget.OrganizationItself(scope.Target)),
            AssignmentScopeKind.User => this.targets.PlaceUserAsync(UserId.Create(scope.Target), cancellationToken),
            _ => Task.FromResult(AdministrativeTarget.Unplaced),
        };

    private async Task<bool> PermitsOverAsync(
        MailFathomPermission permission,
        Func<CancellationToken, Task<AdministrativeTarget>> place,
        CancellationToken cancellationToken)
    {
        if (!this.PermitsAtAnyScope(permission))
        {
            return false;
        }

        var caller = this.principals.Current!;

        return caller.Holds(permission) || caller.HoldsOver(permission, await place(cancellationToken));
    }

    private async Task RequirePermissionOverAsync(
        MailFathomPermission permission,
        Func<CancellationToken, Task<AdministrativeTarget>> place,
        CancellationToken cancellationToken)
    {
        var caller = this.RequireCaller(permission);

        if (!caller.Holds(permission) && !caller.HoldsOver(permission, await place(cancellationToken)))
        {
            throw PrincipalNotAuthorizedException.MissingPermission(permission);
        }
    }

    private AuthorizedPrincipal RequireCaller(MailFathomPermission permission)
    {
        if (!permission.IsSpecified)
        {
            throw new ArgumentException(
                "A use case must require a published permission rather than the unspecified default.",
                nameof(permission));
        }

        var principal = this.RequirePrincipal();

        if (principal.Kind != AuthorizedPrincipalKind.Caller)
        {
            throw PrincipalNotAuthorizedException.WrongPrincipalKind(AuthorizedPrincipalKind.Caller);
        }

        return principal;
    }

    private void RequireKind(AuthorizedPrincipalKind admittedKind)
    {
        var principal = this.RequirePrincipal();

        if (principal.Kind != admittedKind)
        {
            throw PrincipalNotAuthorizedException.WrongPrincipalKind(admittedKind);
        }
    }

    private AuthorizedPrincipal RequirePrincipal() =>
        this.principals.Current ?? throw PrincipalNotAuthorizedException.NoPrincipal();

    /// <summary>Places nothing, so only the deployment scope covers what an operation names.</summary>
    private sealed class UnplacedTargets : IAdministrativeTargets
    {
        internal static UnplacedTargets Instance { get; } = new();

        public Task<IReadOnlyDictionary<MailAccountId, AdministrativeTarget>> PlaceMailAccountsAsync(
            IReadOnlyCollection<MailAccountId> accounts,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<MailAccountId, AdministrativeTarget>>(
                accounts.Distinct().ToDictionary(account => account, _ => AdministrativeTarget.Unplaced));

        public Task<AdministrativeTarget> PlaceUserAsync(UserId user, CancellationToken cancellationToken) =>
            Task.FromResult(AdministrativeTarget.Unplaced);
    }
}
