// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Backend.Authorization;

/// <summary>Everything a sign-in needs, once both discovery documents have been read and found usable.</summary>
/// <param name="Issuer">The authorization server that will issue the token.</param>
/// <param name="AuthorizationEndpoint">Where a person approves the sign-in.</param>
/// <param name="TokenEndpoint">Where the authorization code is exchanged.</param>
/// <param name="Resource">The identifier the issued token's audience must name.</param>
/// <param name="Scope">The scopes to ask for, space separated as RFC 6749 requires, and empty where the deployment published none.</param>
/// <remarks>
/// Nothing here was configured beyond the deployment's address and this client's own identifier: every value came from
/// the deployment or from the server the deployment named. That is what keeps signing in from depending on somebody
/// transcribing four values correctly, and it is why a deployment that moves an endpoint keeps working.
/// </remarks>
internal sealed record DeploymentAuthorization(
    string Issuer,
    Uri AuthorizationEndpoint,
    Uri TokenEndpoint,
    string Resource,
    string Scope);
