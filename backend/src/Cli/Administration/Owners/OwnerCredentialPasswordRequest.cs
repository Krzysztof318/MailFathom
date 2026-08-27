// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Cli.Administration.Owners;

/// <summary>The password to put in place of a credential's current one.</summary>
/// <param name="Password">The new password the owner will type.</param>
/// <remarks><see cref="ToString" /> is redacted, for the reason <see cref="OwnerCredentialProvisioningRequest" />'s is.</remarks>
internal sealed record OwnerCredentialPasswordRequest(string Password)
{
    /// <inheritdoc />
    public override string ToString() => nameof(OwnerCredentialPasswordRequest);
}
