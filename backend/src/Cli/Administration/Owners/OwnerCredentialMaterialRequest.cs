// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Cli.Administration.Owners;

/// <summary>The material to put in place of a credential's current material.</summary>
/// <param name="Method">The method the credential carries, which a rotation never changes.</param>
/// <param name="Username">The username the credential already signs in as, where the method is a password.</param>
/// <param name="Password">The new password the owner will type, where the method is a password.</param>
/// <param name="PublicKey">The client's new public key, where the method verifies signed assertions.</param>
/// <remarks><see cref="ToString" /> is redacted, for the reason <see cref="OwnerCredentialProvisioningRequest" />'s is.</remarks>
internal sealed record OwnerCredentialMaterialRequest(
    string Method,
    string? Username,
    string? Password,
    string? PublicKey)
{
    /// <inheritdoc />
    public override string ToString() => nameof(OwnerCredentialMaterialRequest);
}
