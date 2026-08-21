// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using System.Text.Json.Serialization;

namespace MailFathom.SyntheticMail.Configuration;

/// <summary>The one serialization contract this tool reads, generated rather than discovered by reflection.</summary>
/// <remarks>
/// Source-generated for the reason <c>mfctl</c>'s context is: <c>.config/BannedSymbols.txt</c> refuses the reflective
/// overloads outright, so stating the contract is the only way to read the file at all. Names are matched without
/// regard to case, because a developer writing their own credential file should not have a run refused over a capital
/// letter.
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(SendingAccountDocument))]
internal sealed partial class SyntheticMailJsonContext : JsonSerializerContext;
