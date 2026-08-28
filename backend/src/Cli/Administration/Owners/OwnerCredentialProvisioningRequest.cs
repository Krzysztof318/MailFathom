// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Cli.Administration.Owners;

/// <summary>The credential to provision, as the command sends it.</summary>
/// <param name="Username">The name the owner will sign in with.</param>
/// <param name="Password">The password the owner will type.</param>
/// <remarks><see cref="ToString" /> reports neither half: the username alone would be safe, and a record that printed one half is a record somebody eventually printed while believing it printed neither.</remarks>
internal sealed record OwnerCredentialProvisioningRequest(string Username, string Password)
{
    /// <inheritdoc />
    public override string ToString() => nameof(OwnerCredentialProvisioningRequest);
}
