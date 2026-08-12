// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.SensitiveContent.Secrets;

/// <summary>Measures how much a run of characters looks like something random rather than something written.</summary>
/// <remarks>
/// This is the whole of the entropy heuristic's evidence. A credential with no format to recognise is still drawn from
/// a random alphabet, while the base64 fragment, the message identifier, and the tracking parameter it would otherwise
/// be confused with are frequently not — a fragment of encoded English repeats, and an identifier carries structure.
/// The measure is per character so that it does not vary with how long the run is.
/// </remarks>
internal static class ShannonEntropy
{
    /// <summary>Measures a run of characters in bits per character.</summary>
    /// <param name="text">The run to measure.</param>
    /// <returns>Zero for an empty or single-valued run, rising towards the alphabet's width as the run varies.</returns>
    public static double BitsPerCharacter(ReadOnlySpan<char> text)
    {
        if (text.Length == 0)
        {
            return 0;
        }

        var occurrences = new Dictionary<char, int>(text.Length);

        foreach (var character in text)
        {
            occurrences[character] = occurrences.GetValueOrDefault(character) + 1;
        }

        var bits = 0d;

        foreach (var count in occurrences.Values)
        {
            var share = (double)count / text.Length;
            bits -= share * Math.Log2(share);
        }

        return bits;
    }
}
