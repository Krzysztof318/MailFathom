// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;
using MailFathom.Client.Backend.Authorization;

namespace MailFathom.Client.Backend;

/// <summary>Every document this client reads off the wire, with the readers generated at compile time.</summary>
/// <remarks>
/// <para>
/// Source-generated rather than reflection-based, and not as a preference. The browser head publishes trimmed, and a
/// reflection-based reader is removed by the trimmer rather than reported — so the failure would arrive as a screen
/// that works in a debug build and throws in the published one. <c>.config/BannedSymbols.txt</c> refuses the
/// reflection overloads outright for that reason, which makes this the only way to read a body here.
/// </para>
/// <para>
/// The naming policy is camel case, which is what the deployment's own minimal APIs write. The OAuth documents state
/// their snake-case names field by field instead, because those names are fixed by RFC 6749, RFC 8414, and RFC 9728
/// rather than by either end's serializer settings.
/// </para>
/// </remarks>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DeploymentSession))]
[JsonSerializable(typeof(ProtectedResourceMetadata))]
[JsonSerializable(typeof(AuthorizationServerMetadata))]
[JsonSerializable(typeof(OAuthTokenResponse))]
internal sealed partial class DeploymentJsonContext : JsonSerializerContext;
