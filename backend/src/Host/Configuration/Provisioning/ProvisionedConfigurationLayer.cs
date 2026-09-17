// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.Provisioning;

/// <summary>Decides which provisioned configuration files are layered in, and where among the host's own sources.</summary>
/// <remarks>
/// The two decisions are kept here, away from the registration that applies them, because both are contracts an
/// operator depends on and neither needs a real file system or a built host to be proven.
/// </remarks>
internal static class ProvisionedConfigurationLayer
{
    /// <summary>
    /// The prefix Kubernetes gives the entries it manages inside a mounted volume: <c>..data</c>, which is a symbolic
    /// link to the live version, and the timestamped directory that link points at.
    /// </summary>
    private const string VolumeBookkeepingPrefix = "..";

    /// <summary>Finds the provisioned configuration files, in the order they are layered.</summary>
    /// <param name="paths">The directory and file the deployment named.</param>
    /// <param name="fileSystem">Reports what the deployment actually mounted.</param>
    /// <returns>The files to layer with the format each is read in, lowest precedence first, empty when the deployment provisioned nothing.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="paths" /> or <paramref name="fileSystem" /> is <see langword="null" />.</exception>
    /// <exception cref="ProvisionedConfigurationSourceInvalidException">
    /// Thrown when a configured path does not exist, the named file is neither JSON nor YAML, or the directory holds two
    /// files whose names differ only by extension.
    /// </exception>
    /// <remarks>
    /// The single file is layered above the directory, so a deployment that mounts a shared ConfigMap and then names one
    /// file of its own gets the specific value rather than an order decided by how the two happen to sort.
    /// </remarks>
    public static IReadOnlyList<ProvisionedConfigurationFile> FindFiles(
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

        var format = ProvisionedConfigurationFile.FormatOf(paths.FilePath)
            ?? throw new ProvisionedConfigurationSourceInvalidException(
                $"The configuration file named by {ProvisionedConfigurationPaths.FileKey} is neither JSON nor YAML: {paths.FilePath}. "
                + "Name it with a .json, .yaml, or .yml extension, which is what decides how it is read.");

        return [.. directoryFiles, new ProvisionedConfigurationFile(paths.FilePath, format)];
    }

    /// <summary>Finds the position at which provisioned configuration is layered into the host's own sources.</summary>
    /// <param name="sources">The configuration sources the host builder has already composed.</param>
    /// <returns>The index to insert the first provisioned source at.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sources" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Provisioned files sit directly below the operator's overrides, which puts them above <c>appsettings.json</c> and
    /// its environment overlay and below User Secrets, environment variables, and command-line arguments.
    /// <see cref="OperatorOverrideBoundary" /> holds why that direction is the one an operator can act on, and the
    /// persisted settings layer is inserted at the same boundary afterwards, which is what places it above these files.
    /// </remarks>
    public static int FindInsertionIndex(IReadOnlyList<IConfigurationSource> sources) =>
        OperatorOverrideBoundary.FindIn(sources);

    private static IReadOnlyList<ProvisionedConfigurationFile> FindDirectoryFiles(
        string directoryPath,
        IProvisionedConfigurationFileSystem fileSystem)
    {
        if (!fileSystem.DirectoryExists(directoryPath))
        {
            throw new ProvisionedConfigurationSourceInvalidException(
                $"The configuration directory named by {ProvisionedConfigurationPaths.DirectoryKey} does not exist: {directoryPath}.");
        }

        // Ordered ordinally rather than by the host's culture, so the same ConfigMap layers the same way on every
        // machine that mounts it, and by the whole name so a JSON and a YAML file interleave by what they are called.
        var layered = fileSystem.ListFileNames(directoryPath)
            .Where(fileName => !IsVolumeBookkeeping(fileName))
            .Select(fileName => (Name: fileName, Format: ProvisionedConfigurationFile.FormatOf(fileName)))
            .Where(entry => entry.Format is not null)
            .OrderBy(entry => entry.Name, StringComparer.Ordinal)
            .ToArray();

        RejectNamesThatDifferOnlyByExtension(directoryPath, [.. layered.Select(entry => entry.Name)]);

        return
        [
            .. layered.Select(entry => new ProvisionedConfigurationFile(
                Path.Combine(directoryPath, entry.Name),
                entry.Format!.Value)),
        ];
    }

    /// <summary>Fails when two layered files share a name apart from their extension.</summary>
    /// <remarks>
    /// <c>10-mail.json</c> and <c>10-mail.yaml</c> would be ordered by the extension, which is a choice nobody made:
    /// the operator names a file to place it, and two files at the same place leave the later one's precedence to how
    /// <c>.json</c> and <c>.yaml</c> happen to sort.
    /// </remarks>
    private static void RejectNamesThatDifferOnlyByExtension(string directoryPath, IReadOnlyList<string> fileNames)
    {
        var colliding = fileNames
            .GroupBy(Path.GetFileNameWithoutExtension, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .SelectMany(group => group)
            .ToArray();

        if (colliding.Length > 0)
        {
            throw new ProvisionedConfigurationSourceInvalidException(
                $"The configuration directory named by {ProvisionedConfigurationPaths.DirectoryKey} holds files whose names differ only by extension: {string.Join(", ", colliding)} in {directoryPath}. "
                + "Their order would be decided by the extension rather than by their names, so rename one of them.");
        }
    }

    /// <summary>Reports whether a directory entry is the volume's own bookkeeping rather than a file an operator wrote.</summary>
    /// <remarks>
    /// Kubernetes updates a mounted ConfigMap by writing a new timestamped directory and repointing the <c>..data</c>
    /// symbolic link at it, which is what makes the update atomic. Both entries live beside the keys and neither is
    /// configuration; skipping them by name keeps that true whichever way an enumerator classifies a link.
    /// </remarks>
    private static bool IsVolumeBookkeeping(string fileName) =>
        fileName.StartsWith(VolumeBookkeepingPrefix, StringComparison.Ordinal);
}
