// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.Access;

/// <summary>Whoever a unit of work is running for, and what they were granted.</summary>
/// <remarks>
/// <para>
/// This is the whole of what the application layer learns about the outside of a request. It carries the identity the
/// work was admitted under and the permissions that identity holds, and nothing else: no credential, no
/// claim an authorization server issued, no scheme, no transport. Which credential admitted a caller is a question the
/// transport answered and no use case has any business re-deciding, which is why nothing here can be asked it.
/// </para>
/// <para>
/// It is an ordinary class rather than a record, because two principals are never compared. A record's generated
/// equality would compare <see cref="Permissions" /> by reference and report two identical grants as different, which
/// is worse than having no equality at all.
/// </para>
/// <para>
/// The three factories below are the only way to obtain one, so a principal always states which kind it is and a kind
/// added later cannot arrive with an unset identity. <see cref="Process" /> is a single instance because there is one
/// process and it holds no per-work state.
/// </para>
/// </remarks>
public sealed class AuthorizedPrincipal
{
    /// <summary>The name the process's own identity is reported under, which is MailFathom itself rather than anything an operator configured.</summary>
    public const string ProcessIdentityName = "mailfathom";

    private AuthorizedPrincipal(
        AuthorizedPrincipalKind kind,
        string identity,
        IReadOnlySet<MailFathomPermission> permissions)
    {
        this.Kind = kind;
        this.Identity = identity;
        this.Permissions = permissions;
    }

    /// <summary>Gets the principal work no caller requested runs under.</summary>
    /// <remarks>It holds no permission by construction, so a use case reachable by a caller refuses it exactly as it refuses a caller granted nothing. Admitting it is a decision a use case states, never one a permission carries.</remarks>
    public static AuthorizedPrincipal Process { get; } = new(
        AuthorizedPrincipalKind.ProcessIdentity,
        ProcessIdentityName,
        new HashSet<MailFathomPermission>());

    /// <summary>Gets which of the three things this principal is.</summary>
    public AuthorizedPrincipalKind Kind { get; }

    /// <summary>Gets the identity the work was admitted under.</summary>
    /// <remarks>
    /// <para>
    /// For a caller it is what the transport admitted it as, and that is not one shape: a configured key's own name
    /// where the operator wrote the credential, the issuer and subject the deployment authorized where a token brought
    /// it, and the fixed word <c>anonymous</c> where the surface configures no credential for one caller to be told
    /// apart from another. Never the credential material in any of the three. For the process identity it is
    /// <see cref="ProcessIdentityName" />, and for a signed capability it is the object the signature was bounded to.
    /// </para>
    /// <para>
    /// Nothing decides access from it. It is here for a boundary that has to name the caller to its own readers, and
    /// because the token form carries a host name and a remote party's identifier for a person, it never reaches a
    /// failure message — <see cref="PrincipalNotAuthorizedException" /> states why.
    /// </para>
    /// </remarks>
    public string Identity { get; }

    /// <summary>Gets the permissions this principal holds, which is empty for every kind but a caller.</summary>
    public IReadOnlySet<MailFathomPermission> Permissions { get; }

    /// <summary>Describes a caller the transport admitted.</summary>
    /// <param name="identity">What the transport admitted the caller as, in the forms <see cref="Identity" /> describes.</param>
    /// <param name="grantedPermissions">The permissions the entry that admitted it resolved to, empty when it granted none.</param>
    /// <returns>The principal the use cases the caller reaches are consulted with.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="identity" /> or <paramref name="grantedPermissions" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="identity" /> is empty or white space, which would leave a principal nothing can be reported by.</exception>
    /// <remarks>An unspecified permission is dropped rather than carried, because the struct default names no capability and holding one would mean holding nothing under a name that reads like something.</remarks>
    public static AuthorizedPrincipal Caller(
        string identity,
        IEnumerable<MailFathomPermission> grantedPermissions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        ArgumentNullException.ThrowIfNull(grantedPermissions);

        return new AuthorizedPrincipal(
            AuthorizedPrincipalKind.Caller,
            identity,
            grantedPermissions.Where(permission => permission.IsSpecified).ToHashSet());
    }

    /// <summary>Describes the principal a verified signature produced.</summary>
    /// <param name="authorizedObject">MailFathom's own description of the one object the signature was bounded to.</param>
    /// <returns>The principal the use case behind the capability is consulted with.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="authorizedObject" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="authorizedObject" /> is empty or white space.</exception>
    /// <remarks>
    /// The bound itself stays where a signature put it: the verified ticket the use case is handed names the object,
    /// and reading that is what confines the work. What this principal adds is the statement that a signature — rather
    /// than a credential or this process — is what authorized the work at all, so a use case reached under a capability
    /// admits that kind by name instead of admitting an unidentified caller.
    /// </remarks>
    public static AuthorizedPrincipal SignedCapability(string authorizedObject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authorizedObject);

        return new AuthorizedPrincipal(
            AuthorizedPrincipalKind.SignedCapability,
            authorizedObject,
            new HashSet<MailFathomPermission>());
    }

    /// <summary>Reports whether this principal was granted one named capability.</summary>
    /// <param name="permission">The capability being asked about.</param>
    /// <returns><see langword="true" /> when the principal holds it.</returns>
    /// <remarks>Asks the grant alone. That a kind other than a caller never holds one is a property of how a principal is composed rather than a case decided here.</remarks>
    public bool Holds(MailFathomPermission permission) => this.Permissions.Contains(permission);
}
