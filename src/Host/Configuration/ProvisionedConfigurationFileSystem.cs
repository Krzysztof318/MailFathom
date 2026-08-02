// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration;

/// <summary>Reports the real file system to the provisioned-configuration layering policy.</summary>
internal sealed class ProvisionedConfigurationFileSystem : IProvisionedConfigurationFileSystem
{
    /// <inheritdoc />
    public bool DirectoryExists(string directoryPath) => Directory.Exists(directoryPath);

    /// <inheritdoc />
    public bool FileExists(string filePath) => File.Exists(filePath);

    /// <inheritdoc />
    public IReadOnlyList<string> ListFileNames(string directoryPath) =>
        [.. Directory.EnumerateFiles(directoryPath).Select(filePath => Path.GetFileName(filePath))];
}
