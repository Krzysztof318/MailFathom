// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.AI.Discovery;

/// <summary>The shape a composing agent answers in, before anything of it is believed.</summary>
/// <remarks>
/// It stays inside this boundary and never becomes the plan itself. Every field is optional and every value is
/// untrusted: what a model wrote is read into this and then validated into
/// <see cref="Application.Discovery.Presentation.PresentationPlan" />, which is the type a client receives. A source
/// name the run never declared, a column nobody catalogued, and a claim resting on nothing are all ordinary answers
/// here and none of them survives the reading.
/// </remarks>
internal sealed record DiscoveryResultDocument
{
    /// <summary>Gets the answer in the model's own words, which may rest on nothing.</summary>
    [JsonPropertyName("answer")]
    public string? Answer { get; init; }

    /// <summary>Gets how far the model says its answer is worth trusting, as one of the three bands.</summary>
    [JsonPropertyName("confidence")]
    public string? Confidence { get; init; }

    /// <summary>Gets the sources the model says its answer rests on, by the names the turn gave them.</summary>
    [JsonPropertyName("sources")]
    public IReadOnlyList<string>? Sources { get; init; }

    /// <summary>Gets the sides of the disagreement, where the model says the sources contradict each other.</summary>
    [JsonPropertyName("conflict")]
    public IReadOnlyList<DiscoveryConflictDocument>? Conflict { get; init; }

    /// <summary>Gets the dated events the model read out of the sources, for a question about change over time.</summary>
    [JsonPropertyName("events")]
    public IReadOnlyList<DiscoveryEventDocument>? Events { get; init; }

    /// <summary>Gets the columns the model proposes comparing across, by the catalogue's own names.</summary>
    [JsonPropertyName("columns")]
    public IReadOnlyList<string>? Columns { get; init; }

    /// <summary>Gets the rows of the comparison, each holding one cell per proposed column.</summary>
    [JsonPropertyName("rows")]
    public IReadOnlyList<DiscoveryRowDocument>? Rows { get; init; }
}

/// <summary>One side of a disagreement as a model wrote it.</summary>
internal sealed record DiscoveryConflictDocument
{
    /// <summary>Gets what this side says.</summary>
    [JsonPropertyName("statement")]
    public string? Statement { get; init; }

    /// <summary>Gets the sources saying it, by the names the turn gave them.</summary>
    [JsonPropertyName("sources")]
    public IReadOnlyList<string>? Sources { get; init; }
}

/// <summary>One dated event as a model wrote it.</summary>
internal sealed record DiscoveryEventDocument
{
    /// <summary>Gets when the event happened, as the correspondence dates it.</summary>
    [JsonPropertyName("occurredAt")]
    public DateTimeOffset? OccurredAt { get; init; }

    /// <summary>Gets what happened.</summary>
    [JsonPropertyName("summary")]
    public string? Summary { get; init; }

    /// <summary>Gets what it happened to.</summary>
    [JsonPropertyName("subject")]
    public string? Subject { get; init; }

    /// <summary>Gets the sources the event rests on, by the names the turn gave them.</summary>
    [JsonPropertyName("sources")]
    public IReadOnlyList<string>? Sources { get; init; }
}

/// <summary>One row of a comparison as a model wrote it.</summary>
internal sealed record DiscoveryRowDocument
{
    /// <summary>Gets the cells, which the reading holds to the column count before a table exists.</summary>
    [JsonPropertyName("cells")]
    public IReadOnlyList<DiscoveryCellDocument>? Cells { get; init; }
}

/// <summary>One cell of a comparison as a model wrote it.</summary>
internal sealed record DiscoveryCellDocument
{
    /// <summary>Gets the value as the correspondence wrote it, or nothing where the correspondence says nothing.</summary>
    [JsonPropertyName("value")]
    public string? Value { get; init; }

    /// <summary>Gets the sources the cell rests on, by the names the turn gave them.</summary>
    [JsonPropertyName("sources")]
    public IReadOnlyList<string>? Sources { get; init; }
}
