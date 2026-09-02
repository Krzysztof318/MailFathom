// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.Provisioning;

/// <summary>Reports what the deployment actually mounted, so the layering policy can be decided without a file system.</summary>
/// <remarks>
/// The seam exists for the same reason <c>ISecretFileReader</c> does: which files a directory holds, in what order they
/// are layered, and which of them are Kubernetes' own bookkeeping entries are all decisions worth proving, and none of
/// them needs a real directory to be proven. Listing returns names rather than paths so the caller owns the filtering
/// and the ordering rather than delegating them to a search pattern the tests could not observe.
/// </remarks>
internal interface IProvisionedConfigurationFileSystem
{
    /// <summary>Reports whether the provisioned configuration directory exists.</summary>
    /// <param name="directoryPath">The directory the deployment named.</param>
    /// <returns><see langword="true" /> when the directory exists.</returns>
    bool DirectoryExists(string directoryPath);

    /// <summary>Reports whether the provisioned configuration file exists.</summary>
    /// <param name="filePath">The file the deployment named.</param>
    /// <returns><see langword="true" /> when the file exists.</returns>
    bool FileExists(string filePath);

    /// <summary>Lists the names of the files the provisioned configuration directory holds, in no particular order.</summary>
    /// <param name="directoryPath">The directory the deployment named.</param>
    /// <returns>The file names, without their directory, and without the directory's subdirectories.</returns>
    IReadOnlyList<string> ListFileNames(string directoryPath);
}
