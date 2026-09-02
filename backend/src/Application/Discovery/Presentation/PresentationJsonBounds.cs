// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;

namespace MailFathom.Application.Discovery.Presentation;

/// <summary>Refuses an oversized JSON token before anything in this contract expands it into a string.</summary>
/// <remarks>
/// <para>
/// Every text a plan carries is bounded by the value type that holds it, and that bound is the contract. What this adds
/// is where the bound is applied: a plan arrives from a producer this deployment does not control, so a token of any
/// length would otherwise be decoded into a string first and refused afterwards. The cost of that is paid before the
/// rule is consulted, which is the shape the repository's own limit rule exists to prevent.
/// </para>
/// <para>
/// The ceiling is read off the token rather than off the string it decodes to, which makes it a ceiling rather than the
/// rule: a character can arrive as up to six octets, whether as the longest UTF-8 sequence a surrogate half is written
/// as or as the <c>\uXXXX</c> escape a producer is free to use instead. So this refuses what no legal value could reach
/// and leaves the exact count to the type, which is the only place that can apply it to characters.
/// </para>
/// </remarks>
internal static class PresentationJsonBounds
{
    private const int MaxOctetsPerCharacter = 6;

    /// <summary>Refuses a string token that could not hold the stated number of characters however it is encoded.</summary>
    /// <param name="reader">The reader, positioned on the string token.</param>
    /// <param name="maxCharacters">The greatest number of characters the value may hold.</param>
    /// <param name="what">What the value is, for the message a refusal carries.</param>
    /// <exception cref="JsonException">Thrown when the token is longer than any value of that length could be.</exception>
    internal static void EnsureCouldHoldAtMost(ref Utf8JsonReader reader, int maxCharacters, string what)
    {
        var octets = reader.HasValueSequence ? reader.ValueSequence.Length : reader.ValueSpan.Length;

        if (octets > (long)maxCharacters * MaxOctetsPerCharacter)
        {
            throw new JsonException($"A {what} holds at most {maxCharacters} characters.");
        }
    }
}
