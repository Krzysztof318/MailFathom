// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.AI.BodyCleanup;

/// <summary>The shape a body-cleanup agent answers in, before anything of it is believed.</summary>
/// <remarks>
/// It stays inside this boundary. What a model wrote is read into this and then checked against the document it is about,
/// which is <c>MailBodyCleaningSegments</c>'s work and not this type's.
/// </remarks>
internal sealed record MailBodyCleanupDocument
{
    /// <summary>Gets the ranges the model wrote, which have to partition the outline to be used at all.</summary>
    [JsonPropertyName("segments")]
    public IReadOnlyList<MailBodyCleanupSegmentDocument?>? Segments { get; init; }
}

/// <summary>One range as the model wrote it, with nothing about it yet established.</summary>
/// <remarks>
/// <para>
/// The numbers are the positions the turn published rather than identifiers, which is why they are integers here: a
/// number outside the range the turn named describes no block, and it takes the whole answer with it because the ranges
/// are a partition rather than a list of independent claims.
/// </para>
/// <para>
/// <b>There is nowhere here to put message text, and that is load-bearing.</b> The three members are two numbers and a
/// keyword, so the rendering this produces cannot carry a word a model wrote — and the reading of this document is strict
/// about members nobody declared, so an answer that put a block's text beside its range is refused rather than quietly
/// read for the part that fitted.
/// </para>
/// </remarks>
internal sealed record MailBodyCleanupSegmentDocument
{
    /// <summary>Gets the first block number the range covers.</summary>
    [JsonPropertyName("from")]
    public int? From { get; init; }

    /// <summary>Gets the last block number the range covers.</summary>
    [JsonPropertyName("to")]
    public int? To { get; init; }

    /// <summary>Gets what the model said happens to the range, which is the keep keyword or the drop one.</summary>
    [JsonPropertyName("action")]
    public string? Action { get; init; }
}
