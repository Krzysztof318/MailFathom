// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Application.Discovery.Presentation;

/// <summary>Says whether a source is something somebody wrote or a description of something somebody pictured.</summary>
/// <remarks>
/// <para>
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0030-describing-an-image-attachment-in-words-and-ranking-a-depicted-match-below-a-written-one.md">ADR 0030</see>
/// makes a depicted source corroborating rather than a headline, and that ranking has to survive the journey from
/// retrieval to a screen. A description of a photographed invoice is this deployment's own reading of a picture, so a
/// fact resting on one alone rests on a machine's account of what an image shows; a fact resting on a sentence somebody
/// typed rests on what they said. Presenting the two alike is how an answer quietly promotes a guess to a quotation.
/// </para>
/// <para>
/// It is a property of the source rather than of the fact, which is why it sits on the citation. A block resting on
/// depicted sources alone is then something a reader — and <see cref="PresentationPlan.RestsOnDepictedSourcesOnly" /> —
/// works out from the citations the block names, rather than a second value a producer could set to disagree with them.
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<PresentationSourceMedium>))]
public enum PresentationSourceMedium
{
    /// <summary>Somebody wrote the source: a message body, a quoted passage, or a document's own text.</summary>
    Written = 0,

    /// <summary>The source is this deployment's description of a picture rather than words anybody wrote.</summary>
    Depicted = 1,
}
