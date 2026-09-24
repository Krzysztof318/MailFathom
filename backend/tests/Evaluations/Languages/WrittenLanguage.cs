// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers;
using System.Collections.Frozen;
using System.Text.RegularExpressions;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;

namespace MailFathom.Evaluations.Languages;

/// <summary>Tells which of the two languages MailFathom writes in a piece of model output is written in.</summary>
/// <remarks>
/// <para>
/// Only English and Polish are ever told apart, so a plain count settles it without a judge: Polish is marked by its
/// diacritics and by function words English never writes, English by function words Polish never writes. A word both
/// languages spell alike — <c>to</c>, <c>a</c>, <c>i</c>, <c>o</c>, <c>on</c>, <c>do</c> — counts for neither.
/// </para>
/// <para>
/// One language has to outweigh the other at least twice over, so a subject or a name quoted in its own language inside
/// a reading written in the other — which the instructions permit — does not change the verdict. Text too short to hold
/// two telling words is left undecided rather than guessed.
/// </para>
/// </remarks>
internal static partial class WrittenLanguage
{
    /// <summary>The name the check that output is written in the expected language is recorded under.</summary>
    public const string MetricName = "Written in the expected language";

    /// <summary>The fewest telling words the winning language needs before a verdict is given.</summary>
    private const int FewestTellingWords = 2;

    /// <summary>The fewest words output must have for being undecided to count against it.</summary>
    private const int FewestWordsToDecide = 8;

    private static readonly FrozenSet<string> PolishWords = FrozenSet.ToFrozenSet(
        ["w", "z", "na", "nie", "jest", "jak", "oraz", "dla", "od", "przez", "czy", "ale", "ten", "ta", "te", "tym", "tego", "jej", "jego", "ich", "za", "przy", "lub", "co", "dnia", "pani", "pana", "który", "która", "które", "zostanie", "został", "została", "prosi", "proszę", "termin", "wiadomość"],
        StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> EnglishWords = FrozenSet.ToFrozenSet(
        ["the", "and", "of", "in", "is", "are", "was", "were", "for", "with", "that", "this", "by", "from", "it", "be", "has", "have", "had", "will", "not", "an", "at", "as", "or", "but", "which", "who", "we", "you", "they", "their", "our", "your", "been", "would", "should", "can", "about", "until", "before", "after", "its", "into", "there", "what", "when", "must", "may"],
        StringComparer.OrdinalIgnoreCase);

    private static readonly SearchValues<char> PolishLetters = SearchValues.Create("ąćęłńóśźżĄĆĘŁŃÓŚŹŻ");

    /// <summary>Tells which language a text is written in.</summary>
    /// <param name="text">What a model wrote.</param>
    /// <returns>The language, or <see langword="null" /> where the text is too short or too evenly mixed to tell.</returns>
    /// <remarks>
    /// Read outside quotation marks first, because the instructions keep a quotation in the language it was written in
    /// and ask for the sentence around it in the reader's: a long quoted error message would otherwise outweigh the
    /// short sentence that makes the answer the reader's. The whole text decides only where what stands outside the
    /// quotations is too little to, so a quotation standing alone is still read as the language it is in.
    /// </remarks>
    public static MailAccountLanguage? Of(string? text) =>
        Decided(WordsOf(Quotation().Replace(text ?? string.Empty, " "))) ?? Decided(WordsOf(text));

    private static MailAccountLanguage? Decided(List<string> words)
    {
        var polish = words.Count(static word => PolishWords.Contains(word) || word.AsSpan().ContainsAny(PolishLetters));
        var english = words.Count(EnglishWords.Contains);

        return (polish, english) switch
        {
            _ when Math.Max(polish, english) < FewestTellingWords => null,
            _ when polish >= 2 * english => MailAccountLanguage.Polish,
            _ when english >= 2 * polish => MailAccountLanguage.English,
            _ => null,
        };
    }

    /// <summary>Names how a text misses the language it should be written in.</summary>
    /// <param name="text">What a model wrote.</param>
    /// <param name="expected">The language it should be written in.</param>
    /// <returns>What is wrong, or <see langword="null" /> where the text is in that language or too short to tell.</returns>
    public static string? Shortfall(string? text, MailAccountLanguage expected) => Of(text) switch
    {
        { } found when found == expected => null,
        { } found => $"what it wrote is in {found} rather than {expected}.",
        null when WordsOf(text).Count < FewestWordsToDecide => null,
        null => $"what it wrote reads as neither English nor Polish throughout, where {expected} was expected.",
    };

    /// <summary>Names how a text misses the language a person should have been written for.</summary>
    /// <param name="text">What a model wrote.</param>
    /// <param name="expected">The language the person reads.</param>
    /// <returns>What is wrong, or <see langword="null" /> where the text is in that language or too short to tell.</returns>
    /// <remarks>
    /// The measurement is about the text rather than about whose language was asked for, so the two enumerations meet
    /// here and nowhere else: a mailbox's language and a person's name the same two languages and answer different
    /// questions, and this is the one place an evaluation holds a written text against either of them.
    /// </remarks>
    public static string? Shortfall(string? text, UserLanguage expected) => Shortfall(
        text,
        expected switch
        {
            UserLanguage.Polish => MailAccountLanguage.Polish,
            _ => MailAccountLanguage.English,
        });

    private static List<string> WordsOf(string? text) =>
        [.. Word().Matches(text ?? string.Empty).Select(static match => match.Value)];

    [GeneratedRegex(@"\p{L}+")]
    private static partial Regex Word();

    [GeneratedRegex("[\"“„«][^\"“”„«»]*[\"”»]")]
    private static partial Regex Quotation();
}
