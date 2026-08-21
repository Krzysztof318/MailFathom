// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Cli.Administration;

/// <summary>The grant the command asks a deployment to keep for one of its mail accounts.</summary>
/// <param name="Account">The account identifier as it appears in the deployment's own configuration.</param>
/// <param name="RefreshToken">The token the authorization run produced.</param>
/// <remarks>
/// The command's own contract rather than the service's record of the same shape, for the reason
/// <see cref="AdminSession" /> is one: the two sides are separate artifacts that meet over the wire, and sharing a type
/// would make the published binary depend on the host to state what it sends. <see cref="ToString" /> is redacted, so
/// no failure message can print the token by rendering the request it travelled in.
/// </remarks>
internal sealed record MailboxRefreshTokenRequest(
    [property: JsonPropertyName("account")] string Account,
    [property: JsonPropertyName("refreshToken")] string RefreshToken)
{
    /// <inheritdoc />
    public override string ToString() => $"{nameof(MailboxRefreshTokenRequest)} {{ {this.Account} }}";
}

/// <summary>What a deployment says when it refuses a request, read from the problem document it answers with.</summary>
/// <param name="Detail">The sentence written for the operator, which is the whole of what the command reports back.</param>
/// <param name="Permission">The one permission that would have sufficed, which a refusal for want of a grant carries and no other refusal does.</param>
/// <remarks>
/// Two fields of RFC 9457, because two are what the command uses. The rest of the document — the type, the title, the
/// status — restates either the status line the command already read or a URI it would have nothing to do with, and a
/// contract that named them would have to keep agreeing with a service that never sends anything else. The permission
/// is read as its own member rather than out of the sentence, so what the command tells an operator to grant does not
/// depend on how the deployment happened to word the refusal.
/// </remarks>
internal sealed record AdminProblem(
    [property: JsonPropertyName("detail")] string? Detail,
    [property: JsonPropertyName("permission")] string? Permission);
