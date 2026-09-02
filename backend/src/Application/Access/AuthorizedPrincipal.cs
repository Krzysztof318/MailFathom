// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.Access;

/// <summary>Whoever a unit of work is running for, whose mail it is acting on, and what they were granted.</summary>
/// <remarks>
/// <para>
/// This is the whole of what the application layer learns about the outside of a request. It carries the identity the
/// work was admitted under, the owner it is acting for where there is one, and the permissions that identity holds, and
/// nothing else: no credential, no claim an authorization server issued, no scheme, no transport. Which credential
/// admitted a caller is a question the transport answered and no use case has any business re-deciding, which is why
/// nothing here can be asked it.
/// </para>
/// <para>
/// The owner and the permissions are two axes rather than one, and neither implies the other. A permission says what
/// the work may do and the owner says whose mail it may do it to, so ownership adds no name to the published permission
/// set and a grant however broad still reaches one owner's mail. A principal acting for nobody is not a principal
/// acting for everybody: the deployment administrator and this process's own identity carry no owner, and every use
/// case that reads or writes one owner's mail refuses them. A record that belongs to an owner without being mail — the
/// contact book is the one — may resolve the absent owner instead of refusing, and where it does it says what it
/// resolves it to; <see cref="AccessAuthorization.ActingOwner" /> is the only reading that permits it.
/// </para>
/// <para>
/// It is an ordinary class rather than a record, because two principals are never compared. A record's generated
/// equality would compare <see cref="Permissions" /> by reference and report two identical grants as different, which
/// is worse than having no equality at all.
/// </para>
/// <para>
/// The four factories below are the only way to obtain one, so a principal always states which kind it is and a kind
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
        MailOwnerId? owner,
        IReadOnlySet<MailFathomPermission> permissions)
    {
        this.Kind = kind;
        this.Identity = identity;
        this.Owner = owner;
        this.Permissions = permissions;
    }

    /// <summary>Gets the principal work no caller requested runs under.</summary>
    /// <remarks>It holds no permission by construction, so a use case reachable by a caller refuses it exactly as it refuses a caller granted nothing. Admitting it is a decision a use case states, never one a permission carries.</remarks>
    public static AuthorizedPrincipal Process { get; } = new(
        AuthorizedPrincipalKind.ProcessIdentity,
        ProcessIdentityName,
        owner: null,
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

    /// <summary>Gets the owner this work is acting for, or <see langword="null" /> where it acts for nobody's mail.</summary>
    /// <remarks>
    /// <para>
    /// Absence is a state rather than a gap. This process's own identity acts for no owner because work no caller
    /// requested is not being done on anybody's behalf, and the deployment administrator acts for none because the acts
    /// it reaches are the deployment's rather than one owner's. Both are refused by a use case that reads or writes one
    /// owner's mail, which is what stops "acting for nobody" from being read as "acting for everybody" — and a use case
    /// over a record that belongs to an owner without being mail resolves the absence rather than reading it as
    /// everybody, which is a different act and is stated where it happens.
    /// </para>
    /// <para>
    /// Nothing derives it from the permissions and nothing derives the permissions from it. It is stated by whatever
    /// admitted the work — the transport for a caller, the redeemed ticket for a capability — so a use case reads it
    /// rather than resolving it, and two use cases in one unit of work cannot come to disagree about whose mail they are
    /// reading.
    /// </para>
    /// </remarks>
    public MailOwnerId? Owner { get; }

    /// <summary>Gets the permissions this principal holds, which is empty for every kind but a caller.</summary>
    public IReadOnlySet<MailFathomPermission> Permissions { get; }

    /// <summary>Describes a caller the transport admitted that acts for no owner's mail.</summary>
    /// <param name="identity">What the transport admitted the caller as, in the forms <see cref="Identity" /> describes.</param>
    /// <param name="grantedPermissions">The permissions the entry that admitted it resolved to, empty when it granted none.</param>
    /// <returns>The principal the use cases the caller reaches are consulted with.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="identity" /> or <paramref name="grantedPermissions" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="identity" /> is empty or white space, which would leave a principal nothing can be reported by.</exception>
    /// <remarks>
    /// <para>
    /// This is the deployment administrator: a caller whose acts are the deployment's own rather than one person's
    /// mail. Every use case that reads or writes mail on somebody's behalf refuses it, so the administrative surface
    /// cannot become a way to read a mailbox by holding an administrative grant.
    /// </para>
    /// <para>
    /// An unspecified permission is dropped rather than carried, because the struct default names no capability and
    /// holding one would mean holding nothing under a name that reads like something.
    /// </para>
    /// </remarks>
    public static AuthorizedPrincipal Caller(
        string identity,
        IEnumerable<MailFathomPermission> grantedPermissions) =>
        AdmittedCaller(owner: null, identity, grantedPermissions);

    /// <summary>Describes a caller the transport admitted that acts for one owner's mail.</summary>
    /// <param name="owner">The owner whose mail the caller was admitted to act on.</param>
    /// <param name="identity">What the transport admitted the caller as, in the forms <see cref="Identity" /> describes.</param>
    /// <param name="grantedPermissions">The permissions the entry that admitted it resolved to, empty when it granted none.</param>
    /// <returns>The principal the use cases the caller reaches are consulted with.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="identity" /> or <paramref name="grantedPermissions" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="identity" /> is empty or white space, or when <paramref name="owner" /> names nobody.</exception>
    /// <remarks>
    /// This is the caller every mail-reading surface admits. The owner is decided by whatever admitted the caller and is
    /// carried rather than chosen, so no argument of any tool can move it: a request cannot name an owner, and a grant
    /// however broad cannot widen the mail it reaches beyond that owner's own.
    /// </remarks>
    public static AuthorizedPrincipal CallerActingFor(
        MailOwnerId owner,
        string identity,
        IEnumerable<MailFathomPermission> grantedPermissions)
    {
        if (!owner.IsSpecified)
        {
            throw new ArgumentException(
                "A caller admitted to act for an owner is admitted for a named one, never for the identity that names nobody.",
                nameof(owner));
        }

        return AdmittedCaller(owner, identity, grantedPermissions);
    }

    /// <summary>Describes the principal a verified signature produced.</summary>
    /// <param name="owner">The owner the capability was minted for, whose mail is the only mail it reaches.</param>
    /// <param name="authorizedObject">MailFathom's own description of the one object the signature was bounded to.</param>
    /// <returns>The principal the use case behind the capability is consulted with.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="authorizedObject" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="authorizedObject" /> is empty or white space, or when <paramref name="owner" /> names nobody.</exception>
    /// <remarks>
    /// The bound itself stays where a signature put it: the verified ticket the use case is handed names the object,
    /// and reading that is what confines the work. What this principal adds is the statement that a signature — rather
    /// than a credential or this process — is what authorized the work at all, so a use case reached under a capability
    /// admits that kind by name instead of admitting an unidentified caller. The owner is carried beside it because a
    /// capability is redeemed by whoever holds the URL, and the mail behind it is one owner's rather than the
    /// deployment's.
    /// </remarks>
    public static AuthorizedPrincipal SignedCapability(MailOwnerId owner, string authorizedObject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authorizedObject);

        if (!owner.IsSpecified)
        {
            throw new ArgumentException(
                "A capability is minted for a named owner, never for the identity that names nobody.",
                nameof(owner));
        }

        return new AuthorizedPrincipal(
            AuthorizedPrincipalKind.SignedCapability,
            authorizedObject,
            owner,
            new HashSet<MailFathomPermission>());
    }

    private static AuthorizedPrincipal AdmittedCaller(
        MailOwnerId? owner,
        string identity,
        IEnumerable<MailFathomPermission> grantedPermissions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        ArgumentNullException.ThrowIfNull(grantedPermissions);

        return new AuthorizedPrincipal(
            AuthorizedPrincipalKind.Caller,
            identity,
            owner,
            grantedPermissions.Where(permission => permission.IsSpecified).ToHashSet());
    }

    /// <summary>Reports whether this principal was granted one named capability.</summary>
    /// <param name="permission">The capability being asked about.</param>
    /// <returns><see langword="true" /> when the principal holds it.</returns>
    /// <remarks>Asks the grant alone. That a kind other than a caller never holds one is a property of how a principal is composed rather than a case decided here.</remarks>
    public bool Holds(MailFathomPermission permission) => this.Permissions.Contains(permission);
}
