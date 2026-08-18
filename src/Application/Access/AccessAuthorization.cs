// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

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
/// <see cref="Permits" /> is the one member that reports instead of refusing, for a boundary composing an answer per
/// caller rather than performing an operation for one. It decides nothing of its own: it answers exactly what
/// <see cref="RequirePermission" /> would have refused, so the transport and the use case cannot come to disagree about
/// what holding a permission means.
/// </para>
/// </remarks>
public sealed class AccessAuthorization
{
    private readonly IAuthorizedPrincipalSource principals;

    /// <summary>Initializes the authorization over the principal of the unit of work in hand.</summary>
    /// <param name="principals">Reports whoever the work is running for.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="principals" /> is <see langword="null" />.</exception>
    public AccessAuthorization(IAuthorizedPrincipalSource principals)
    {
        ArgumentNullException.ThrowIfNull(principals);

        this.principals = principals;
    }

    /// <summary>Requires that an admitted caller holding one named capability is what reached this use case.</summary>
    /// <param name="permission">The capability the operation is published under.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="permission" /> names no published capability, which is a defect in the calling use case rather than a refusal.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the work was reached under no principal, under a principal that is not a caller, or by a caller whose grant omits the permission.</exception>
    public void RequirePermission(MailFathomPermission permission)
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

        if (!principal.Holds(permission))
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
        var surfaces = principal.Permissions.Select(static permission => permission.Surface).ToHashSet();

        return surfaces.Contains(first.Surface) || !surfaces.Contains(second.Surface) ? first : second;
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
}
