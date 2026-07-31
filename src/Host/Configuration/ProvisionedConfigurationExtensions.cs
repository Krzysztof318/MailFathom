// Copyright © 2026 Krzysztof Kasprowicz

using Microsoft.Extensions.Configuration.Json;

namespace MailMcp.Host.Configuration;

/// <summary>Layers deployment-provisioned JSON configuration into the sources the host builder has already composed.</summary>
internal static class ProvisionedConfigurationExtensions
{
    /// <summary>Layers in the configuration files the deployment provisioned, reading the real file system.</summary>
    /// <param name="configuration">The host builder's configuration, which is both the source list and the place the two keys are read from.</param>
    /// <returns>The number of files layered in, which is zero when the deployment provisioned none.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration" /> is <see langword="null" />.</exception>
    /// <exception cref="ProvisionedConfigurationSourceInvalidException">Thrown when a configured path does not exist.</exception>
    public static int AddProvisionedConfiguration(this IConfigurationManager configuration) =>
        configuration.AddProvisionedConfiguration(new ProvisionedConfigurationFileSystem());

    /// <summary>Layers in the configuration files the deployment provisioned, reading a given file system.</summary>
    /// <param name="configuration">The host builder's configuration, which is both the source list and the place the two keys are read from.</param>
    /// <param name="fileSystem">Reports what the deployment actually mounted.</param>
    /// <returns>The number of files layered in, which is zero when the deployment provisioned none.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration" /> or <paramref name="fileSystem" /> is <see langword="null" />.</exception>
    /// <exception cref="ProvisionedConfigurationSourceInvalidException">Thrown when a configured path does not exist.</exception>
    /// <remarks>
    /// A deployment that names neither path leaves the source list exactly as the host builder composed it, so the
    /// default configuration order is unchanged for everything that does not mount anything.
    /// </remarks>
    internal static int AddProvisionedConfiguration(
        this IConfigurationManager configuration,
        IProvisionedConfigurationFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(fileSystem);

        var paths = ProvisionedConfigurationPaths.ReadFrom(configuration);

        if (!paths.AreConfigured)
        {
            return 0;
        }

        var files = ProvisionedConfigurationLayer.FindFiles(paths, fileSystem);
        var insertionIndex = ProvisionedConfigurationLayer.FindInsertionIndex([.. configuration.Sources]);

        foreach (var (offset, filePath) in files.Index())
        {
            configuration.Sources.Insert(insertionIndex + offset, CreateJsonSource(filePath));
        }

        return files.Count;
    }

    private static JsonConfigurationSource CreateJsonSource(string filePath)
    {
        var source = new JsonConfigurationSource
        {
            Path = filePath,

            // The existence check has already run and produced a message naming the configuration key that pointed at
            // the path. Letting the provider enforce the same condition again would give one rule two mechanisms, and
            // the second one reports a bare file path with no indication of which setting named it.
            Optional = true,
            ReloadOnChange = true,
        };

        // AddJsonFile resolves the file provider for a rooted path as part of appending the source, and appending is
        // exactly what must not happen here. This is the same call that helper makes, without the append.
        source.ResolveFileProvider();

        return source;
    }
}
