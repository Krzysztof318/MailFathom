// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Cli.Administration.Owners;

/// <summary>What provisioning a credential produced.</summary>
/// <param name="CredentialId">The identifier the new credential carries, which every later act on it names.</param>
/// <param name="Lookup">What the credential will be resolved by, where the deployment publishes it.</param>
/// <param name="Key">The key the deployment minted, where the method is one it mints — carried here and never again.</param>
/// <remarks><see cref="ToString" /> reports the identifier alone, so a diagnostic rendering this record cannot print the one field that is a secret.</remarks>
internal sealed record OwnerCredentialProvisioned(Guid CredentialId, string? Lookup, string? Key)
{
    /// <inheritdoc />
    public override string ToString() => $"{nameof(OwnerCredentialProvisioned)} {{ {this.CredentialId} }}";
}

/// <summary>What replacing a credential's material produced.</summary>
/// <param name="Lookup">What the credential is resolved by from now on, where the deployment publishes it.</param>
/// <param name="Key">The key the deployment minted, where the method is one it mints — carried here and never again.</param>
/// <remarks><see cref="ToString" /> is redacted, for the reason <see cref="OwnerCredentialProvisioned" />'s is.</remarks>
internal sealed record OwnerCredentialRotated(string? Lookup, string? Key)
{
    /// <inheritdoc />
    public override string ToString() => nameof(OwnerCredentialRotated);
}
