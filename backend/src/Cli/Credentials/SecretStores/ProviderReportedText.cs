// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Text;

namespace MailFathom.Cli.Credentials.SecretStores;

/// <summary>Reduces text a secret-service provider reported to a form that is safe to put in front of an operator.</summary>
/// <remarks>
/// <para>
/// The message is the one useful thing a refusal carries — it separates a locked collection from a provider that is not
/// running — and it is also text from a process this command does not own. Whichever process claimed the well-known
/// name on the session bus answers, and that need not be the desktop's keyring: nothing bounds the message's length,
/// its character set, or whether it contains the escape sequences a terminal acts on rather than prints.
/// </para>
/// <para>
/// It reaches a terminal by three routes — the failure a command exits with, the sentence <c>login</c> prints about
/// where the credential ended up, and the warning <c>logout</c> prints about what it could not clear — so it is reduced
/// here, once, where the message is built. Reducing rather than refusing keeps the diagnostic: a provider's own wording
/// is what tells an operator which thing to unlock, so what is removed is the ability to move the cursor, break the
/// line, or bury the rest of the output under bulk.
/// </para>
/// </remarks>
internal static class ProviderReportedText
{
    /// <summary>The longest message kept, past which it is truncated.</summary>
    /// <remarks>Long enough for a sentence naming a collection and a reason, short enough that the failure it is embedded in stays one thing an operator reads rather than a screen they scroll.</remarks>
    private const int MaximumLength = 200;

    /// <summary>Reduces a provider-supplied message to printable, single-line text of bounded length.</summary>
    /// <param name="reported">What the provider said, which may be anything.</param>
    /// <returns>The reduced message, or <see langword="null" /> when nothing usable remained.</returns>
    internal static string? Sanitize(string? reported)
    {
        if (reported is null)
        {
            return null;
        }

        var kept = new StringBuilder(Math.Min(reported.Length, MaximumLength));

        foreach (var character in reported)
        {
            // Tested before anything is appended rather than after, so that the collapsing space below is bounded by
            // the same ceiling as the text is. Testing it afterwards leaves a message whose 200th character arrives as
            // that space one character over the bound and never equal to it again, which is unbounded rather than long.
            if (kept.Length >= MaximumLength)
            {
                break;
            }

            // A format character is neither control nor whitespace and is the one that rewrites what the operator
            // reads rather than merely adding to it: a right-to-left override or a directional isolate reorders the
            // sentence this message is embedded in, so it is dropped outright rather than collapsed to a space.
            if (char.GetUnicodeCategory(character) == UnicodeCategory.Format)
            {
                continue;
            }

            // A control character is what carries an escape sequence and what breaks one line into two. The line
            // and paragraph separators break it without being control characters, and char.IsWhiteSpace covers
            // those as well as the ordinary blanks a run of which is collapsed below.
            if (char.IsControl(character) || char.IsWhiteSpace(character))
            {
                // One space for any run of them, and none at the front, so bulk whitespace cannot pad the message out
                // to its bound and push the rest of the sentence past it.
                if (kept.Length > 0 && kept[^1] != ' ')
                {
                    kept.Append(' ');
                }

                continue;
            }

            kept.Append(character);
        }

        var reduced = kept.ToString().TrimEnd();

        return reduced.Length == 0 ? null : reduced;
    }
}
