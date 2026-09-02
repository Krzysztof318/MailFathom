// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Mcp.Tools.Authorship;

/// <summary>Reports one thing an email's text carried, as the protocol spells it.</summary>
/// <remarks>
/// Published one member at a time rather than as a combined value, because a caller reads a list of names and a bit
/// field would make it read a number instead. The transport carries its own enumeration for the reason every other
/// published one does.
/// </remarks>
internal enum MachineAuthorshipSignal
{
    /// <summary>The text carried characters that occupy no width and render as nothing.</summary>
    HiddenCharacters = 0,

    /// <summary>The text carried Unicode tag characters, which encode readable ASCII invisibly.</summary>
    TagCharacters = 1,

    /// <summary>The text carried direction overrides with no right-to-left writing to justify them.</summary>
    BidirectionalOverrides = 2,

    /// <summary>The text carried a long run of variation selectors, which carries data rather than styling a character.</summary>
    VariationSelectorRun = 3,

    /// <summary>The text used em dashes closed up against the words on both sides, repeatedly.</summary>
    UnspacedEmDashes = 4,

    /// <summary>The text used typographic quotation marks throughout and no straight ones anywhere.</summary>
    UniformTypography = 5,

    /// <summary>The text repeated the labelled-bullet shape, where each item opens with a short bolded or colon-terminated term.</summary>
    ListScaffolding = 6,

    /// <summary>The text used more than one of the fixed phrases a text generator opens, closes, and hedges with.</summary>
    FormulaicFraming = 7,
}
