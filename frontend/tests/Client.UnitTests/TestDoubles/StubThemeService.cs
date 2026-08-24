// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.UnitTests.TestDoubles;

/// <summary>
/// An <see cref="IThemeService"/> that remembers what it was asked to apply instead of touching a visual tree.
/// </summary>
/// <remarks>
/// The real service reaches <c>Window.Content</c> to set <c>RequestedTheme</c> and the platform's settings store to
/// persist the choice, neither of which exists in a unit-test host. What a model owes the service is the theme it
/// hands over, so this keeps the last one and announces every one through <see cref="ThemeChanged"/>.
/// </remarks>
internal sealed class StubThemeService : IThemeService
{
    /// <inheritdoc />
    public event EventHandler<AppTheme>? ThemeChanged;

    /// <inheritdoc />
    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <inheritdoc />
    public bool IsDark => this.Theme is AppTheme.Dark;

    /// <inheritdoc />
    public Task InitializeAsync() => Task.CompletedTask;

    /// <inheritdoc />
    public Task<bool> SetThemeAsync(AppTheme theme)
    {
        this.Theme = theme;
        ThemeChanged?.Invoke(this, theme);

        return Task.FromResult(true);
    }
}
