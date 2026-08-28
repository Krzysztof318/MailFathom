// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.Access.Credentials;

/// <summary>Reads a client's public key into the two things a credential row keeps of it.</summary>
/// <remarks>
/// <para>
/// A public key registered for an owner is stored whole, because the assertion it verifies has to be checked against
/// it, and it is resolved by a fingerprint, because the assertion names one in its own <c>kid</c> header rather than
/// naming a credential this deployment invented a handle for. Deriving both from the same reading is what keeps the
/// two in step: a fingerprint computed anywhere but beside the material it describes is one that can be computed over
/// a different encoding of the same key.
/// </para>
/// <para>
/// The port exists so no use case, no endpoint, and no command holds an opinion about which key types are accepted,
/// what encoding a client sends, or how a fingerprint is derived. All of that belongs to the one adapter behind this,
/// beside the verification that reads the stored material back.
/// </para>
/// </remarks>
public interface IClientPublicKeyReader
{
    /// <summary>Reads a written public key into what is stored and what it is resolved by.</summary>
    /// <param name="written">The key as the operator supplied it, in the encoding the deployment publishes.</param>
    /// <param name="publicKey">The key when the written form is usable; otherwise <see langword="null" />.</param>
    /// <returns><see langword="true" /> when the written form is a public key of a kind this deployment accepts.</returns>
    /// <remarks>It answers rather than raising because its caller is an operator provisioning a credential, who is answered with what to supply instead.</remarks>
    bool TryRead(string? written, out ClientPublicKey? publicKey);

    /// <summary>Describes what a written public key may be, for a refusal an operator reads.</summary>
    /// <returns>The sentence naming the accepted form.</returns>
    /// <remarks>Written by the adapter that decides it rather than by each surface that refuses one, so an operator provisioning a credential and a validator refusing theirs describe one rule.</remarks>
    string DescribeAcceptedForm();
}

/// <summary>One client's public key, as the deployment keeps it and resolves it.</summary>
/// <param name="Material">The key in the deployment's own canonical encoding, which is what an assertion is verified against.</param>
/// <param name="Lookup">The key's fingerprint, which an assertion names and a row is resolved by.</param>
/// <remarks>Nothing here is secret, which is the whole point of the method: a deployment holding this cannot sign anything, so a copy of the row, of a backup, or of an administrative answer is worth nothing to whoever took it.</remarks>
public sealed record ClientPublicKey(string Material, OwnerCredentialLookup Lookup);
