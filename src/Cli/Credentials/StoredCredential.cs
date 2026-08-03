// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Cli.Credentials;

/// <summary>One deployment the operator has signed in to, under the name they gave it.</summary>
/// <param name="Endpoint">The address the profile reaches, so a command needs only the name.</param>
/// <param name="Token">The bearer credential, encrypted; see <see cref="TokenProtector" />.</param>
/// <param name="Credential">The name the deployment reported for the credential, kept so the command can say who it is signed in as without asking again.</param>
internal sealed record StoredCredential(
    [property: JsonPropertyName("endpoint")] string Endpoint,
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("credential")] string Credential)
{
    /// <inheritdoc />
    /// <remarks>Redacted, so no diagnostic or exception message prints the token by formatting the record it lives in — even encrypted, which is a value worth not scattering.</remarks>
    public override string ToString() => $"{nameof(StoredCredential)} {{ {this.Endpoint}, {this.Credential} }}";
}

/// <summary>Everything the command remembers between invocations.</summary>
/// <param name="Default">The profile a command uses when the operator names none, which <c>login</c> and <c>switch</c> both set.</param>
/// <param name="Profiles">The signed-in deployments, keyed by the operator's own name for each.</param>
/// <remarks>
/// Keyed by name rather than by address, because a name is what an operator types and an address is what changes: a
/// deployment that moves port or gains a domain keeps its name, and its profile follows rather than becoming a second
/// entry nobody meant to create.
/// </remarks>
internal sealed record StoredCredentials(
    [property: JsonPropertyName("default")] string? Default,
    [property: JsonPropertyName("profiles")] Dictionary<string, StoredCredential> Profiles)
{
    /// <summary>Builds the state a machine that has never signed in is in.</summary>
    /// <returns>An empty store.</returns>
    internal static StoredCredentials Empty() =>
        new(Default: null, new Dictionary<string, StoredCredential>(StringComparer.OrdinalIgnoreCase));
}
