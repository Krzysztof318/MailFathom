// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Configuration;

/// <summary>Carries the store a configuration write lands in, or the reason the setting may not be written at all.</summary>
/// <remarks>
/// A refused write is a result rather than an exception because the caller acts on it directly: a write is validated
/// before it commits, and an administrator who asked for a setting MailFathom does not persist gets that sentence back
/// as the outcome of their request, in the same shape as an unknown property or a stale version.
/// <para>
/// The message names the setting and never its value. A key is MailFathom's own name for a setting, in the same class
/// as an account alias, while the values behind the refused settings are a connection string, a credential reference,
/// and a filesystem path the deployment chose.
/// </para>
/// </remarks>
public sealed record ConfigurationWriteTarget
{
    private ConfigurationWriteTarget(ConfigurationStorageRoute route, string? refusalMessage)
    {
        this.Route = route;
        this.RefusalMessage = refusalMessage;
    }

    /// <summary>Gets the store the write lands in, which is a route exactly when <see cref="IsWritable" /> holds.</summary>
    /// <remarks>A refused write carries the unspecified default rather than a store, because there is no store a bootstrap setting is persisted in.</remarks>
    public ConfigurationStorageRoute Route { get; }

    /// <summary>Gets the sentence naming the setting and where it is configured instead, or <see langword="null" /> when the write is permitted.</summary>
    public string? RefusalMessage { get; }

    /// <summary>Gets whether the write may proceed.</summary>
    public bool IsWritable => this.Route.IsSpecified;

    /// <summary>Reports a write the catalog routes to a store.</summary>
    /// <param name="route">The store the setting is persisted in.</param>
    /// <returns>A routed target.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="route" /> is the unspecified default rather than a store.</exception>
    public static ConfigurationWriteTarget RoutedTo(ConfigurationStorageRoute route)
    {
        if (!route.IsSpecified)
        {
            throw new ArgumentException("A routed write target needs a store, and the value is the unspecified default.", nameof(route));
        }

        return new ConfigurationWriteTarget(route, refusalMessage: null);
    }

    /// <summary>Reports a write refused because it targets a setting the persisted layer is itself reached through.</summary>
    /// <param name="configurationPath">The path the write targeted.</param>
    /// <param name="refusedSetting">The bootstrap setting covering that path, which the message names.</param>
    /// <returns>A refused target.</returns>
    /// <exception cref="ArgumentException">Thrown when either argument is <see langword="null" />, empty, or white space.</exception>
    public static ConfigurationWriteTarget RefusedAsBootstrapOnly(string configurationPath, string refusedSetting)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(refusedSetting);

        var setting = SettingPath.Covers(refusedSetting, configurationPath) && !string.Equals(configurationPath, refusedSetting, StringComparison.OrdinalIgnoreCase)
            ? $"{configurationPath}, which is part of {refusedSetting}"
            : configurationPath;

        return new ConfigurationWriteTarget(
            route: default,
            $"MailFathom does not persist {setting}: it is read before the persisted configuration layer exists, so a persisted value for it could not be read without first reading it. Configure it in a file, in the environment, or as a command-line argument, which is where MailFathom takes it from.");
    }
}
