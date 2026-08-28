// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Cli.Administration.Owners;

/// <summary>Every credential one owner's clients present.</summary>
/// <param name="Owner">The owner the listing is about.</param>
/// <param name="Credentials">The credentials, oldest first.</param>
internal sealed record OwnerCredentialList(Guid Owner, IReadOnlyList<OwnerCredential> Credentials);

/// <summary>One credential an owner's clients present, as the deployment reports it.</summary>
/// <param name="Id">The identifier every later act on this credential names.</param>
/// <param name="Method">The published name of the method the credential is presented by.</param>
/// <param name="Lookup">What the credential is resolved by, or <see langword="null" /> where the deployment withholds it because it is derived from the secret.</param>
/// <param name="Permissions">The published permission names the credential holds.</param>
/// <param name="Enabled">Whether it currently authenticates anything.</param>
/// <param name="Version">How many times the record has been written, counting the act that provisioned it.</param>
/// <param name="CreatedAt">When the credential was provisioned.</param>
/// <param name="MaterialChangedAt">When what it is presented as was last replaced.</param>
/// <remarks>
/// No password, no hash, and no key digest, because the deployment publishes none of them. Every field here is a fact
/// about the record rather than about the secret, which is what makes a listing safe to print into a terminal, a
/// pipeline, or whatever a script captured.
/// </remarks>
internal sealed record OwnerCredential(
    Guid Id,
    string Method,
    string? Lookup,
    IReadOnlyList<string> Permissions,
    bool Enabled,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset MaterialChangedAt);
