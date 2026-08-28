// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Immutable;
using MailFathom.Client.Backend;
using MailFathom.Client.Session;
using Microsoft.Extensions.Localization;

namespace MailFathom.Client.Presentation.Settings;

/// <summary>
/// The model behind <see cref="SettingsPage"/>: the two things a reader can decide about the application itself —
/// which language it is read in, and which theme it is shown in — beside the build it is running, the deployment it is
/// pointed at, and the version that deployment is running, which are what somebody reporting a problem is asked for.
/// </summary>
public partial record SettingsModel
{
    private readonly ILocalizationService localization;
    private readonly IThemeService themes;
    private readonly DeploymentAddress address;
    private readonly OwnerSignIn signIn;
    private readonly IImmutableList<AppLanguage> languages;
    private readonly IImmutableList<AppThemeOption> themeOptions;

    /// <summary>Initializes the model over the services a reader's choices are held by, and over the deployment reached.</summary>
    /// <param name="localization">Which languages the application is readable in, and which one it is being read in.</param>
    /// <param name="themes">The theme service, which holds the choice and persists it across restarts.</param>
    /// <param name="address">Which deployment this client is pointed at, which is what the screen names.</param>
    /// <param name="session">What that deployment reported about itself, which is where its own version comes from.</param>
    /// <param name="signIn">Who is signed in on that deployment, and how somebody stops being them.</param>
    /// <param name="localizer">Where a string resolved here rather than by a <c>x:Uid</c> in the view comes from.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public SettingsModel(
        ILocalizationService localization,
        IThemeService themes,
        DeploymentAddress address,
        IClientSession session,
        OwnerSignIn signIn,
        IStringLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localization);
        ArgumentNullException.ThrowIfNull(themes);
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(signIn);
        ArgumentNullException.ThrowIfNull(localizer);

        this.localization = localization;
        this.themes = themes;
        this.address = address;
        this.signIn = signIn;
        this.Session = session.Standing;
        this.languages = [.. localization.SupportedCultures.Select(AppLanguage.FromCulture)];
        this.themeOptions = [.. AppThemeOption.Offered.Select(theme => AppThemeOption.Named(theme, localizer))];
    }

    /// <summary>What this build of the client reports about itself.</summary>
    public IFeed<ClientBuild> Build => Feed.Async(_ => ValueTask.FromResult(ClientBuild.Current));

    /// <summary>What the deployment reported about itself, which is where the version beside this build comes from.</summary>
    /// <remarks>
    /// Two versions rather than one, because they are two things that move separately: a client is installed and a
    /// deployment is upgraded, and the first question anybody debugging asks is which of the pair is which. The
    /// application's own state rather than a second fetch — it is the same session the shell decides from, so the
    /// screen names the version that answered rather than one asked for again here.
    /// </remarks>
    public IFeed<SessionStanding> Session { get; }

    /// <summary>The deployment this client is pointed at, as it would be typed.</summary>
    /// <remarks>
    /// A person running a client against more than one deployment has to be able to tell which one they are looking at
    /// without signing in to find out, and it is what makes the way to change it findable rather than remembered. It is
    /// read once, because pointing the client elsewhere leaves this screen and comes back to a new one.
    /// </remarks>
    public IFeed<string> Deployment =>
        Feed.Async(_ => ValueTask.FromResult(this.address.Current?.AbsoluteUri ?? string.Empty));

    /// <summary>The username this client is signed in under on that deployment.</summary>
    /// <remarks>
    /// The username and nothing else about the credential, which is the only half of it anything outside
    /// <c>Client.Backend</c> is ever given. It is read once, for the reason the address is: signing out leaves this
    /// screen and does not come back to it.
    /// </remarks>
    public IFeed<string> SignedInAs =>
        Feed.Async(_ => ValueTask.FromResult(this.signIn.Username ?? string.Empty));

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

    /// <summary>Ends the session and clears whatever this machine kept of it.</summary>
    /// <param name="ct">Abandons clearing what was kept, which does not un-end the session.</param>
    /// <returns>A task completing once nothing of the session is held.</returns>
    /// <remarks>
    /// Nothing here navigates. A session that has ended is answered in one place — <see cref="ShellModel" /> — because
    /// this is not the only way one ends: a deployment that stops accepting a credential ends one too, from whichever
    /// screen happened to be open, and two screens each navigating for themselves would be the same rule written
    /// twice.
    /// </remarks>
    public ValueTask SignOut(CancellationToken ct) => this.signIn.SignOutAsync(ct);

    private AppThemeOption OptionFor(AppTheme theme) =>
        this.themeOptions.First(option => option.Theme == theme);
}
