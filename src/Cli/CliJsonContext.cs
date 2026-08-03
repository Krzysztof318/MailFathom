// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Cli;

/// <summary>The serialization contracts the command reads and writes, generated rather than discovered by reflection.</summary>
/// <remarks>
/// Source-generated because the published binary is trimmed: reflection-based serialization would either be trimmed
/// away or force the trimmer to keep enough metadata that trimming stops being worth doing. Stating the contracts here
/// also means an unexpected field in a response is a compile-time question rather than a runtime surprise.
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(AdminSession))]
[JsonSerializable(typeof(Dictionary<string, StoredCredential>))]
internal sealed partial class CliJsonContext : JsonSerializerContext;
