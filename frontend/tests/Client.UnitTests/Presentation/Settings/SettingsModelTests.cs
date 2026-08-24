// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Reflection;
using MailFathom.Client.Backend;
using MailFathom.Client.Backend.Authorization;
using MailFathom.Client.Presentation;
using MailFathom.Client.Presentation.Settings;
using MailFathom.Client.UnitTests.TestDoubles;

namespace MailFathom.Client.UnitTests.Presentation.Settings;

public sealed class SettingsModelTests
{
    /// <summary>Awaiting a feed is how a model's state is asserted in this stack, and this one proves the MVUX path behind the settings screen reaches the running build.</summary>
    [Fact]
    public async Task Build_TheScaffoldModel_YieldsTheRunningBuild()
    {
        // Arrange
        await using var model = ModelOver();

        // Act
        var build = await model.Build;

        // Assert
        Assert.Equal(ClientBuild.Current, build);
    }

    /// <summary>Which deployment is being looked at has to be readable without signing in to find out.</summary>
    [Fact]
    public async Task Deployment_AClientPointedAtADeployment_NamesIt()
    {
        // Arrange
        var address = new DeploymentAddress(new AccessTokenStore());
        address.PointAt(new Uri("https://mail.example/"));

        await using var model = ModelOver(address: address);

        // Act
        var deployment = await model.Deployment;

        // Assert
        Assert.Equal("https://mail.example/", deployment);
    }

    /// <summary>The screen offers what the configuration named and nothing else, in the order it named it.</summary>
    [Fact]
    public async Task Languages_TheConfiguredCultures_AreTheOnesOffered()
    {
        // Arrange
        await using var model = ModelOver();

        // Act
        var languages = await model.Languages;

        // Assert
        Assert.Equal(["en", "pl"], languages.Select(language => language.Tag));
    }

    /// <summary>The picker opens on the language being read rather than on whichever one the list happens to start with.</summary>
    [Fact]
    public async Task ChosenLanguage_ACultureBeingReadIn_IsWhatTheChoiceStartsOn()
    {
        // Arrange
        await using var model = ModelOver(new StubLocalizationService("pl", "en", "pl"));

        // Act
        var chosen = await model.ChosenLanguage;

        // Assert
        Assert.Equal("pl", chosen?.Tag);
    }

    /// <summary>Taking a language writes it, and says that a restart is what it arrives on.</summary>
    [Fact]
    public async Task ApplyLanguage_ALanguageTheConfigurationOffers_IsTakenAndAwaitsARestart()
    {
        // Arrange
        var localization = new StubLocalizationService("en", "en", "pl");
        await using var model = ModelOver(localization);
        var polish = AppLanguage.FromCulture(CultureInfo.GetCultureInfo("pl"));
        await model.ChosenLanguage.UpdateAsync(_ => polish, TestContext.Current.CancellationToken);

        // Act
        await model.ApplyLanguage(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("pl", localization.Applied?.Name);
        Assert.True(await model.LanguageAwaitsRestart);
    }

    /// <summary>A language with no string table behind it is dropped rather than written, because writing it would empty the next launch.</summary>
    [Fact]
    public async Task ApplyLanguage_ALanguageTheConfigurationDoesNotOffer_IsNotTaken()
    {
        // Arrange
        var localization = new StubLocalizationService("en", "en", "pl");
        await using var model = ModelOver(localization);
        await model.ChosenLanguage.UpdateAsync(_ => new AppLanguage("de", "Deutsch"), TestContext.Current.CancellationToken);

        // Act
        await model.ApplyLanguage(TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(localization.Applied);
        Assert.False(await model.LanguageAwaitsRestart);
    }

    /// <summary>Following the operating system is one of the three choices rather than an absence of one.</summary>
    [Fact]
    public async Task ThemeOptions_TheScaffoldModel_OffersSystemLightAndDark()
    {
        // Arrange
        await using var model = ModelOver();

        // Act
        var options = await model.ThemeOptions;

        // Assert
        Assert.Equal(
            [AppTheme.System, AppTheme.Light, AppTheme.Dark],
            options.Select(option => option.Theme));
    }

    /// <summary>Each theme reaches the picker as words rather than as the enum member's name, which is an identifier.</summary>
    [Fact]
    public async Task ThemeOptions_TheStringTable_NamesEveryOfferInTheLanguageBeingReadIn()
    {
        // Arrange
        await using var model = ModelOver();

        // Act
        var options = await model.ThemeOptions;

        // Assert
        Assert.Equal(
            ["Follow the system", "Light", "Dark"],
            options.Select(option => option.Name));
    }

    /// <summary>The state starts at what the service read back, which is the choice a previous run persisted.</summary>
    [Fact]
    public async Task ChosenTheme_AServiceHoldingAPersistedChoice_StartsAtThatChoice()
    {
        // Arrange
        await using var model = ModelOver(themes: new StubThemeService { Theme = AppTheme.Dark });

        // Act
        var chosen = await model.ChosenTheme;

        // Assert
        Assert.Equal(AppTheme.Dark, chosen?.Theme);
    }

    /// <summary>Picking one is the whole of changing the theme: the service is what applies and persists it.</summary>
    /// <remarks>
    /// The state hands the value over on its own pipeline rather than inside the write, so the assertion waits for the
    /// service to be reached instead of reading the recording straight after the write, which would be a race.
    /// </remarks>
    [Fact]
    public async Task ChosenTheme_PickedInTheView_IsHandedToTheThemeService()
    {
        // Arrange
        var themes = new StubThemeService();
        await using var model = ModelOver(themes: themes);
        var offers = await model.ThemeOptions;
        var light = offers.First(option => option.Theme is AppTheme.Light);
        var handedOver = new TaskCompletionSource<AppTheme>(TaskCreationOptions.RunContinuationsAsynchronously);
        themes.ThemeChanged += (_, theme) =>
        {
            if (theme is AppTheme.Light)
            {
                handedOver.TrySetResult(theme);
            }
        };

        // Act
        await model.ChosenTheme.UpdateAsync(_ => light, TestContext.Current.CancellationToken);

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
    public async Task ChosenTheme_ReadTwice_IsOneState()
    {
        // Arrange
        await using var model = ModelOver();

        // Act
        var (first, second) = (model.ChosenTheme, model.ChosenTheme);

        // Assert
        Assert.Same(first, second);
    }

    /// <summary>A model that could be constructed without one of its services would be one whose screen silently did nothing.</summary>
    [Fact]
    public void Constructor_AMissingService_IsRefused()
    {
        // Arrange
        var localization = new StubLocalizationService("en", "en", "pl");
        var themes = new StubThemeService();
        var address = new DeploymentAddress(new AccessTokenStore());
        var localizer = ThemeWords();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new SettingsModel(null!, themes, address, localizer));
        Assert.Throws<ArgumentNullException>(() => new SettingsModel(localization, null!, address, localizer));
        Assert.Throws<ArgumentNullException>(() => new SettingsModel(localization, themes, null!, localizer));
        Assert.Throws<ArgumentNullException>(() => new SettingsModel(localization, themes, address, null!));
    }

    /// <summary>
    /// The model that offers the languages is a plain record, so it is reachable by the tests above without a visual
    /// tree. Nothing on its surface or in its fields is a WinUI type, which is what keeps that true as it grows.
    /// </summary>
    [Fact]
    public void SettingsModel_TheModelOfferingTheLanguages_NamesNoXamlType()
    {
        // Arrange
        const BindingFlags Instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var model = typeof(SettingsModel);

        // Act
        var named = model.GetConstructors().SelectMany(constructor => constructor.GetParameters()).Select(parameter => parameter.ParameterType)
            .Concat(model.GetProperties(Instance).Select(property => property.PropertyType))
            .Concat(model.GetFields(Instance).Select(field => field.FieldType))
            .Concat(model.GetMethods(Instance).Where(method => method.DeclaringType == model).SelectMany(WrittenBy))
            .SelectMany(Unwrap)
            .Where(type => type.Namespace?.StartsWith("Microsoft.UI.Xaml", StringComparison.Ordinal) is true)
            .Select(type => type.FullName)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        // Assert
        Assert.Empty(named);

        static IEnumerable<Type> WrittenBy(MethodInfo method) =>
            method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType);

        static IEnumerable<Type> Unwrap(Type type) =>
            type.GetGenericArguments().SelectMany(Unwrap).Append(type);
    }

    private static SettingsModel ModelOver(
        StubLocalizationService? localization = null,
        StubThemeService? themes = null,
        DeploymentAddress? address = null) =>
        new(
            localization ?? new StubLocalizationService("en", "en", "pl"),
            themes ?? new StubThemeService(),
            address ?? new DeploymentAddress(new AccessTokenStore()),
            ThemeWords());

    private static StubStringLocalizer ThemeWords() => new(new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [AppThemeOption.ResourceKeyFor(AppTheme.System)] = "Follow the system",
        [AppThemeOption.ResourceKeyFor(AppTheme.Light)] = "Light",
        [AppThemeOption.ResourceKeyFor(AppTheme.Dark)] = "Dark",
    });
}
