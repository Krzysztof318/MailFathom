// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.SensitiveContent.Egress;

/// <summary>One user and what a read spanning every account they are assigned is scanned under.</summary>
/// <remarks>
/// Built only by <see cref="SensitiveContentEgressGuard.ReadPostureAcrossAccountsOfAsync" />, so a scope is never entered
/// under a posture somebody composed for a different user.
/// </remarks>
public sealed class UserSensitiveContentPosture
{
    internal UserSensitiveContentPosture(UserId user, SensitiveContentPosture posture)
    {
        this.User = user;
        this.Posture = posture;
    }

    /// <summary>Gets the user the read acts for.</summary>
    public UserId User { get; }

    /// <summary>Gets what the read's mail is scanned under.</summary>
    public SensitiveContentPosture Posture { get; }
}
