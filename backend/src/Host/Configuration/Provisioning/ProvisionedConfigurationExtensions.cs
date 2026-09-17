// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.Provisioning;

/// <summary>Layers deployment-provisioned JSON and YAML configuration into the sources the host builder has already composed.</summary>
internal static class ProvisionedConfigurationExtensions
{
    /// <summary>Layers in the configuration files the deployment provisioned, reading the real file system.</summary>
    /// <param name="configuration">The host builder's configuration, which is both the source list and the place the two keys are read from.</param>
    /// <returns>The files layered in with the format each was read in, which is empty when the deployment provisioned none.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration" /> is <see langword="null" />.</exception>
    /// <exception cref="ProvisionedConfigurationSourceInvalidException">Thrown when the configured paths do not describe files MailFathom can layer.</exception>
    public static IReadOnlyList<ProvisionedConfigurationFile> AddProvisionedConfiguration(this IConfigurationManager configuration) =>
        configuration.AddProvisionedConfiguration(new ProvisionedConfigurationFileSystem());

    /// <summary>Layers in the configuration files the deployment provisioned, reading a given file system.</summary>
    /// <param name="configuration">The host builder's configuration, which is both the source list and the place the two keys are read from.</param>
    /// <param name="fileSystem">Reports what the deployment actually mounted.</param>
    /// <returns>The files layered in with the format each was read in, which is empty when the deployment provisioned none.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration" /> or <paramref name="fileSystem" /> is <see langword="null" />.</exception>
    /// <exception cref="ProvisionedConfigurationSourceInvalidException">Thrown when the configured paths do not describe files MailFathom can layer.</exception>
    /// <remarks>
    /// A deployment that names neither path leaves the source list exactly as the host builder composed it, so the
    /// default configuration order is unchanged for everything that does not mount anything.
    /// </remarks>
    internal static IReadOnlyList<ProvisionedConfigurationFile> AddProvisionedConfiguration(
        this IConfigurationManager configuration,
        IProvisionedConfigurationFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(fileSystem);

        var paths = ProvisionedConfigurationPaths.ReadFrom(configuration);

        if (!paths.AreConfigured)
        {
            return [];
        }

        var files = ProvisionedConfigurationLayer.FindFiles(paths, fileSystem);
        var insertionIndex = ProvisionedConfigurationLayer.FindInsertionIndex([.. configuration.Sources]);

        foreach (var (offset, file) in files.Index())
        {
            configuration.Sources.Insert(insertionIndex + offset, CreateSource(file));
        }

        return files;
    }

    private static FileConfigurationSource CreateSource(ProvisionedConfigurationFile file)
    {
        FileConfigurationSource source = file.Format switch
        {
            ProvisionedConfigurationFormat.Json => new ProvisionedJsonConfigurationSource(),
            ProvisionedConfigurationFormat.Yaml => new ProvisionedYamlConfigurationSource(),
            _ => throw new ArgumentOutOfRangeException(nameof(file), file.Format, "The provisioned file has no format this host reads."),
        };

        source.Path = file.Path;

        // Required, because the existence check cannot be atomic with the load: a mount being updated could remove
        // the file in between, and an optional provider would turn that into an empty source and let startup
        // continue on lower-precedence defaults. The check still owns the ordinary diagnosis — it names the
        // configuration key, which the provider's own FileNotFoundException does not — and this closes the window
        // the check structurally cannot cover.
        //
        // It costs nothing on the reload path. A watcher-driven reload of a file that has since disappeared empties
        // the provider and raises the change token without throwing, whatever this flag says; only the initial load
        // and an explicit IConfigurationRoot.Reload throw, and nothing here calls the latter.
        source.Optional = false;
        source.ReloadOnChange = true;

        // AddJsonFile resolves the file provider for a rooted path as part of appending the source, and appending is
        // exactly what must not happen here. This is the same call that helper makes, without the append, and it is
        // FileConfigurationSource's own, so the YAML source resolves its provider the same way.
        source.ResolveFileProvider();

        return source;
    }
}
