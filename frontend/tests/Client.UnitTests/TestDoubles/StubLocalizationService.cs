// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;

namespace MailFathom.Client.UnitTests.TestDoubles;

/// <summary>
/// Stands in for the localization Uno composes into a running head: it offers the cultures a test names, reports the
/// one being read in, and records what a model asked it to change to instead of writing anything.
/// </summary>
internal sealed class StubLocalizationService : ILocalizationService
{
    /// <summary>Initializes the stub with the cultures it offers and the one it starts on.</summary>
    /// <param name="current">The culture the application is being read in.</param>
    /// <param name="supported">The cultures the configuration offers.</param>
    public StubLocalizationService(string current, params string[] supported)
    {
        this.SupportedCultures = [.. supported.Select(CultureInfo.GetCultureInfo)];
        this.CurrentCulture = CultureInfo.GetCultureInfo(current);
    }

    /// <inheritdoc />
    public CultureInfo[] SupportedCultures { get; }

    /// <inheritdoc />
    public CultureInfo CurrentCulture { get; private set; }

    /// <summary>The culture the last successful change asked for, or <see langword="null" /> when none was asked for.</summary>
    public CultureInfo? Applied { get; private set; }

    /// <inheritdoc />
    public Task SetCurrentCultureAsync(CultureInfo newCulture)
    {
        this.Applied = newCulture;
        this.CurrentCulture = newCulture;

        return Task.CompletedTask;
    }
}
