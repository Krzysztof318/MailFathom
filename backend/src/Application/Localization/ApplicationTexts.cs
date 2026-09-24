// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Resources;
using MailFathom.Application.Access;
using MailFathom.Domain.Access;

namespace MailFathom.Application.Localization;

/// <summary>Reads a sentence the service writes for a person, in the language that person chose.</summary>
/// <remarks>
/// One resource file per language beside this type holds every such sentence the application has, whichever boundary
/// writes it: <c>ApplicationTexts.resx</c> is English and the neutral set, and <c>ApplicationTexts.&lt;language&gt;.resx</c>
/// is each other language. Adding a language is a file and a <see cref="UserLanguage" /> member, never an edit here
/// beyond naming its culture.
/// </remarks>
public static class ApplicationTexts
{
    private static readonly ResourceManager Resources = new(typeof(ApplicationTexts));

    /// <summary>Reads one sentence in one language.</summary>
    /// <param name="text">Which sentence.</param>
    /// <param name="language">The language the person reading it chose.</param>
    /// <returns>The sentence as that language states it.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no resource file states the sentence, which is a build defect rather than an input.</exception>
    public static string GetText(ApplicationText text, UserLanguage language) =>
        Resources.GetString(text.ToString(), CultureOf(language))
        ?? throw new InvalidOperationException($"No resource states the application text {text}.");

    /// <summary>Reads one sentence in the language one person chose.</summary>
    /// <param name="languages">Resolves which language that is.</param>
    /// <param name="text">Which sentence.</param>
    /// <param name="user">The person who will read it.</param>
    /// <returns>The sentence as that person's language states it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="languages" /> is <see langword="null" />.</exception>
    public static string GetText(this IUserLanguages languages, ApplicationText text, UserId user)
    {
        ArgumentNullException.ThrowIfNull(languages);

        return GetText(text, languages.LanguageOf(user));
    }

    /// <summary>Names the culture a language's resource file is stored under.</summary>
    /// <param name="language">The language.</param>
    /// <returns>The culture whose resources state that language.</returns>
    public static CultureInfo CultureOf(UserLanguage language) =>
        language switch
        {
            UserLanguage.English => CultureInfo.GetCultureInfo("en"),
            UserLanguage.Polish => CultureInfo.GetCultureInfo("pl"),
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, "The language is not one a person may choose."),
        };
}
