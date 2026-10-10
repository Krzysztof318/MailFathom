// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Failures;

namespace MailFathom.Infrastructure.Persistence.Policies;

/// <summary>Indicates that the settings policy one scope stores could not be handed on.</summary>
/// <remarks>
/// <para>
/// A scope that stores no policy is not this: that is read as a policy stating nothing. What this covers is a row
/// holding a document past what this build reads a policy from, which the statement refuses rather than transfers.
/// No write through MailFathom produces one, so it is a row written beside it — by hand, or restored from a build
/// that read more.
/// </para>
/// <para>
/// Raised rather than returned, because no caller between the statement and the operator can act on it: the policy
/// cannot be opened to be corrected through the route that was asked for it. The message names the scope, the size,
/// and the limit, and never any part of the document.
/// </para>
/// </remarks>
public sealed class SettingsPolicyUnreadableException : MailFathomException
{
    /// <summary>Initializes a new failure naming which policy could not be read and what to do about it.</summary>
    /// <param name="operatorSafeMessage">A message naming the scope, the limit, and the operator's next step.</param>
    public SettingsPolicyUnreadableException(string operatorSafeMessage)
        : base(operatorSafeMessage)
    {
    }

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.SettingsPolicyUnreadable;
}
