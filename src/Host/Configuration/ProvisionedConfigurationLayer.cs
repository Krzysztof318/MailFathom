// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.Extensions.Configuration.EnvironmentVariables;

namespace MailFathom.Host.Configuration;

/// <summary>Decides which provisioned configuration files are layered in, and where among the host's own sources.</summary>
/// <remarks>
/// The two decisions are kept here, away from the registration that applies them, because both are contracts an
/// operator depends on and neither needs a real file system or a built host to be proven.
/// </remarks>
internal static class ProvisionedConfigurationLayer
{
    private const string JsonFileExtension = ".json";

    /// <summary>
    /// The prefix Kubernetes gives the entries it manages inside a mounted volume: <c>..data</c>, which is a symbolic
    /// link to the live version, and the timestamped directory that link points at.
    /// </summary>
    private const string VolumeBookkeepingPrefix = "..";

    /// <summary>Finds the provisioned configuration files, in the order they are layered.</summary>
    /// <param name="paths">The directory and file the deployment named.</param>
    /// <param name="fileSystem">Reports what the deployment actually mounted.</param>
    /// <returns>The absolute paths to layer, lowest precedence first, empty when the deployment provisioned nothing.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="paths" /> or <paramref name="fileSystem" /> is <see langword="null" />.</exception>
    /// <exception cref="ProvisionedConfigurationSourceInvalidException">Thrown when a configured path does not exist.</exception>
    /// <remarks>
    /// The single file is layered above the directory, so a deployment that mounts a shared ConfigMap and then names one
    /// file of its own gets the specific value rather than an order decided by how the two happen to sort.
    /// </remarks>
    public static IReadOnlyList<string> FindFiles(
        ProvisionedConfigurationPaths paths,
        IProvisionedConfigurationFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(fileSystem);

        var directoryFiles = paths.DirectoryPath is null
            ? []
            : FindDirectoryFiles(paths.DirectoryPath, fileSystem);

        if (paths.FilePath is null)
        {
            return directoryFiles;
        }

        if (!fileSystem.FileExists(paths.FilePath))
        {
            throw new ProvisionedConfigurationSourceInvalidException(
                $"The configuration file named by {ProvisionedConfigurationPaths.FileKey} does not exist: {paths.FilePath}.");
        }

        return [.. directoryFiles, paths.FilePath];
    }

    /// <summary>Finds the position at which provisioned configuration is layered into the host's own sources.</summary>
    /// <param name="sources">The configuration sources the host builder has already composed.</param>
    /// <returns>The index to insert the first provisioned source at.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sources" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// Provisioned files sit immediately below the environment-variable provider, which puts them above
    /// <c>appsettings.json</c> and its environment overlay and below environment variables and command-line arguments.
    /// That is the precedence the .NET provider order already promises an operator: a file states the deployment's
    /// configuration, and an environment variable overrides it. Layering them on top instead would let a stale mounted
    /// file silently beat a value injected per pod, which is the direction that cannot be diagnosed from the outside.
    /// </para>
    /// <para>
    /// Only the unprefixed provider is the boundary. The prefixed ones carry <c>DOTNET_</c> and <c>ASPNETCORE_</c>
    /// host settings and are composed before the application's own files, so inserting ahead of those would place
    /// provisioned configuration below <c>appsettings.json</c> and invert the whole point of mounting it.
    /// </para>
    /// </remarks>
    public static int FindInsertionIndex(IReadOnlyList<IConfigurationSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        return sources
            .Index()
            .Where(source => source.Item is EnvironmentVariablesConfigurationSource { Prefix: null or "" })
            .Select(source => source.Index)
            .DefaultIfEmpty(sources.Count)
            .First();
    }

    private static IReadOnlyList<string> FindDirectoryFiles(
        string directoryPath,
        IProvisionedConfigurationFileSystem fileSystem)
    {
        if (!fileSystem.DirectoryExists(directoryPath))
        {
            throw new ProvisionedConfigurationSourceInvalidException(
                $"The configuration directory named by {ProvisionedConfigurationPaths.DirectoryKey} does not exist: {directoryPath}.");
        }

        // Ordered ordinally rather than by the host's culture, so the same ConfigMap layers the same way on every
        // machine that mounts it.
        return
        [
            .. fileSystem.ListFileNames(directoryPath)
                .Where(IsLayeredJsonFile)
                .Order(StringComparer.Ordinal)
                .Select(fileName => Path.Combine(directoryPath, fileName)),
        ];
    }

    /// <summary>Reports whether a directory entry is a configuration file rather than the volume's own bookkeeping.</summary>
    /// <remarks>
    /// Kubernetes updates a mounted ConfigMap by writing a new timestamped directory and repointing the <c>..data</c>
    /// symbolic link at it, which is what makes the update atomic. Both entries live beside the keys and neither is
    /// configuration; skipping them by name keeps that true whichever way an enumerator classifies a link.
    /// </remarks>
    private static bool IsLayeredJsonFile(string fileName) =>
        !fileName.StartsWith(VolumeBookkeepingPrefix, StringComparison.Ordinal)
        && fileName.EndsWith(JsonFileExtension, StringComparison.OrdinalIgnoreCase);
}
