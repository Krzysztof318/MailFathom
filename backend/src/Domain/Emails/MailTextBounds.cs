// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;

namespace MailFathom.Domain.Emails;

/// <summary>Cuts mail-derived text to a length without leaving a string a consumer cannot carry.</summary>
/// <remarks>
/// Every value MailFathom derives from a message — a file name, a subject, a body — is bounded somewhere, because nothing
/// between the sender and local storage bounds it. Cutting at a fixed number of UTF-16 code units would split a
/// surrogate pair or a combining sequence, so text ending in an emoji could keep a lone high surrogate: a JSON writer
/// replaces or rejects that, and PostgreSQL rejects it outright. The cut therefore falls on a text-element boundary,
/// which also keeps a flag or skin-tone sequence whole.
/// </remarks>
public static class MailTextBounds
{
    /// <summary>Cuts text to at most a number of characters, never through the middle of one.</summary>
    /// <param name="text">The text to bound.</param>
    /// <param name="maxCharacters">The greatest number of UTF-16 characters the result may hold.</param>
    /// <returns>The text unchanged when it already fits; otherwise its longest prefix that ends on a text-element boundary.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="text" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxCharacters" /> is negative.</exception>
    /// <remarks>
    /// Text whose very first element is longer than the bound yields an empty string rather than a partial character.
    /// A caller for which nothing left is a meaningful outcome has to say what it means, which is why this reports it
    /// rather than substituting a replacement character.
    /// </remarks>
    public static string TruncateAtTextElementBoundary(string text, int maxCharacters)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegative(maxCharacters);

        if (text.Length <= maxCharacters)
        {
            return text;
        }

        var boundedLength = 0;
        var textElements = StringInfo.GetTextElementEnumerator(text);

        while (textElements.MoveNext())
        {
            var elementEnd = textElements.ElementIndex + ((string)textElements.Current).Length;
            if (elementEnd > maxCharacters)
            {
                break;
            }

            boundedLength = elementEnd;
        }

        return text[..boundedLength];
    }
}
