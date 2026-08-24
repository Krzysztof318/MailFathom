// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Immutable;
using Microsoft.Extensions.Localization;

namespace MailFathom.Client.Presentation;

/// <summary>One of the themes the application can be put into, named in the language it is being read in.</summary>
/// <remarks>
/// The enum is what the theme service is told and what a persisted choice is written as; this is what a reader sees.
/// The two are separate because an enum member's name is an identifier rather than a word — a picker bound straight to
/// <see cref="AppTheme"/> shows <c>System</c> to somebody reading Polish — and because the string has to come from the
/// same tables under <c>Strings/</c> as every other visible word. A <c>x:Uid</c> cannot reach it: the words are per
/// item rather than per control, so this is one of the few places a string is resolved in code, which is what
/// <see cref="IStringLocalizer"/> is for.
/// </remarks>
/// <param name="Theme">The theme this offers, which is what the theme service is handed.</param>
/// <param name="Name">What the offer reads as, in the language the application is being read in.</param>
public sealed record AppThemeOption(AppTheme Theme, string Name)
{
    /// <summary>The themes a reader is offered, in the order they are offered in.</summary>
    internal static readonly IImmutableList<AppTheme> Offered =
        [AppTheme.System, AppTheme.Light, AppTheme.Dark];

    /// <summary>Names a theme in the language the application is being read in.</summary>
    /// <param name="theme">The theme to offer.</param>
    /// <param name="localizer">The string table the name is resolved against.</param>
    /// <returns>The offer a picker shows.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="localizer" /> is <see langword="null" />.</exception>
    public static AppThemeOption Named(AppTheme theme, IStringLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        return new AppThemeOption(theme, localizer[ResourceKeyFor(theme)]);
    }

    /// <summary>The key a theme's name is held under in every table under <c>Strings/</c>.</summary>
    /// <param name="theme">The theme to name.</param>
    /// <returns>The resource key.</returns>
    internal static string ResourceKeyFor(AppTheme theme) => $"AppTheme.{theme}";
}
