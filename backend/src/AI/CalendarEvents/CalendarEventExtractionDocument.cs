// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.AI.CalendarEvents;

/// <summary>The shape an extraction agent answers in, before anything of it is believed.</summary>
/// <remarks>
/// It stays inside this boundary and never becomes an event. Every field is optional and every value is untrusted:
/// what a model wrote is read into this and then validated into
/// <see cref="Application.Calendar.Extraction.ExtractedCalendarEvent" />, which is the type the rest of the system
/// works with.
/// </remarks>
internal sealed record CalendarEventExtractionDocument
{
    /// <summary>Gets the events the model says the text names, in the order it wrote them.</summary>
    [JsonPropertyName("events")]
    public IReadOnlyList<CalendarEventDocument>? Events { get; init; }
}

/// <summary>One event as the model wrote it, with nothing about it yet established.</summary>
/// <remarks>
/// The two instants are strings rather than dates because that is what is being judged: a model writes a zone offset
/// it was told not to, an hour that does not exist, and a day it resolved wrongly, and each of those has to be a
/// reading that fails rather than a parse that succeeds against whatever the runtime happened to accept.
/// </remarks>
internal sealed record CalendarEventDocument
{
    /// <summary>Gets what the model called the event.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    /// <summary>Gets when the model says it begins, written as a local date and time.</summary>
    [JsonPropertyName("start")]
    public string? Start { get; init; }

    /// <summary>Gets when the model says it ends, or <see langword="null" /> where the text stated no length.</summary>
    [JsonPropertyName("end")]
    public string? End { get; init; }
}
