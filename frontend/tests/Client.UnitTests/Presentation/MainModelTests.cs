// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Presentation;
using MailFathom.Client.UnitTests.TestDoubles;

namespace MailFathom.Client.UnitTests.Presentation;

public sealed class MainModelTests
{
    /// <summary>Awaiting a feed is how a model's state is asserted in this stack, and this one proves the MVUX path behind the only screen reaches the running build.</summary>
    [Fact]
    public async Task Build_TheScaffoldModel_YieldsTheRunningBuild()
    {
        // Arrange
        await using var model = new MainModel(new StubThemeService());

        // Act
        var build = await model.Build;

        // Assert
        Assert.Equal(ClientBuild.Current, build);
    }

    /// <summary>Following the operating system is one of the three choices rather than an absence of one.</summary>
    [Fact]
    public async Task ThemeOptions_TheScaffoldModel_OffersSystemLightAndDark()
    {
        // Arrange
        await using var model = new MainModel(new StubThemeService());

        // Act
        var options = model.ThemeOptions;

        // Assert
        Assert.Equal([AppTheme.System, AppTheme.Light, AppTheme.Dark], options);
    }

    /// <summary>The state starts at what the service read back, which is the choice a previous run persisted.</summary>
    [Fact]
    public async Task Theme_AServiceHoldingAPersistedChoice_StartsAtThatChoice()
    {
        // Arrange
        var themes = new StubThemeService { Theme = AppTheme.Dark };
        await using var model = new MainModel(themes);

        // Act
        var theme = await model.Theme;

        // Assert
        Assert.Equal(AppTheme.Dark, theme);
    }

    /// <summary>Writing the state is the whole of changing the theme: the service is what applies and persists it.</summary>
    /// <remarks>
    /// The state hands the value over on its own pipeline rather than inside the write, so the assertion waits for the
    /// service to be reached instead of reading the recording straight after the write, which would be a race.
    /// </remarks>
    [Fact]
    public async Task Theme_WrittenByTheView_IsHandedToTheThemeService()
    {
        // Arrange
        var themes = new StubThemeService();
        await using var model = new MainModel(themes);
        _ = await model.Theme;
        var handedOver = new TaskCompletionSource<AppTheme>(TaskCreationOptions.RunContinuationsAsynchronously);
        themes.ThemeChanged += (_, theme) =>
        {
            if (theme is AppTheme.Light)
            {
                handedOver.TrySetResult(theme);
            }
        };

        // Act
        await model.Theme.SetAsync(AppTheme.Light, TestContext.Current.CancellationToken);

        // Assert
        var applied = await handedOver.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(AppTheme.Light, applied);
    }

    /// <summary>
    /// The property is expression-bodied, so it is worth stating that it hands back one state rather than building a
    /// new one per read: a second state would carry a second subscription to the service and would leave the binding
    /// in the view and anything the model read holding different values of the same thing.
    /// </summary>
    [Fact]
    public async Task Theme_ReadTwice_IsOneState()
    {
        // Arrange
        await using var model = new MainModel(new StubThemeService());

        // Act
        var (first, second) = (model.Theme, model.Theme);

        // Assert
        Assert.Same(first, second);
    }

    /// <summary>A model that could be constructed without a theme service would be one whose theme silently did nothing.</summary>
    [Fact]
    public void Constructor_NoThemeService_IsRefused()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new MainModel(null!));
    }
}
