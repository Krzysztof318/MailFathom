// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.AI.Search;

/// <summary>The filters half of that answer, equally untrusted.</summary>
/// <remarks>
/// The two days arrive as text rather than as <see cref="DateOnly" /> so a value no calendar holds is a field this
/// build drops rather than a document it fails to read at all: a model that wrote one impossible date has still read
/// the rest of the sentence usefully.
/// </remarks>
internal sealed record MailSearchPhraseFiltersDocument
{
    /// <summary>Gets the address the model read as the sender's.</summary>
    [JsonPropertyName("senderAddress")]
    public string? SenderAddress { get; init; }

    /// <summary>Gets the address the model read as a recipient's.</summary>
    [JsonPropertyName("recipientAddress")]
    public string? RecipientAddress { get; init; }

    /// <summary>Gets the first calendar day the model resolved the sentence's time expression to.</summary>
    [JsonPropertyName("receivedFrom")]
    public string? ReceivedFrom { get; init; }

    /// <summary>Gets the last calendar day the model resolved it to, inclusive.</summary>
    [JsonPropertyName("receivedTo")]
    public string? ReceivedTo { get; init; }

    /// <summary>Gets whether the model read the sentence as asking for unread mail.</summary>
    [JsonPropertyName("unread")]
    public bool? Unread { get; init; }

    /// <summary>Gets whether the model read it as asking for flagged mail.</summary>
    [JsonPropertyName("flagged")]
    public bool? Flagged { get; init; }

    /// <summary>Gets whether the model read it as asking for mail carrying files.</summary>
    [JsonPropertyName("hasAttachments")]
    public bool? HasAttachments { get; init; }
}
