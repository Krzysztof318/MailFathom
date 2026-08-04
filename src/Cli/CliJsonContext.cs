// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Authorization;
using MailFathom.Cli.Credentials;
using MailFathom.Common.OAuth;

namespace MailFathom.Cli;

/// <summary>The serialization contracts the command reads and writes, generated rather than discovered by reflection.</summary>
/// <remarks>
/// <para>
/// Source-generated because the published binary is trimmed: reflection-based serialization would either be trimmed
/// away or force the trimmer to keep enough metadata that trimming stops being worth doing. Stating the contracts here
/// also means an unexpected field in a response is a compile-time question rather than a runtime surprise.
/// </para>
/// <para>
/// Names are matched without regard to case, which matters for the two OAuth responses this shares with
/// <see cref="OAuthJsonContext" />: the same wire shape reaches this process through both, and a server whose casing
/// differs from the specification's would otherwise sign in through <c>mailbox authorize</c> and fail through
/// <c>login</c>. One policy for one shape rather than two that happen to agree today.
/// </para>
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = true)]
[JsonSerializable(typeof(AdminSession))]
[JsonSerializable(typeof(MailboxRefreshTokenRequest))]
[JsonSerializable(typeof(AdminProblem))]
[JsonSerializable(typeof(StoredCredentials))]
[JsonSerializable(typeof(ProtectedResourceMetadata))]
[JsonSerializable(typeof(AuthorizationServerMetadata))]
[JsonSerializable(typeof(OAuthTokenResponse))]
[JsonSerializable(typeof(OAuthDeviceAuthorizationResponse))]
internal sealed partial class CliJsonContext : JsonSerializerContext;
