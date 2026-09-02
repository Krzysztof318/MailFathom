// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Cli.Administration.Owners;

/// <summary>The credential to provision, as the command sends it.</summary>
/// <param name="Method">Which of the published methods the credential is presented by.</param>
/// <param name="Username">The name the owner will sign in with, where the method is a password.</param>
/// <param name="Password">The password the owner will type, where the method is a password.</param>
/// <param name="PublicKey">The client's public key, where the method verifies signed assertions.</param>
/// <param name="Issuer">The authorization server's issuer identifier, where the method maps a validated subject.</param>
/// <param name="Subject">That server's own identifier for the person, where the method maps a validated subject.</param>
/// <param name="Permissions">The published permission names the credential holds, or <see langword="null" /> to hold the whole mail surface.</param>
/// <remarks><see cref="ToString" /> reports no field at all: the method alone would be safe, and a record that printed one field is a record somebody eventually printed while believing it printed none.</remarks>
internal sealed record OwnerCredentialProvisioningRequest(
    string Method,
    string? Username,
    string? Password,
    string? PublicKey,
    string? Issuer,
    string? Subject,
    IReadOnlyList<string>? Permissions)
{
    /// <inheritdoc />
    public override string ToString() => nameof(OwnerCredentialProvisioningRequest);
}
