// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Application.EmailContent.Rendering.Document;

/// <summary>How a block places its content across the width it was given.</summary>
/// <remarks>
/// The one positional property the reduction admits, and it is admitted because it cannot resolve to a position outside
/// the parent: it distributes content within a width the parent already decided. Nothing here offsets, transforms, or
/// floats, which is what keeps message style inside the pane it is rendered in.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<MailBlockAlignment>))]
public enum MailBlockAlignment
{
    /// <summary>The message said nothing, so the pane's own reading direction decides.</summary>
    Inherited = 0,

    /// <summary>Against the start of the line, which is the left in a left-to-right message.</summary>
    Start = 1,

    /// <summary>Centred within the width.</summary>
    Center = 2,

    /// <summary>Against the end of the line, which is the right in a left-to-right message.</summary>
    End = 3,

    /// <summary>Spread to both edges.</summary>
    Justify = 4,
}
