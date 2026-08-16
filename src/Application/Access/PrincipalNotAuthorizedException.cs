// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
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
/// The message carries a published permission name, the identity the work was admitted under, or neither. All three are
/// MailFathom's own configured names, which is what the message rule on <see cref="MailFathomException" /> permits.
/// </para>
/// </remarks>
public sealed class PrincipalNotAuthorizedException : MailFathomException
{
    private PrincipalNotAuthorizedException(string operatorSafeMessage, MailFathomPermission requiredPermission)
        : base(operatorSafeMessage) => this.RequiredPermission = requiredPermission;

    /// <summary>Gets the permission that would have sufficed, unspecified when the refusal was about the kind of principal rather than about a grant.</summary>
    /// <remarks>
    /// The closed enumeration already models "no permission", so absence is expressed in the value rather than by a
    /// nullable property. Ask <see cref="MailFathomPermission.IsSpecified" /> before reporting it: a boundary that
    /// names a permission where none was required would tell an operator to grant something that would not have helped.
    /// </remarks>
    public MailFathomPermission RequiredPermission { get; }

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.PrincipalNotAuthorized;

    /// <summary>Reports a caller that reached an operation its grant does not carry.</summary>
    /// <param name="requiredPermission">The permission the operation requires.</param>
    /// <param name="identity">The configured identity the work was admitted under.</param>
    /// <returns>The failure to raise.</returns>
    internal static PrincipalNotAuthorizedException MissingPermission(
        MailFathomPermission requiredPermission,
        string identity) =>
        new(
            $"'{identity}' was not granted '{requiredPermission.Name}'.",
            requiredPermission);

    /// <summary>Reports an operation reached under a kind of principal it does not admit.</summary>
    /// <param name="admittedKind">The one kind the operation admits.</param>
    /// <param name="identity">The configured identity the work was admitted under.</param>
    /// <returns>The failure to raise.</returns>
    internal static PrincipalNotAuthorizedException WrongPrincipalKind(
        AuthorizedPrincipalKind admittedKind,
        string identity) =>
        new(
            $"The operation is reached under {Describe(admittedKind)} and '{identity}' is not one.",
            default);

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
