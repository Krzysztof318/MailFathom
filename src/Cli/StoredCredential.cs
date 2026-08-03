// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Cli;

/// <summary>A credential the operator signed in to one endpoint with.</summary>
/// <param name="Token">The bearer credential presented on every request to that endpoint.</param>
/// <param name="Credential">The name the service reported for it, kept so the command can say who it is signed in as without asking again.</param>
/// <remarks>The token is stored because that is what signing in is for; see <see cref="CredentialStore" /> for how the file is protected.</remarks>
internal sealed record StoredCredential(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("credential")] string Credential)
{
    /// <inheritdoc />
    /// <remarks>Redacted, so no diagnostic, log, or exception message can print the token by formatting the record it lives in.</remarks>
    public override string ToString() => $"{nameof(StoredCredential)} {{ {this.Credential} }}";
}
