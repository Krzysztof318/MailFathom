// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Emails.Authorship;

/// <summary>What a message's text carried that a machine-written message carries more often than a typed one.</summary>
/// <remarks>
/// <para>
/// A set rather than a list of separate answers, because the members genuinely compose: a message carries any
/// combination of them at once, they are recorded together, and they are read together to reach one likelihood. It is
/// stored as the set it is, so a likelihood can be read back as the reasons that produced it rather than as a number
/// whose derivation is gone.
/// </para>
/// <para>
/// The members fall into two groups that are weighed very differently, and the split is deliberate rather than
/// presentational. The concealment members say the message's bytes carry characters no mail client renders, which a
/// person typing does not produce and a program assembling text does; they are close to unambiguous and are the reason
/// this assessment exists at all, because that is the channel by which instructions meant for a reading agent are
/// hidden from the reader the mailbox belongs to. The prose members say the text is shaped the way a text generator
/// shapes it, and every one of them is something a careful writer also reaches — none of them means anything alone.
/// </para>
/// <para>
/// A member that is present is a fact about the text. It is never a finding against the message or its sender, and
/// nothing in this system acts on one.
/// </para>
/// </remarks>
[Flags]
public enum MachineAuthorshipSignals
{
    /// <summary>The text carried none of the signals below, which is what an ordinary typed message carries.</summary>
    None = 0,

    /// <summary>The text carried characters that occupy no width and render as nothing.</summary>
    /// <remarks>
    /// Zero-width space, word joiner, zero-width no-break space, soft hyphen, the invisible mathematical operators, and
    /// the Mongolian vowel separator. Zero-width joiner and non-joiner are deliberately not among them: both are
    /// ordinary in Indic and Arabic script and inside emoji sequences, so counting them would report the writer's
    /// language rather than the message's construction.
    /// </remarks>
    HiddenCharacters = 1,

    /// <summary>The text carried characters from the Unicode tag block, which encode readable ASCII invisibly.</summary>
    /// <remarks>
    /// <c>U+E0000</c>–<c>U+E007F</c> mirror the printable ASCII range one for one and render as nothing at all, which makes a run
    /// of them a complete hidden message rather than a disturbance in a visible one. Nothing legitimate writes them
    /// into mail; the one live use in modern text is the subdivision flag emoji, and a run belonging to one is not
    /// counted.
    /// </remarks>
    TagCharacters = 2,

    /// <summary>The text carried direction overrides in a message with no right-to-left writing to justify them.</summary>
    /// <remarks>
    /// The overrides and isolates of <c>U+202A</c>–<c>U+202E</c> and <c>U+2066</c>–<c>U+2069</c> reorder what a reader sees away from what
    /// the bytes say, which is what conceals text in plain sight. They are ordinary in a message that genuinely mixes
    /// directions, so this is read only where the text contains no strong right-to-left character at all.
    /// </remarks>
    BidirectionalOverrides = 4,

    /// <summary>The text carried a long run of variation selectors, which carries data rather than styling one character.</summary>
    /// <remarks>
    /// A variation selector follows one base character and selects how it is drawn, so one or two of them in sequence
    /// is ordinary. A long run encodes bytes: the selector blocks are wide enough to carry arbitrary payload, and
    /// nothing renders any of it.
    /// </remarks>
    VariationSelectorRun = 8,

    /// <summary>The text used em dashes closed up against the words on both sides, repeatedly.</summary>
    /// <remarks>
    /// The most-cited typographic mark of generated prose, and one of the weakest on its own: a writer whose editor
    /// substitutes the character reaches it honestly, which is why a single occurrence is not enough and why it is
    /// weighed as one contribution among several.
    /// </remarks>
    UnspacedEmDashes = 16,

    /// <summary>The text used typographic quotation marks throughout and no straight ones anywhere.</summary>
    /// <remarks>
    /// A message typed in a mail client mixes the two, because substitution is applied to some keystrokes and not to
    /// pasted text, quoted code, or a URL. Text assembled in one pass is uniform, which is what this reads — and a
    /// writer using an editor that substitutes everything reaches the same uniformity, so it is weighed lightly.
    /// </remarks>
    UniformTypography = 32,

    /// <summary>The text repeated the labelled-bullet shape, where each item opens with a short bolded or colon-terminated term.</summary>
    UnsolicitedListScaffolding = 64,

    /// <summary>The text used more than one of the fixed phrases a text generator opens, closes, and hedges with.</summary>
    /// <remarks>
    /// Deliberately a small, closed set of whole phrases rather than a vocabulary: single words drift into ordinary use
    /// and would report the writer's register instead of the message's construction. Two are required, because one is
    /// something anybody writes.
    /// </remarks>
    FormulaicFraming = 128,
}
