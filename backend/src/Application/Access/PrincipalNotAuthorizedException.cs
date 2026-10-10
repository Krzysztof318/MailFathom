// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Failures;

namespace MailFathom.Application.Access;

/// <summary>The failure raised when a use case is reached by a principal that was not granted it.</summary>
/// <remarks>
/// <para>
/// It travels as an application failure rather than as a status code or a protocol result, because the same refusal has
/// to reach two boundaries that answer it differently: the MCP surface says nothing a caller can tell from a tool that
/// does not exist, and the administrative surface names the permission that would have sufficed. A use case that raised
/// either shape directly would have decided both.
/// </para>
/// <para>
/// One failure covers a caller whose grant omits the permission, work admitted under the wrong kind of principal, and a
/// use case reached under no principal at all. The message separates them for an operator reading a log; nothing else
/// does, and a boundary reports <see cref="RequiredPermission" /> rather than parsing prose.
/// </para>
/// <para>
/// The message names a published permission, a kind of principal, or neither, and never the identity the work was
/// admitted under. That identity is MailFathom's own name for a configured key, but for a token it is the issuer and
/// subject the deployment authorized — a host name and a remote party's identifier for a person — and the message rule
/// on <see cref="MailFathomException" /> admits neither. A boundary that has to name the caller reads
/// <see cref="AuthorizedPrincipal.Identity" /> and decides for itself what its own readers may see.
/// </para>
/// </remarks>
public sealed class PrincipalNotAuthorizedException : MailFathomException
{
    private PrincipalNotAuthorizedException(
        string operatorSafeMessage,
        MailFathomPermission requiredPermission,
        bool refusedForTheDeploymentAlone = false)
        : base(operatorSafeMessage)
    {
        this.RequiredPermission = requiredPermission;
        this.RefusedForTheDeploymentAlone = refusedForTheDeploymentAlone;
    }

    /// <summary>Gets the permission that would have sufficed, unspecified when the refusal was about the kind of principal rather than about a grant.</summary>
    /// <remarks>
    /// The closed enumeration already models "no permission", so absence is expressed in the value rather than by a
    /// nullable property. Ask <see cref="MailFathomPermission.IsSpecified" /> before reporting it: a boundary that
    /// names a permission where none was required would tell an operator to grant something that would not have helped.
    /// </remarks>
    public MailFathomPermission RequiredPermission { get; }

    /// <summary>Gets whether what was refused is an act only the deployment scope admits, decided before anything the request names was read.</summary>
    /// <remarks>
    /// A boundary serving a route that names a target says nothing of scope when it refuses, because whether a scope
    /// covers a target tells the caller the target exists. This marks the one refusal behind such a route that is the
    /// same whatever the request names, so the boundary may say the permission is held only below the deployment.
    /// </remarks>
    public bool RefusedForTheDeploymentAlone { get; }
    /// <summary>Gets whether the caller holds <see cref="RequiredPermission" />, only at no scope covering the one the act is bounded by.</summary>
    /// <remarks>A boundary that names the permission reads this to say so, because telling a caller it was not granted a name it holds sends an operator to look for an assignment that is already there.</remarks>
    public bool IsHeldTooNarrowly { get; private init; }

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.PrincipalNotAuthorized;

    /// <summary>Reports a caller that reached an operation its grant does not carry.</summary>
    /// <param name="requiredPermission">The permission the operation requires.</param>
    /// <returns>The failure to raise.</returns>
    internal static PrincipalNotAuthorizedException MissingPermission(MailFathomPermission requiredPermission) =>
        new($"The caller was not granted '{requiredPermission.Name}'.", requiredPermission);

    /// <summary>Reports a caller that reached an act only the deployment scope admits without holding its permission there.</summary>
    /// <param name="requiredPermission">The permission the act requires over the whole deployment.</param>
    /// <returns>The failure to raise, marked <see cref="RefusedForTheDeploymentAlone" />.</returns>
    internal static PrincipalNotAuthorizedException MissingOverTheDeployment(MailFathomPermission requiredPermission) =>
        new(
            $"The caller was not granted '{requiredPermission.Name}' over the whole deployment.",
            requiredPermission,
            refusedForTheDeploymentAlone: true);

    /// <summary>Reports a caller that holds a permission, but at no scope covering one somebody else holds it at, where a write would widen that somebody past the caller.</summary>
    /// <param name="requiredPermission">The permission held too narrowly.</param>
    /// <returns>The failure to raise.</returns>
    /// <remarks>It names the permission like <see cref="MissingPermission" />, because widening the grant is the remedy either way; the message tells an operator reading a log that the name is held, only not widely enough.</remarks>
    internal static PrincipalNotAuthorizedException HeldTooNarrowly(MailFathomPermission requiredPermission) =>
        new($"The caller holds '{requiredPermission.Name}' only at a scope narrower than the one the act is bounded by.", requiredPermission)
        {
            IsHeldTooNarrowly = true,
        };

    /// <summary>Reports an operation reached under a kind of principal it does not admit.</summary>
    /// <param name="admittedKind">The one kind the operation admits.</param>
    /// <returns>The failure to raise.</returns>
    internal static PrincipalNotAuthorizedException WrongPrincipalKind(AuthorizedPrincipalKind admittedKind) =>
        new($"The operation is reached under {Describe(admittedKind)}, and what reached it is not one.", default);

    /// <summary>Reports an operation scoped to a user's mail reached by a principal acting for no user.</summary>
    /// <returns>The failure to raise.</returns>
    /// <remarks>
    /// It names no permission, because no permission would have helped: the deployment administrator and this process's
    /// own identity are refused here however broad their grant, and widening one is the remedy an operator must not be
    /// pointed at. The message says the operation is one user's rather than the deployment's, which is the distinction
    /// an operator reading a log has to make.
    /// </remarks>
    internal static PrincipalNotAuthorizedException NoUser() =>
        new("The operation acts on one user's mail, and what reached it is acting for no user.", default);

    /// <summary>Reports an operation reached under no principal at all.</summary>
    /// <returns>The failure to raise.</returns>
    /// <remarks>This is the entrypoint that never stated what admitted it, so the refusal names nothing to grant: an operator's remedy is the missing adapter rather than a wider grant.</remarks>
    internal static PrincipalNotAuthorizedException NoPrincipal() =>
        new("The operation was reached under no principal.", default);

    private static string Describe(AuthorizedPrincipalKind kind) => kind switch
    {
        AuthorizedPrincipalKind.Caller => "an admitted caller",
        AuthorizedPrincipalKind.ProcessIdentity => "MailFathom's own identity",
        AuthorizedPrincipalKind.SignedCapability => "a capability this deployment signed",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "The value names no principal kind."),
    };
}
