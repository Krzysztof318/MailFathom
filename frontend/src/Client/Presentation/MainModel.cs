// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Immutable;
using Microsoft.Extensions.Localization;

namespace MailFathom.Client.Presentation;

/// <summary>
/// The model behind <see cref="MainPage"/>. The application is empty of features, so what it has to say is what it is
/// — the product name and the version this build was stamped with — and the two things a reader can already decide
/// about it: which language it is read in, and which theme it is shown in.
/// </summary>
public partial record MainModel
{
    private readonly ILocalizationService localization;
    private readonly IThemeService themes;
    private readonly IImmutableList<AppLanguage> languages;
    private readonly IImmutableList<AppThemeOption> themeOptions;

    /// <summary>Initializes the model over the two services a reader's choices are held by.</summary>
    /// <param name="localization">Which languages the application is readable in, and which one it is being read in.</param>
    /// <param name="themes">The theme service, which holds the choice and persists it across restarts.</param>
    /// <param name="localizer">Where a string resolved here rather than by a <c>x:Uid</c> in the view comes from.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public MainModel(ILocalizationService localization, IThemeService themes, IStringLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localization);
        ArgumentNullException.ThrowIfNull(themes);
        ArgumentNullException.ThrowIfNull(localizer);

        this.localization = localization;
        this.themes = themes;
        this.languages = [.. localization.SupportedCultures.Select(AppLanguage.FromCulture)];
        this.themeOptions = [.. AppThemeOption.Offered.Select(theme => AppThemeOption.Named(theme, localizer))];
    }

    /// <summary>What this build of the client reports about itself.</summary>
    public IFeed<ClientBuild> Build => Feed.Async(_ => ValueTask.FromResult(ClientBuild.Current));

    /// <summary>
    /// The languages this application can be read in, in the order the configuration names them, carrying which of
    /// them is chosen.
    /// </summary>
    /// <remarks>
    /// The selection travels with the list rather than through a second binding on the control. MVUX's
    /// <c>Selection</c> operator keeps the list and <see cref="ChosenLanguage"/> in step both ways — the state's value
    /// arrives as the picker's selection, and what somebody picks arrives back in the state — which is what a
    /// <c>SelectedItem</c> binding onto a state cannot do: a state of a record is exposed to a binding as a nested
    /// bindable rather than as the value, so the write is dropped and the picker opens on nothing.
    /// </remarks>
    public IListFeed<AppLanguage> Languages =>
        ListFeed.Async(_ => ValueTask.FromResult(this.languages)).Selection(this.ChosenLanguage);

    /// <summary>The language currently chosen, which starts as the one the application is being read in.</summary>
    public IState<AppLanguage> ChosenLanguage => State.Value(this, () =>
        AppLanguage.FromCulture(this.localization.CurrentCulture));

    /// <summary>
    /// Whether a chosen language is waiting for a restart to be read in. It is what the screen says the choice plainly
    /// with, rather than leaving somebody to wonder why the words did not change.
    /// </summary>
    public IState<bool> LanguageAwaitsRestart => State.Value(this, () => false);

    /// <summary>
    /// The three themes the application can be put into, in the order a reader is offered them, carrying which of them
    /// is chosen.
    /// </summary>
    /// <remarks>
    /// <see cref="AppTheme.System"/> is one of the three rather than a mode beside them: following the operating
    /// system is a value of the same enum, so a reader who never chooses is already in it. The selection travels with
    /// the list for the reason <see cref="Languages"/> gives.
    /// </remarks>
    public IListFeed<AppThemeOption> ThemeOptions =>
        ListFeed.Async(_ => ValueTask.FromResult(this.themeOptions)).Selection(this.ChosenTheme);

    /// <summary>The theme the application is in, and the way it is changed.</summary>
    /// <remarks>
    /// The state starts at whatever the service read back — the persisted choice, or <see cref="AppTheme.System"/>
    /// when nothing was ever chosen — and every write is handed straight to the service, which applies it to the
    /// visual tree and saves it. That is what makes picking one in the view the whole of the interaction, and it is
    /// why a theme needs no restart while a language does.
    /// </remarks>
    public IState<AppThemeOption> ChosenTheme => State
        .Value(this, () => this.OptionFor(this.themes.Theme))
        .ForEach(async (option, _) =>
        {
            if (option is not null)
            {
                await this.themes.SetThemeAsync(option.Theme).ConfigureAwait(false);
            }
        });

    /// <summary>
    /// Takes the chosen language, and says that it arrives on the next launch.
    /// </summary>
    /// <remarks>
    /// Applying a culture is what Uno does while a head is starting: <see cref="ILocalizationService.SetCurrentCultureAsync"/>
    /// writes the choice and overrides the application's primary language, and the visual tree already built keeps the
    /// words it was built with. So this states the consequence rather than hiding it — the screen reads the state below
    /// and says so.
    /// </remarks>
    /// <param name="ct">Cancels the act.</param>
    /// <returns>A task that completes once the choice is written.</returns>
    public async ValueTask ApplyLanguage(CancellationToken ct)
    {
        var chosen = await this.ChosenLanguage;
        if (chosen is null)
        {
            return;
        }

        // Only ever a culture the configuration offered. The chosen one came from that list, so this finds it — and a
        // value that somehow did not is dropped rather than handed to the localization service, which would otherwise
        // write a language with no string table behind it and leave the next launch empty.
        var culture = Array.Find(this.localization.SupportedCultures, supported => supported.Name == chosen.Tag);
        if (culture is null)
        {
            return;
        }

        await this.localization.SetCurrentCultureAsync(culture).ConfigureAwait(false);
        await this.LanguageAwaitsRestart.SetAsync(true, ct).ConfigureAwait(false);
    }

    private AppThemeOption OptionFor(AppTheme theme) =>
        this.themeOptions.First(option => option.Theme == theme);
}
