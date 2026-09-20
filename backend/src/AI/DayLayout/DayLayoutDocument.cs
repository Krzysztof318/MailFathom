// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.AI.DayLayout;

/// <summary>The shape a day-layout agent answers in, before anything of it is believed.</summary>
/// <remarks>
/// It stays inside this boundary and never becomes a suggestion. Every field is optional and every value is untrusted:
/// what a model wrote is read into this and then validated into
/// <see cref="Application.Tasks.DayLayoutSuggestion" />, which is the type the rest of the system works with.
/// </remarks>
internal sealed record DayLayoutDocument
{
    /// <summary>Gets what the model said to do and when.</summary>
    [JsonPropertyName("placements")]
    public IReadOnlyList<DayLayoutPlacementDocument>? Placements { get; init; }

    /// <summary>Gets the turn's own numbers for the tasks the model said will not fit the day.</summary>
    [JsonPropertyName("notToday")]
    public IReadOnlyList<int>? NotToday { get; init; }
}

/// <summary>One placement as the model wrote it, with nothing about it yet established.</summary>
/// <remarks>
/// The task is the number the turn gave it rather than an identifier, which is why it is an integer here: a number
/// outside the range the turn published names no task and the placement falls away with it.
/// </remarks>
internal sealed record DayLayoutPlacementDocument
{
    /// <summary>Gets the turn's own number for the task being placed.</summary>
    [JsonPropertyName("task")]
    public int? Task { get; init; }

    /// <summary>Gets when the model said to begin it, as it wrote it.</summary>
    [JsonPropertyName("startAt")]
    public string? StartAt { get; init; }

    /// <summary>Gets how many minutes the model said to allow for it.</summary>
    [JsonPropertyName("minutes")]
    public int? Minutes { get; init; }
}
