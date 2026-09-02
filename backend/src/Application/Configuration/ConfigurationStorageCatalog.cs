// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Configuration;

/// <summary>Decides, in compiled code, which store each writable configuration path is persisted in.</summary>
/// <remarks>
/// <para>
/// Where a setting lives cannot be answered per call: a path that could land in two places is a value with two truths,
/// and a later reader would have to choose between them with nothing to choose on. So the answer is a catalog rather
/// than an argument. A path no entry names is persisted in <see cref="ConfigurationStorageRoute.RootDocument" />, and a
/// path an entry names is persisted in that entry's store and excluded from the root document, which is what keeps one
/// setting to one home.
/// </para>
/// <para>
/// The catalog takes a configuration path and nothing else. It reads no configuration, accepts no relation name, and
/// exposes no way to register an entry, because a store an API caller or a configuration value could ask for would be
/// a relation this deployment never reviewed and a document nothing knows how to read back. Adding a special route is
/// therefore a reviewed change to this file: an entry here, a typed projection for the document it stores, a table
/// with its additive migration, and round-trip tests over the three.
/// </para>
/// <para>
/// Refusing a write is part of the same decision and is reached through the same method. Routing and the bootstrap
/// deny-list are one answer rather than two calls a caller could make one of and forget the other, so a write that
/// resolves a store has already been judged against <see cref="BootstrapOnlySettings" />.
/// </para>
/// </remarks>
public static class ConfigurationStorageCatalog
{
    /// <summary>The paths that are persisted somewhere other than the root document, and where each one goes.</summary>
    /// <remarks>
    /// The owner-account collection is the top-level <c>Accounts</c>, which is not <c>MailSynchronization:Accounts</c>:
    /// one document per owner rather than one settings row per mailbox. Everything nested beneath a path listed here
    /// travels with it, so a route is a section rather than a single key.
    /// </remarks>
    private static readonly (string Path, ConfigurationStorageRoute Route)[] SpecialRoutes =
    [
        ("Accounts", ConfigurationStorageRoute.OwnerAccounts),
    ];

    /// <summary>Resolves where a write to a configuration path lands, or why it may not be written.</summary>
    /// <param name="configurationPath">The colon-delimited configuration path the write targets.</param>
    /// <returns>The store the write is persisted in, or a refusal naming the setting.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="configurationPath" /> is <see langword="null" />, empty, or white space.</exception>
    public static ConfigurationWriteTarget ResolveWriteTarget(string configurationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationPath);

        return BootstrapOnlySettings.TryFindCovering(configurationPath, out var refusedSetting)
            ? ConfigurationWriteTarget.RefusedAsBootstrapOnly(configurationPath, refusedSetting)
            : ConfigurationWriteTarget.RoutedTo(Resolve(configurationPath));
    }

    /// <summary>Finds the settings a root-document candidate carries that the catalog persists somewhere else.</summary>
    /// <param name="rootDocumentKeys">Every configuration key the persisted root document flattened to.</param>
    /// <returns>The specially routed paths the document reaches, ordered as they are declared, empty when it reaches none.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="rootDocumentKeys" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The exclusion is what makes the routing worth anything. A root document carrying an owner's account beside the
    /// <see cref="ConfigurationStorageRoute.OwnerAccounts" /> store's copy of it would leave two rows describing one
    /// setting, and the reader that composed them would be choosing which of the two the deployment meant.
    /// </remarks>
    public static IReadOnlyList<string> FindRoutedElsewhereIn(IEnumerable<string> rootDocumentKeys)
    {
        ArgumentNullException.ThrowIfNull(rootDocumentKeys);

        return SettingPath.FindReachedIn(SpecialRoutes.Select(entry => entry.Path), rootDocumentKeys);
    }

    private static ConfigurationStorageRoute Resolve(string configurationPath)
    {
        var special = SpecialRoutes.FirstOrDefault(entry => SettingPath.Covers(entry.Path, configurationPath));

        return special.Route.IsSpecified ? special.Route : ConfigurationStorageRoute.RootDocument;
    }
}
