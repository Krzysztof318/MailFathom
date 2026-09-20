// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using System.Text.Json.Serialization;

namespace MailFathom.AI.CalendarEvents;

/// <summary>Reads an extraction agent's answer without reflection, as everything serialized in this repository is.</summary>
/// <remarks>
/// The options are deliberately forgiving about case and about a field nobody declared, because the text being read
/// was written by a model rather than by a program. What they are not forgiving about is what the values mean: that is
/// <see cref="CalendarEventExtractionReading" />'s to decide against what an event is allowed to be.
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(CalendarEventExtractionDocument))]
internal sealed partial class CalendarEventExtractionJsonContext : JsonSerializerContext;
