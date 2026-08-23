// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Immutable;

namespace MailFathom.Client.Presentation;

/// <summary>
/// The model behind <see cref="MainPage"/>. The application is empty of features, so what it has to say is what it
/// is — the product name and the version this build was stamped with — and which theme it is being shown in.
/// </summary>
public partial record MainModel
{
    private readonly IThemeService themes;

    /// <summary>Initializes the model over the service that puts the application into a theme.</summary>
    /// <param name="themes">The theme service, which holds the choice and persists it across restarts.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="themes" /> is <see langword="null" />.</exception>
    public MainModel(IThemeService themes)
    {
        ArgumentNullException.ThrowIfNull(themes);

        this.themes = themes;
    }

    /// <summary>What this build of the client reports about itself.</summary>
    public IFeed<ClientBuild> Build => Feed.Async(_ => ValueTask.FromResult(ClientBuild.Current));

    /// <summary>The three themes the application can be put into, in the order a reader is offered them.</summary>
    /// <remarks>
    /// <see cref="AppTheme.System"/> is one of the three rather than a mode beside them: following the operating
    /// system is a value of the same enum, so a reader who never chooses is already in it.
    /// </remarks>
    public IImmutableList<AppTheme> ThemeOptions { get; } =
        [AppTheme.System, AppTheme.Light, AppTheme.Dark];

    /// <summary>The theme the application is in, and the way it is changed.</summary>
    /// <remarks>
    /// The state starts at whatever the service read back — the persisted choice, or <see cref="AppTheme.System"/>
    /// when nothing was ever chosen — and every write is handed straight to the service, which applies it to the
    /// visual tree and saves it. That is what makes a two-way binding in the view the whole of the interaction.
    /// </remarks>
    public IState<AppTheme> Theme => State
        .Value(this, () => this.themes.Theme)
        .ForEach(async (theme, _) => await this.themes.SetThemeAsync(theme).ConfigureAwait(false));
}
