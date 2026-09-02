// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Common.MailboxOAuth;

/// <summary>What an interactive authorization run produced for the operator to provision.</summary>
/// <param name="RefreshToken">The long-lived token MailFathom exchanges for access tokens at run time.</param>
/// <param name="AccessTokenExpiresAt">When the access token issued alongside it stops working, which the run reports only as evidence that the grant works.</param>
/// <remarks>
/// <para>
/// The refresh token is returned rather than written anywhere. MailFathom reads every credential through a secret
/// reference — a file, an environment variable, or a systemd credential — and a tool that wrote one into a
/// configuration file would be establishing a second, unreviewed way for a credential to exist on disk. Handing it to
/// the operator keeps provisioning in the mechanism that already owns rotation.
/// </para>
/// <para>
/// <see cref="ToString" /> is redacted, so the value reaches a terminal only where the command deliberately prints it.
/// </para>
/// </remarks>
public sealed record MailboxAuthorizationGrant(string RefreshToken, DateTimeOffset AccessTokenExpiresAt)
{
    /// <inheritdoc />
    public override string ToString() => "***";
}
