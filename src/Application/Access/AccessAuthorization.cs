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
