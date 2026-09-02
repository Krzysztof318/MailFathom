// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Application.EmailContent.Rendering.Document;

/// <summary>What a message asked for about the appearance of one run of its text.</summary>
/// <remarks>
/// A flags enumeration because the five genuinely compose — mail routinely writes a bold italic link — and a run
/// carrying them as one value is what keeps the reduction from splitting a sentence into a run per property. The set is
/// deliberately about the glyphs rather than about where they sit: nothing here moves a run, sizes it against a
/// viewport, or gives it a box of its own.
/// </remarks>
[Flags]
[JsonConverter(typeof(JsonStringEnumConverter<MailTextEmphasis>))]
public enum MailTextEmphasis
{
    /// <summary>The run is drawn as the pane's ordinary body text.</summary>
    None = 0,

    /// <summary>The run is drawn heavier than the body text around it.</summary>
    Bold = 1,

    /// <summary>The run is drawn slanted.</summary>
    Italic = 2,

    /// <summary>The run is drawn with a line under it.</summary>
    Underline = 4,

    /// <summary>The run is drawn with a line through it.</summary>
    Strikethrough = 8,

    /// <summary>The run is drawn in the pane's fixed-width face, which is what code and terminal output need.</summary>
    Monospace = 16,
}
