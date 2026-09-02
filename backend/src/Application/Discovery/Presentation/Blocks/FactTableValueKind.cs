// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Application.Discovery.Presentation.Blocks;

/// <summary>What kind of value a fact table's column holds, which is how a client draws the column.</summary>
/// <remarks>
/// A cell is always text, because the correspondence wrote it as text and a table that reformatted "roughly £40k" into
/// a number would be asserting a precision nobody wrote. What this says is how to present that text: which edge to
/// align it to, and whether ordering it means anything.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<FactTableValueKind>))]
public enum FactTableValueKind
{
    /// <summary>Words, aligned as the reader's language reads and ordered only as the producer gave them.</summary>
    Text = 0,

    /// <summary>A counted quantity, aligned to the end of the column.</summary>
    Number = 1,

    /// <summary>A money amount as the correspondence wrote it, currency included, aligned to the end of the column.</summary>
    Amount = 2,

    /// <summary>A date as the correspondence wrote it, which the client presents in the reader's own format where it can.</summary>
    Date = 3,
}
