// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Application.Emails.Enrichment;

/// <summary>Names which of the three readings of a message one mark carries.</summary>
/// <remarks>
/// Three and no fourth, because each answers one of the questions triage asks of a row: what this is about, why it
/// might matter, and whether the message contains a commitment somebody made or expects. A message carries at most one
/// mark of each aspect, which is what lets a row draw them without choosing between two answers to one question.
/// <para>
/// It reaches a client by name rather than by number, because a renderer keyed by an ordinal would be keyed by a value
/// this file may never reorder for exactly that reason.
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<EmailEnrichmentAspect>))]
public enum EmailEnrichmentAspect
{
    /// <summary>What the message is about, in one sentence.</summary>
    Sense = 0,

    /// <summary>Why the message may matter to the person whose mailbox it arrived in.</summary>
    Significance = 1,

    /// <summary>A commitment the message contains, which is the one aspect that may carry a date.</summary>
    Commitment = 2,
}
