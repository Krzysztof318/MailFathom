// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;

namespace MailFathom.Client.Presentation;

/// <summary>
/// A language the client can be read in, as it is offered to somebody choosing one.
/// </summary>
/// <remarks>
/// <para>
/// It exists because a <see cref="CultureInfo"/> is not a value: it is mutable, it carries formatting, calendars, and
/// comparison rules a screen has no use for, and MVUX compares list items by equality. This carries the two things a
/// choice is made of — which language it is, and what it is called — and nothing else.
/// </para>
/// <para>
/// No key is declared beside the tag, because the tag is the identity: the name is derived from it and two instances
/// naming the same language are the same offer. That is the case <c>frontend/src/AGENTS.md</c> leaves to structural
/// equality rather than the one it asks for a key on.
/// </para>
/// </remarks>
/// <param name="Tag">The IETF language tag naming the culture, such as <c>en</c> or <c>pl</c>.</param>
/// <param name="Name">What speakers of that language call it, which is how somebody looking for it will look.</param>
public sealed record AppLanguage(string Tag, string Name)
{
    /// <summary>Describes a culture as the language offer a person chooses from.</summary>
    /// <param name="culture">The culture to describe.</param>
    /// <returns>That culture as a language this application offers.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="culture" /> is <see langword="null" />.</exception>
    public static AppLanguage FromCulture(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        return new AppLanguage(culture.Name, NameInItsOwnLanguage(culture));
    }

    /// <summary>
    /// Names a language the way its own speakers write it, which is what a person scanning a list of languages they do
    /// not yet read is looking for: somebody who wants Polish looks for <c>Polski</c> rather than for whatever the
    /// language currently on screen calls it.
    /// </summary>
    /// <remarks>
    /// Capitalization is the culture's own rule rather than this application's, because several languages, Polish
    /// among them, write their own name in lower case in running text and in title case where it stands alone as a
    /// label — which is what an entry in a list of languages is.
    /// </remarks>
    private static string NameInItsOwnLanguage(CultureInfo culture) =>
        culture.TextInfo.ToTitleCase(culture.NativeName);
}
