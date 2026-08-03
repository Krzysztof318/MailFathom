// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Cli.Credentials;

/// <summary>A profile a command is about to act through, with its token readable.</summary>
/// <param name="Name">The operator's name for the deployment, which is what a message says rather than an address.</param>
/// <param name="Endpoint">The address to send to.</param>
/// <param name="Token">The bearer credential, opened.</param>
/// <param name="Credential">The name the deployment reported for the credential when it was stored.</param>
/// <remarks>
/// Distinct from <see cref="StoredCredential" /> because the two hold the token in different states: what is written to
/// the file is sealed, and what a command sends is not. Keeping one type for both would leave every caller having to
/// know which of the two it was holding.
/// </remarks>
internal sealed record SignedInProfile(string Name, Uri Endpoint, string Token, string Credential)
{
    /// <inheritdoc />
    /// <remarks>Redacted, so no diagnostic or exception message prints the token by formatting the record it lives in.</remarks>
    public override string ToString() => $"{nameof(SignedInProfile)} {{ {this.Name}, {this.Endpoint}, {this.Credential} }}";
}
