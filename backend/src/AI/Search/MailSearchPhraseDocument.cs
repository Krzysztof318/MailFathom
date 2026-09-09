// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.AI.Search;

/// <summary>The shape a phrase-reading agent answers in, before anything of it is believed.</summary>
/// <remarks>
/// It stays inside this boundary and never becomes the reading itself. Every field is optional and every value is
/// untrusted: what a model wrote is read into this and then validated into
/// <see cref="Application.Emails.Search.Phrasing.MailSearchPhraseReading" />, which is the type the rest of the system
/// works with.
/// </remarks>
internal sealed record MailSearchPhraseDocument
{
    /// <summary>Gets the constraints the model read the sentence as stating.</summary>
    [JsonPropertyName("filters")]
    public MailSearchPhraseFiltersDocument? Filters { get; init; }

    /// <summary>Gets the phrases the model left to rank by, in the order it wrote them.</summary>
    [JsonPropertyName("criteria")]
    public IReadOnlyList<string?>? Criteria { get; init; }

    /// <summary>Gets the part of the sentence the model says it made nothing of.</summary>
    [JsonPropertyName("unaccounted")]
    public string? Unaccounted { get; init; }
}
