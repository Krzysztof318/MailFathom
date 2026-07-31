// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System.Text;

namespace MailFathom.Infrastructure.Mail.Mime;

/// <summary>Turns the text a message body carries into text every consumer of it can hold.</summary>
/// <remarks>
/// It is shared by the two readings a body gets — the text the lexical index covers and the text a reader is shown —
/// because both are consumed by PostgreSQL and by a JSON writer, and a character one of them cannot carry is a
/// character neither may keep. Normalizing in one place is also what keeps a message from being indexed under one
/// spelling and displayed under another.
/// </remarks>
internal static class MailBodyTextNormalizer
{
    /// <summary>Appends what an index and a reader can both carry, and reports whether room is left for more.</summary>
    /// <param name="body">The text being accumulated.</param>
    /// <param name="source">The text one body part carried.</param>
    /// <param name="maxCharacters">The greatest number of characters the accumulated text may hold.</param>
    /// <returns><see langword="true" /> while room is left for more; otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// Line endings are unified so the trimming rules see one line structure whichever platform wrote the message.
    /// Control characters other than the line break and the tab are removed rather than kept: no body displays them,
    /// PostgreSQL rejects a null byte in a text value outright, and a message could otherwise place one in the middle
    /// of a word and make it unmatchable by any query. Normalizing during the append is what keeps the bound a bound on
    /// work rather than only on the result.
    /// </remarks>
    public static bool Append(StringBuilder body, string source, int maxCharacters)
    {
        var previousWasCarriageReturn = false;

        foreach (var character in source)
        {
            if (body.Length >= maxCharacters)
            {
                return false;
            }

            switch (character)
            {
                case '\r':
                    body.Append('\n');
                    break;

                case '\n' when previousWasCarriageReturn:
                    break;

                case '\n':
                case '\t':
                    body.Append(character);
                    break;

                default:
                    if (!char.IsControl(character))
                    {
                        body.Append(character);
                    }

                    break;
            }

            previousWasCarriageReturn = character == '\r';
        }

        return body.Length < maxCharacters;
    }
}
