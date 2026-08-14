// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.SyntheticMail.Generation.SensitiveDecoys;

/// <summary>One kind of fabricated sensitive material, and the sentence a message says it in.</summary>
/// <param name="Scanner">Which of the two scanners looks for it, named as the deployment configures it.</param>
/// <param name="Category">The category it is expected to be reported under, named as the deployment configures it.</param>
/// <param name="Rule">The corpus rule or analyzer entity expected to match, which is what a suppression is written from.</param>
/// <param name="Sentence">The line the value is planted in, with <see cref="ValuePlaceholder" /> where the value goes.</param>
/// <param name="Fabricate">Builds one value of this kind from the corpus seed.</param>
/// <remarks>
/// <para>
/// <b>The sentence is part of the decoy rather than decoration around it.</b> A personal-data recogniser scores a bare
/// identifier below what a deployment's confidence floor accepts and raises that score when the words it expects stand
/// near the value — a payment card scores 0.3 on its own and clears 0.4 only beside the word <c>card</c>. A value
/// planted in an arbitrary paragraph would therefore be found or not found depending on which nouns the vocabulary
/// happened to draw around it, which is a corpus nobody could conclude anything from. So each sentence carries its
/// recogniser's context words, and changing one of them is changing what the decoy tests.
/// </para>
/// <para>
/// The scanner, the category, and the rule are spelled the way the deployment spells them, and are carried as text
/// because this project deliberately references nothing under <c>src/</c>. That is a copy, and the tests assert its
/// shape rather than its truth: what would go stale is a name, which the corpus listing prints and a reader compares
/// against what the scanner reported.
/// </para>
/// </remarks>
internal sealed record SensitiveDecoyKind(
    string Scanner,
    string Category,
    string Rule,
    string Sentence,
    Func<Random, string> Fabricate)
{
    /// <summary>Where the fabricated value goes in the sentence.</summary>
    internal const string ValuePlaceholder = "{value}";

    /// <summary>How the corpus listing names this kind.</summary>
    internal string Label => $"{this.Scanner}:{this.Category}";

    /// <summary>Fabricates one value of this kind and writes the sentence carrying it.</summary>
    /// <param name="source">What the draw comes from.</param>
    /// <param name="placement">Where in the sentence the value goes, which decides what follows it.</param>
    /// <returns>The planted decoy.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="placement" /> is not one of the declared placements.</exception>
    internal SensitiveDecoy Plant(Random source, SensitiveDecoyPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new SensitiveDecoy(this, placement, this.Write(this.Fabricate(source), placement));
    }

    /// <summary>Writes the sentence with the value placed the way this planting asks for.</summary>
    /// <remarks>
    /// The three placements that are not the sentence's own are derived from it rather than written a second time per
    /// kind. Every sentence here states its recogniser's context words before the placeholder, so cutting the sentence
    /// at the placeholder keeps everything a recogniser scores on and drops only the words that followed the value.
    /// Writing a second sentence per kind and per placement would be forty-eight sentences to keep true instead of
    /// twelve, and the words after the value are the ones that carry nothing.
    /// </remarks>
    private string Write(string value, SensitiveDecoyPlacement placement) => placement switch
    {
        SensitiveDecoyPlacement.MidSentence =>
            this.Sentence.Replace(ValuePlaceholder, value, StringComparison.Ordinal),
        SensitiveDecoyPlacement.ClosingTheSentence =>
            string.Concat(this.Opening(), value, "."),
        SensitiveDecoyPlacement.InBrackets =>
            this.Sentence.Replace(ValuePlaceholder, $"({value})", StringComparison.Ordinal),
        SensitiveDecoyPlacement.InATableCell =>
            string.Concat(this.Opening().TrimEnd(), "\n|", value, "|"),
        _ => throw new ArgumentOutOfRangeException(nameof(placement), placement, "That is not a declared placement."),
    };

    /// <summary>The part of the sentence that stands before the value.</summary>
    private string Opening() => this.Sentence[..this.Sentence.IndexOf(ValuePlaceholder, StringComparison.Ordinal)];
}
