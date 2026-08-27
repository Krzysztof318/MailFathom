// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Provisioning;
using MailFathom.Host.Configuration.RootSettings;
using Microsoft.Extensions.Configuration.CommandLine;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;

namespace MailFathom.Host.Configuration.Administration;

/// <summary>Reads the deployment's settings as it composed them, saying which layer supplied each value.</summary>
/// <remarks>
/// <para>
/// The composed configuration reports a value and nothing about where it came from, which is the one thing an operator
/// about to persist a setting needs: three sources outrank the persisted layer, so a write to a path one of them
/// supplies commits and changes nothing observable. This reads the layers themselves — the providers the running
/// process holds, in the order it holds them — so the answer is what the process actually resolves rather than what
/// the precedence table says it should.
/// </para>
/// <para>
/// The providers are read rather than rebuilt, unlike the ones a candidate is judged against. A candidate has to be
/// composed from sources nobody else holds, because judging it loads files and raises reload tokens; a reading takes
/// what the process is already bound to, which is the only thing that can honestly be called effective.
/// </para>
/// <para>
/// Every value leaves through <see cref="SettingRedaction" />, so a secret-bearing setting reports the marker whichever
/// reading asked for it. That is the whole of the disclosure boundary here: a caller holding the administrative reading
/// permission learns which settings exist, where each one is decided, and what the non-secret ones say.
/// </para>
/// </remarks>
internal sealed class EffectiveSettingsReader(
    IConfigurationRoot configuration,
    RootSettingsConfigurationProvider layer)
{
    /// <summary>How many settings one reading answers with before it refuses to answer at all.</summary>
    /// <remarks>
    /// A bound on a public reading like every other in this repository, and one an operator meets by asking for the
    /// whole configuration rather than a section of it. It is far above any prefix worth reading and far below what
    /// would cost a request anything, so meeting it means the prefix was not narrowed rather than that a deployment
    /// grew.
    /// </remarks>
    internal const int MaximumSettings = 2000;

    /// <summary>The file name .NET User Secrets is layered in under, whichever store directory holds it.</summary>
    private const string UserSecretsFileName = "secrets.json";

    /// <summary>Gets the persisted configuration version this process composed its settings over.</summary>
    /// <remarks>
    /// Read from the layer rather than from the database, because it is the version the reading beside it actually
    /// describes: a row somebody moved on since is not what this process is bound to, and a write composed over the
    /// number in the row would be composed over settings this reading never reported.
    /// </remarks>
    internal long ComposedVersion => layer.Version;

    /// <summary>Reads every setting at or beneath a path, as the deployment reads it.</summary>
    /// <param name="prefix">The colon-delimited path to read beneath, or <see langword="null" /> for every setting the deployment composed.</param>
    /// <returns>The settings, ordered by path, or a reading that reports the prefix reached the bound.</returns>
    /// <remarks>Ordered by path so the same deployment answers the same reading identically, whatever order its providers happen to enumerate in.</remarks>
    internal SettingsReading Read(string? prefix)
    {
        var providers = this.Providers();
        var paths = this.PathsBeneath(prefix);

        return paths.Count > MaximumSettings
            ? SettingsReading.TooBroad(paths.Count)
            : SettingsReading.Of(
            [
                .. paths
                    .Select(path => (Path: path, Supplier: SupplierBelow(providers, path, providers.Count)))
                    .Where(found => found.Supplier is not null)
                    .Select(found => Describe(found.Path, found.Supplier!)),
            ]);
    }

    /// <summary>Reads one setting as the deployment reads it.</summary>
    /// <param name="path">The colon-delimited configuration path.</param>
    /// <returns>The setting, or <see langword="null" /> when no source supplies a value at that exact path.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="path" /> is <see langword="null" />.</exception>
    internal EffectiveSetting? ReadOne(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var providers = this.Providers();

        return SupplierBelow(providers, path, providers.Count) is { } supplier ? Describe(path, supplier) : null;
    }

    /// <summary>Reads what the sources beneath the persisted layer supply at or beneath a path.</summary>
    /// <param name="prefix">The colon-delimited path to read beneath, or <see langword="null" /> for every setting.</param>
    /// <returns>The settings the deployment's files decide, ordered by path, or a reading that reports the prefix reached the bound.</returns>
    /// <remarks>
    /// This is what an adoption copies, and it is a different question from what the deployment reads: a value an
    /// environment variable currently beats is still what the files supply, and adopting it is how an operator moves
    /// that decision into the database before removing the override.
    /// </remarks>
    internal SettingsReading ReadBeneathTheLayer(string? prefix)
    {
        var providers = this.Providers();
        var paths = this.PathsBeneath(prefix);
        var layerPosition = PositionOfLayer(providers, layer);

        return paths.Count > MaximumSettings
            ? SettingsReading.TooBroad(paths.Count)
            : SettingsReading.Of(
            [
                .. paths
                    .Select(path => (Path: path, Supplier: SupplierBelow(providers, path, layerPosition)))
                    .Where(found => found.Supplier is not null)
                    .Select(found => Describe(found.Path, found.Supplier!)),
            ]);
    }

    /// <summary>Reports the source that outranks the persisted layer at a path, so a write knows before it commits.</summary>
    /// <param name="path">The colon-delimited configuration path.</param>
    /// <returns>The setting as the outranking source supplies it, or <see langword="null" /> when nothing above the layer names the path.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="path" /> is <see langword="null" />.</exception>
    internal EffectiveSetting? ShadowOver(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var providers = this.Providers();
        var layerPosition = PositionOfLayer(providers, layer);

        var shadow = providers
            .Index()
            .Where(provider => provider.Index > layerPosition && provider.Item.TryGet(path, out _))
            .Select(provider => provider.Item)
            .LastOrDefault();

        return shadow is null ? null : Describe(path, shadow);
    }

    /// <summary>Reads what a source beneath the persisted layer supplies at one path, as it stands.</summary>
    /// <param name="path">The colon-delimited configuration path.</param>
    /// <returns>The value the files decide, or <see langword="null" /> when nothing beneath the layer supplies the path.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="path" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Unredacted, and the one reading here that is. It exists so an adoption can persist the value the files actually
    /// supply rather than the marker a reading reports, which means its answer stays inside this process: it reaches a
    /// <see cref="Application.Configuration.ConfigurationEdit" /> and never a response.
    /// </remarks>
    internal string? ValueBeneathTheLayer(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var providers = this.Providers();

        return SupplierBelow(providers, path, PositionOfLayer(providers, layer)) is { } supplier
            && supplier.TryGet(path, out var value)
                ? value
                : null;
    }

    /// <summary>Reads what the persisted layer itself carries at one path, as it stands.</summary>
    /// <param name="path">The colon-delimited configuration path.</param>
    /// <returns>The persisted value, or <see langword="null" /> when the document does not carry the path.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="path" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Unredacted for the reason <see cref="ValueBeneathTheLayer" /> is, and used for one thing: deciding whether a
    /// change would change the document at all. A write of the value the document already carries is a version an
    /// operator gains nothing from.
    /// </remarks>
    internal string? PersistedValue(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return layer.TryGet(path, out var value) ? value : null;
    }

    /// <summary>Reports whether the persisted layer itself carries a setting at or beneath a path.</summary>
    /// <param name="path">The colon-delimited configuration path.</param>
    /// <returns><see langword="true" /> when removing the path would change the persisted document.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="path" /> is <see langword="null" />.</exception>
    internal bool LayerCarries(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return this.PathsBeneath(path).Any(beneath => layer.TryGet(beneath, out _));
    }

    private IReadOnlyList<IConfigurationProvider> Providers() => [.. configuration.Providers];

    /// <summary>Names every path any source supplies a value at, within the prefix.</summary>
    /// <remarks>
    /// Taken from the composed configuration rather than from the providers one at a time, because that is what merges
    /// the layers into one set of paths: a key only the files carry and a key only the layer carries are both settings
    /// of this deployment, and a reading that enumerated the winning provider alone would miss whichever of them lost.
    /// </remarks>
    private IReadOnlyList<string> PathsBeneath(string? prefix) =>
    [
        .. configuration
            .AsEnumerable()
            .Where(setting => setting.Value is not null && IsBeneath(setting.Key, prefix))
            .Select(setting => setting.Key)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase),
    ];

    private static bool IsBeneath(string path, string? prefix) => prefix is not { Length: > 0 }
        || path.Equals(prefix, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith($"{prefix}:", StringComparison.OrdinalIgnoreCase);

    /// <summary>Finds the highest-precedence provider below a position that supplies a path.</summary>
    /// <param name="providers">The pipeline, lowest precedence first.</param>
    /// <param name="path">The configuration path.</param>
    /// <param name="below">The position to search below, which is the provider count for the whole pipeline.</param>
    /// <returns>The provider, or <see langword="null" /> when nothing below the position supplies the path.</returns>
    private static IConfigurationProvider? SupplierBelow(
        IReadOnlyList<IConfigurationProvider> providers,
        string path,
        int below) =>
        providers
            .Take(Math.Clamp(below, 0, providers.Count))
            .Where(provider => provider.TryGet(path, out _))
            .LastOrDefault();

    /// <summary>Finds where the persisted layer sits in the pipeline, or past the end when this host composed none.</summary>
    /// <remarks>
    /// Past the end rather than at the start, so a host without the layer reports every source as beneath it and
    /// nothing as shadowing it — which is the truthful answer for a deployment whose settings are files alone.
    /// </remarks>
    private static int PositionOfLayer(
        IReadOnlyList<IConfigurationProvider> providers,
        RootSettingsConfigurationProvider persisted) =>
        providers
            .Index()
            .Where(provider => ReferenceEquals(provider.Item, persisted))
            .Select(provider => provider.Index)
            .DefaultIfEmpty(providers.Count)
            .First();

    private static EffectiveSetting Describe(string path, IConfigurationProvider supplier)
    {
        _ = supplier.TryGet(path, out var value);

        return new EffectiveSetting(
            path,
            SettingRedaction.Apply(path, value ?? string.Empty),
            SourceOf(supplier),
            OriginOf(supplier),
            SettingRedaction.Redacts(path));
    }

    /// <summary>Says which layer a provider is, by the type the composition built it as.</summary>
    /// <remarks>
    /// The persisted layer is recognized by identity rather than by type, because a candidate composition builds a
    /// second provider of the same type over a document that is not this deployment's. Everything else is recognized by
    /// type, which is what <see cref="OperatorOverrideBoundary" /> already does and for the reason it gives: a file name
    /// is something a deployment chooses, so User Secrets is told apart by the framework's own file name and a
    /// provisioned file by the type this host constructs for it.
    /// </remarks>
    private static SettingSource SourceOf(IConfigurationProvider provider) => provider switch
    {
        RootSettingsConfigurationProvider => SettingSource.PersistedLayer,
        CommandLineConfigurationProvider => SettingSource.CommandLine,
        EnvironmentVariablesConfigurationProvider => SettingSource.EnvironmentVariable,
        JsonConfigurationProvider { Source: ProvisionedJsonConfigurationSource } => SettingSource.File,
        JsonConfigurationProvider json => IsUserSecrets(json) ? SettingSource.UserSecrets : SettingSource.File,
        _ => SettingSource.Unclassified,
    };

    private static bool IsUserSecrets(JsonConfigurationProvider json) =>
        string.Equals(json.Source.Path, UserSecretsFileName, StringComparison.Ordinal);

    /// <summary>Names the one instance of a source whose kind has several, and nothing for a kind that has one.</summary>
    /// <remarks>
    /// A file path is the only such name. It is the deployment's own — a mount point, an image path — rather than
    /// anything derived from mail, and it is what an operator has to be told to repair a value that came from a file
    /// they did not remember mounting.
    /// </remarks>
    private static string? OriginOf(IConfigurationProvider provider) =>
        provider is JsonConfigurationProvider { Source.Path: { Length: > 0 } path } ? path : null;
}
