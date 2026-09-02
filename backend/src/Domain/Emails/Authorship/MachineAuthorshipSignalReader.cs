// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.RegularExpressions;

namespace MailFathom.Domain.Emails.Authorship;

/// <summary>Reads out of a message's text what a machine-written message carries more often than a typed one.</summary>
/// <remarks>
/// <para>
/// It decides which signals a text carries and never what they are worth; the weighing is
/// <see cref="MachineAuthorshipProfile" />'s and is kept apart so a tuned weight is not a change to what is observed.
/// Everything here is a pure reading of characters — no allocation per character, no network, no model, and no state
/// carried between messages.
/// </para>
/// <para>
/// Two texts rather than one, because the two groups of signals ask different questions of a message.
/// <em>Concealment</em> is asked of the body as it was delivered, quoted history included, since a payload hidden
/// inside a quoted block is still hidden inside this message and is still what a reading agent would be handed.
/// <em>Prose</em> is asked of the trimmed text alone, because quoted history and a signature block are somebody else's
/// writing and would report their habits as this sender's.
/// </para>
/// <para>
/// The text is mail content and personal data. Nothing here logs, stores, or returns any part of it; what leaves is the
/// signal set and nothing else.
/// </para>
/// </remarks>
internal static partial class MachineAuthorshipSignalReader
{
    /// <summary>
    /// How many invisible characters a text carries before the reading counts them as construction rather than as
    /// noise.
    /// </summary>
    /// <remarks>
    /// A bound rather than a single occurrence, because a soft hyphen from a word processor and a zero-width space from
    /// a newsletter template both reach ordinary mail one at a time. A concealed instruction is a payload rather than a
    /// stray character, so it clears this comfortably.
    /// </remarks>
    private const int MinimumHiddenCharacters = 4;

    /// <summary>How many variation selectors have to stand in one run before the run is carrying data rather than styling a character.</summary>
    /// <remarks>
    /// One selector follows one base character and chooses how it is drawn, and an emoji sequence stacks a small
    /// handful. A run this long selects nothing: there is no base character with that many renderings.
    /// </remarks>
    private const int MinimumVariationSelectorRun = 8;

    /// <summary>How many stacked selectors a whole message may carry before the stacking says the same thing one long run does.</summary>
    /// <remarks>
    /// Counted over the message rather than over one run, because a payload split into runs below the bound above
    /// would otherwise carry as much as it liked. Only a selector standing on top of another one is counted, so the
    /// ordinary case costs nothing however much of it a message holds: an emoji sequence puts one selector after each
    /// base character, and a message of nothing but emoji adds not one to this total.
    /// </remarks>
    private const int MinimumStackedVariationSelectors = 8;

    /// <summary>How many characters a text needs before its prose is read at all.</summary>
    /// <remarks>
    /// A short message carries no evidence about how it was written — a two-line reply has no room for a habit — so
    /// reading one would report the shape of the sample rather than the shape of the writing. Below this, only the
    /// concealment signals are asked, since a single hidden payload is evidence at any length.
    /// </remarks>
    private const int MinimumProseLength = 400;

    /// <summary>How many unspaced em dashes a text carries before the habit counts as one.</summary>
    private const int MinimumUnspacedEmDashes = 2;

    /// <summary>How many typographic quotation marks a text carries before its uniformity says anything.</summary>
    private const int MinimumTypographicQuotationMarks = 3;

    /// <summary>How many labelled list items a text carries before the scaffolding counts as one.</summary>
    private const int MinimumScaffoldedListItems = 3;

    /// <summary>How many distinct fixed phrases a text uses before the framing counts as one.</summary>
    /// <remarks>
    /// Two, because every one of these phrases is something a person writes; it is using several of them in one
    /// message that is the habit rather than the phrase.
    /// </remarks>
    private const int MinimumFormulaicPhrases = 2;

    /// <summary>How many tag characters a flag sequence may spell its region with before the run stops reading as one.</summary>
    /// <remarks>
    /// A subdivision code is at most six characters, so this leaves room and still bounds one run. A run is forgiven
    /// when it closes with the cancel character within this many characters, and every other run — one that ran past
    /// the bound, one another flag base interrupted, one ordinary text interrupted, one the message ended on — is
    /// counted as what it is.
    /// </remarks>
    private const int MaximumFlagSequenceTagCharacters = 8;

    /// <summary>How many tag characters a whole message may spend on flag sequences before none of them is forgiven.</summary>
    /// <remarks>
    /// Bounding each run bounds one flag and nothing else: a payload chopped into runs that each close properly would
    /// otherwise be forgiven a run at a time, without limit. So the forgiveness is spent from one message-wide budget,
    /// and a message that exhausts it has every flag-borne tag character counted — the ones already forgiven included.
    /// Four subdivision flags' worth is far past what correspondence carries and far below what an instruction worth
    /// hiding needs, and over-reporting is the direction to fail in: the reading is informational, so a newsletter that
    /// somehow spends this reads as likely machine written, which costs its reader nothing, while a payload that reads
    /// as unlikely defeats the signal.
    /// </remarks>
    private const int MaximumForgivenFlagSequenceTagCharacters = 24;

    private const char EmDash = '—';

    /// <summary>The code points that render as nothing, that reorder what is rendered, or that encode text invisibly.</summary>
    /// <remarks>
    /// Named individually rather than matched by Unicode category, because the categories these fall into also hold
    /// characters with ordinary uses — the zero-width joiner and non-joiner are format characters and are deliberately
    /// absent from the set below, since both are how Indic and Arabic script and emoji sequences are written.
    /// </remarks>
    private const int SoftHyphen = 0x00AD;
    private const int MongolianVowelSeparator = 0x180E;
    private const int ZeroWidthSpace = 0x200B;
    private const int WordJoiner = 0x2060;
    private const int ZeroWidthNoBreakSpace = 0xFEFF;
    private const int FirstInvisibleOperator = 0x2061;
    private const int LastInvisibleOperator = 0x2064;
    private const int FirstEmbeddingControl = 0x202A;
    private const int LastEmbeddingControl = 0x202E;
    private const int FirstIsolateControl = 0x2066;
    private const int LastIsolateControl = 0x2069;
    private const int FirstVariationSelector = 0xFE00;
    private const int LastVariationSelector = 0xFE0F;
    private const int FirstVariationSelectorSupplement = 0xE0100;
    private const int LastVariationSelectorSupplement = 0xE01EF;
    private const int FirstTagCharacter = 0xE0000;
    private const int LastTagCharacter = 0xE007F;
    private const int TagCancel = 0xE007F;
    private const int RegionalFlagBase = 0x1F3F4;

    /// <summary>Reads the signals a message's text carries.</summary>
    /// <param name="deliveredText">The body as it was delivered, which the concealment signals are read from.</param>
    /// <param name="writtenText">The body with quoted history and signatures removed, which the prose signals are read from.</param>
    /// <returns>Everything the two texts carried, or <see cref="MachineAuthorshipSignals.None" /> when they carried nothing.</returns>
    public static MachineAuthorshipSignals Read(string? deliveredText, string? writtenText) =>
        ReadConcealment(deliveredText) | ReadProse(writtenText);

    /// <summary>Walks the delivered text once, counting every kind of character that renders as nothing.</summary>
    /// <remarks>
    /// One pass over runes rather than four passes over chars, because three of the four ranges live outside the basic
    /// plane and a per-<see cref="char" /> reading would meet each of them as two unrelated surrogates.
    /// </remarks>
    private static MachineAuthorshipSignals ReadConcealment(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return MachineAuthorshipSignals.None;
        }

        var hiddenCharacters = 0;
        var tagCharacters = 0;
        var longestVariationSelectorRun = 0;
        var currentVariationSelectorRun = 0;
        var stackedVariationSelectors = 0;
        var carriesBidirectionalControl = false;
        var carriesRightToLeftWriting = false;
        var withinFlagSequence = false;
        var unclosedFlagSequenceTagCharacters = 0;
        var forgivenFlagSequenceTagCharacters = 0;

        foreach (var rune in text.EnumerateRunes())
        {
            var value = rune.Value;

            if (IsVariationSelector(value))
            {
                currentVariationSelectorRun++;
                longestVariationSelectorRun = Math.Max(longestVariationSelectorRun, currentVariationSelectorRun);

                if (currentVariationSelectorRun > 1)
                {
                    stackedVariationSelectors++;
                }
            }
            else
            {
                currentVariationSelectorRun = 0;
            }

            if (value == RegionalFlagBase)
            {
                // A second flag base is what an interrupted run looks like, and a payload chunked into short runs is
                // what it is used for, so whatever the open run had collected is counted before the new one starts.
                tagCharacters += unclosedFlagSequenceTagCharacters;
                unclosedFlagSequenceTagCharacters = 0;
                withinFlagSequence = true;
                continue;
            }

            if (IsTagCharacter(value))
            {
                // A subdivision flag spells its region out in tag characters and closes with the cancel character, so a
                // run belonging to one is the single legitimate appearance of this block in text. Nothing is forgiven
                // until that run closes, because everything this exemption could otherwise hide is written as a run
                // that never does.
                if (withinFlagSequence)
                {
                    if (value == TagCancel)
                    {
                        forgivenFlagSequenceTagCharacters += unclosedFlagSequenceTagCharacters;
                        unclosedFlagSequenceTagCharacters = 0;
                        withinFlagSequence = false;

                        // The budget is spent for the rest of the message once it is exceeded, and what it already
                        // forgave is counted with it: a message that reached here is one where the runs are the
                        // payload rather than the flags.
                        if (forgivenFlagSequenceTagCharacters > MaximumForgivenFlagSequenceTagCharacters)
                        {
                            tagCharacters += forgivenFlagSequenceTagCharacters;
                            forgivenFlagSequenceTagCharacters = 0;
                        }

                        continue;
                    }

                    unclosedFlagSequenceTagCharacters++;

                    if (unclosedFlagSequenceTagCharacters <= MaximumFlagSequenceTagCharacters)
                    {
                        continue;
                    }

                    tagCharacters += unclosedFlagSequenceTagCharacters;
                    unclosedFlagSequenceTagCharacters = 0;
                    withinFlagSequence = false;
                    continue;
                }

                tagCharacters++;
                continue;
            }

            tagCharacters += unclosedFlagSequenceTagCharacters;
            unclosedFlagSequenceTagCharacters = 0;
            withinFlagSequence = false;

            if (IsHiddenCharacter(value))
            {
                hiddenCharacters++;
            }
            else if (IsBidirectionalControl(value))
            {
                carriesBidirectionalControl = true;
            }
            else if (IsRightToLeftLetter(value))
            {
                carriesRightToLeftWriting = true;
            }
        }

        // A run the message ended on never closed either, so it is counted like any other that did not.
        tagCharacters += unclosedFlagSequenceTagCharacters;

        var signals = MachineAuthorshipSignals.None;

        if (hiddenCharacters >= MinimumHiddenCharacters)
        {
            signals |= MachineAuthorshipSignals.HiddenCharacters;
        }

        if (tagCharacters > 0)
        {
            signals |= MachineAuthorshipSignals.TagCharacters;
        }

        if (longestVariationSelectorRun >= MinimumVariationSelectorRun
            || stackedVariationSelectors >= MinimumStackedVariationSelectors)
        {
            signals |= MachineAuthorshipSignals.VariationSelectorRun;
        }

        // Only where nothing in the message is written right to left. A message that genuinely mixes directions needs
        // these characters to render correctly, and reading them there would report the writer's language.
        if (carriesBidirectionalControl && !carriesRightToLeftWriting)
        {
            signals |= MachineAuthorshipSignals.BidirectionalOverrides;
        }

        return signals;
    }

    private static MachineAuthorshipSignals ReadProse(string? text)
    {
        if (text is null || text.Length < MinimumProseLength)
        {
            return MachineAuthorshipSignals.None;
        }

        var signals = MachineAuthorshipSignals.None;

        if (CountUnspacedEmDashes(text) >= MinimumUnspacedEmDashes)
        {
            signals |= MachineAuthorshipSignals.UnspacedEmDashes;
        }

        if (UsesOnlyTypographicQuotationMarks(text))
        {
            signals |= MachineAuthorshipSignals.UniformTypography;
        }

        if (ScaffoldedListItem().Count(text) >= MinimumScaffoldedListItems)
        {
            signals |= MachineAuthorshipSignals.UnsolicitedListScaffolding;
        }

        if (CountDistinctFormulaicPhrases(text) >= MinimumFormulaicPhrases)
        {
            signals |= MachineAuthorshipSignals.FormulaicFraming;
        }

        return signals;
    }

    /// <summary>Counts em dashes closed up against a word on both sides, which is the shape the habit takes.</summary>
    /// <remarks>
    /// A dash at either end of the text is not counted at all: it has only one neighbour, so nothing distinguishes the
    /// closed-up habit from the spaced one there.
    /// </remarks>
    private static int CountUnspacedEmDashes(string text)
    {
        var count = 0;

        for (var index = 1; index < text.Length - 1; index++)
        {
            if (text[index] == EmDash && !char.IsWhiteSpace(text[index - 1]) && !char.IsWhiteSpace(text[index + 1]))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Reads whether the text substitutes every quotation mark, which typing in a mail client does not.</summary>
    /// <remarks>
    /// Both halves are required. A text with no straight marks and no curly ones has no quotation marks at all and says
    /// nothing, and a text carrying both is exactly the mixture a person produces.
    /// </remarks>
    private static bool UsesOnlyTypographicQuotationMarks(string text)
    {
        var typographic = 0;

        foreach (var character in text)
        {
            if (character is '\'' or '"')
            {
                return false;
            }

            if (character is '‘' or '’' or '“' or '”')
            {
                typographic++;
            }
        }

        return typographic >= MinimumTypographicQuotationMarks;
    }

    /// <summary>Counts how many of the fixed phrases the text uses, each at most once however often it repeats.</summary>
    /// <remarks>
    /// Distinct rather than total, because a message quoting its own opening line twice has used one phrase and not
    /// two, and the threshold is about reaching for several of them rather than about repeating one.
    /// </remarks>
    private static int CountDistinctFormulaicPhrases(string text) =>
        FormulaicPhrase()
            .Matches(text)
            .Select(static match => match.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

    private static bool IsHiddenCharacter(int value) =>
        value is SoftHyphen or MongolianVowelSeparator or ZeroWidthSpace or WordJoiner or ZeroWidthNoBreakSpace
            or (>= FirstInvisibleOperator and <= LastInvisibleOperator);

    private static bool IsBidirectionalControl(int value) =>
        value is (>= FirstEmbeddingControl and <= LastEmbeddingControl)
            or (>= FirstIsolateControl and <= LastIsolateControl);

    private static bool IsTagCharacter(int value) => value is >= FirstTagCharacter and <= LastTagCharacter;

    private static bool IsVariationSelector(int value) =>
        value is (>= FirstVariationSelector and <= LastVariationSelector)
            or (>= FirstVariationSelectorSupplement and <= LastVariationSelectorSupplement);

    /// <summary>Reads whether a character is a letter of a script written right to left.</summary>
    /// <remarks>
    /// The blocks rather than a character database lookup, because the question is only whether the message contains
    /// any such writing at all. A block that also holds punctuation costs nothing here: punctuation belonging to a
    /// right-to-left script is exactly as good an answer as a letter of one.
    /// </remarks>
    private static bool IsRightToLeftLetter(int value) =>
        value is (>= 0x0590 and <= 0x08FF)
            or (>= 0xFB1D and <= 0xFDFF)
            or (>= 0xFE70 and <= 0xFEFF)
            or (>= 0x10800 and <= 0x10FFF)
            or (>= 0x1E800 and <= 0x1EFFF);

    /// <summary>Matches a bullet whose item opens with a short bolded or colon-terminated label.</summary>
    /// <remarks>
    /// Every quantifier is bounded, so the pattern cannot be made to backtrack expensively by a message that chose its
    /// own text. The label bound is what keeps an ordinary sentence containing a colon from matching.
    /// </remarks>
    [GeneratedRegex(
        @"^[ \t]*(?:[-*•‣]|\d{1,2}[.)])[ \t]+(?:\*\*[^*\r\n]{1,60}\*\*|[^:\r\n]{1,60}:)[ \t]+\S",
        RegexOptions.Multiline)]
    private static partial Regex ScaffoldedListItem();

    /// <summary>Matches the fixed phrases a text generator opens, closes, and hedges with.</summary>
    /// <remarks>
    /// A closed set of whole phrases and deliberately not a vocabulary. Single words that were once a tell — one verb
    /// or one adverb — drift into ordinary use as people read more generated text, so matching them would report the
    /// writer's register rather than the message's construction. Both apostrophes are accepted because a message that
    /// substitutes its quotation marks substitutes these too.
    /// </remarks>
    [GeneratedRegex(
        @"i hope (?:this|the) (?:e-?mail|message|note) finds you well"
        + @"|(?:in conclusion|to summari[sz]e|that (?:being )?said)\s*,"
        + @"|let me know if you have any (?:questions|concerns)"
        + @"|feel free to reach out"
        + @"|it['’]?s (?:important|worth) (?:to note|noting) that"
        + @"|i['’]?d be (?:happy|glad) to (?:help|assist)"
        + @"|as an ai(?:\s|,)"
        + @"|delve into"
        + @"|navigat(?:e|ing) the complexit(?:y|ies)"
        + @"|in today['’]?s (?:fast[- ]paced|digital|ever[- ]changing) world",
        RegexOptions.IgnoreCase)]
    private static partial Regex FormulaicPhrase();
}
