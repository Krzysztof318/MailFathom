// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;

namespace MailFathom.SyntheticMail.Generation.SensitiveDecoys;

/// <summary>The character draws every fabricated value is assembled from.</summary>
/// <remarks>
/// One place rather than a private copy in each of the two fabricators, because both draw the same three things and a
/// second implementation of "sixteen characters out of this alphabet" is a second thing to get wrong. Every method
/// takes the caller's <see cref="Random" /> rather than making one, so the whole corpus stays derived from the plan's
/// single seed.
/// </remarks>
[SuppressMessage(
    "Security",
    "CA5394:Do not use insecure randomness",
    Justification = "Being reproducible from the corpus seed is the point. Nothing drawn here authenticates anything: it fills fabricated credentials and identifiers planted in invented mail so that a scanner has something to find.")]
internal static class RandomDraw
{
    private const string Digits = "0123456789";
    private const string LowercaseHexadecimal = "0123456789abcdef";

    /// <summary>Draws digits.</summary>
    /// <param name="source">What the draw comes from.</param>
    /// <param name="length">How many digits to draw.</param>
    /// <returns>Exactly <paramref name="length" /> characters, each between <c>0</c> and <c>9</c>.</returns>
    internal static string DecimalDigits(Random source, int length) => From(source, Digits, length);

    /// <summary>Draws lowercase hexadecimal digits, which is the form several token corpora spell a key in.</summary>
    /// <param name="source">What the draw comes from.</param>
    /// <param name="length">How many digits to draw.</param>
    /// <returns>Exactly <paramref name="length" /> characters, each between <c>0</c> and <c>f</c>.</returns>
    internal static string HexadecimalDigits(Random source, int length) => From(source, LowercaseHexadecimal, length);

    /// <summary>Draws characters out of one alphabet.</summary>
    /// <param name="source">What the draw comes from.</param>
    /// <param name="alphabet">The characters that may appear, which is the rule the value has to satisfy.</param>
    /// <param name="length">How many characters to draw.</param>
    /// <returns>Exactly <paramref name="length" /> characters out of <paramref name="alphabet" />.</returns>
    internal static string From(Random source, string alphabet, int length) =>
        string.Create(length, (source, alphabet), static (destination, state) =>
        {
            for (var index = 0; index < destination.Length; index++)
            {
                destination[index] = state.alphabet[state.source.Next(state.alphabet.Length)];
            }
        });

    /// <summary>Draws bytes, for the values that are encoded rather than written out.</summary>
    /// <param name="source">What the draw comes from.</param>
    /// <param name="length">How many bytes to draw.</param>
    /// <returns>Exactly <paramref name="length" /> bytes.</returns>
    internal static byte[] Bytes(Random source, int length)
    {
        var drawn = new byte[length];

        source.NextBytes(drawn);

        return drawn;
    }
}
