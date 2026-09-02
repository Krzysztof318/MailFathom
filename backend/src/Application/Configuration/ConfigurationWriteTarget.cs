// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
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
    /// <remarks>
    /// Both factories are internal so <see cref="ConfigurationStorageCatalog" /> is the only thing that produces a
    /// target. A writable target reachable beside the catalog would be a way to reach a store without the deny-list
    /// having run, which is the second entry point the catalog exists to not have.
    /// </remarks>
    internal static ConfigurationWriteTarget RoutedTo(ConfigurationStorageRoute route)
    {
        if (!route.IsSpecified)
        {
            throw new ArgumentException("A routed write target needs a store, and the value is the unspecified default.", nameof(route));
        }

        return new ConfigurationWriteTarget(route, refusalMessage: null);
    }

    /// <summary>Reports a write refused because it would reach a setting the persisted layer is itself read through.</summary>
    /// <param name="configurationPath">The path the write targeted.</param>
    /// <param name="refusedSetting">The bootstrap setting the write would reach, which the message names.</param>
    /// <returns>A refused target.</returns>
    /// <exception cref="ArgumentException">Thrown when either argument is <see langword="null" />, empty, or white space.</exception>
    /// <remarks>
    /// The message names the path the caller wrote and, when the two differ, how it reaches the refused setting — as a
    /// value beneath it, or as a section carrying it. Which of the two happened is what an administrator needs to know
    /// to write a narrower path instead.
    /// </remarks>
    internal static ConfigurationWriteTarget RefusedAsBootstrapOnly(string configurationPath, string refusedSetting)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(refusedSetting);

        var namesItExactly = string.Equals(configurationPath, refusedSetting, StringComparison.OrdinalIgnoreCase);

        var subject = namesItExactly
            ? configurationPath
            : SettingPath.Covers(refusedSetting, configurationPath)
                ? $"{configurationPath}, which is part of {refusedSetting}"
                : $"{configurationPath}, which contains {refusedSetting}";

        return new ConfigurationWriteTarget(
            route: default,
            $"MailFathom does not persist {subject}: {(namesItExactly ? "it" : refusedSetting)} is read before the persisted configuration layer exists, so a persisted value for it could not be read without first reading it. Configure it in a file, in the environment, or as a command-line argument, which is where MailFathom takes it from.");
    }
}
