// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Access;

/// <summary>Which of the three things a use case can be reached under produced the work in hand.</summary>
/// <remarks>
/// <para>
/// The three are not degrees of the same thing and none of them is the absence of another. A caller presented a
/// credential this deployment admits and holds whatever that credential's entry granted; the process identity is what
/// work nobody asked for runs under; and a signed capability is a ticket this deployment issued for one object, which
/// is the whole of the authorization rather than a grant over a surface.
/// </para>
/// <para>
/// Modelling the process identity as its own kind is what keeps it from being a caller with everything granted. A
/// principal admitted by holding permissions would pass every ordinary check, so a use case that may run without a
/// caller says so by naming this kind instead.
/// </para>
/// </remarks>
public enum AuthorizedPrincipalKind
{
    /// <summary>Somebody who presented a credential a configured entry admits, holding what that entry granted.</summary>
    Caller = 0,

    /// <summary>MailFathom itself, running work no caller requested.</summary>
    ProcessIdentity = 1,

    /// <summary>A ticket this deployment signed, bounded to the one object it names and to the lifetime it was minted with.</summary>
    SignedCapability = 2,
}
