// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailMcp.Host.Configuration;

namespace MailMcp.Host.UnitTests;

/// <summary>Reports a mount the test describes, so layering can be proven without a directory on disk.</summary>
internal sealed class FakeProvisionedConfigurationFileSystem : IProvisionedConfigurationFileSystem
{
    private readonly Dictionary<string, IReadOnlyList<string>> directories = new(StringComparer.Ordinal);
    private readonly HashSet<string> files = new(StringComparer.Ordinal);

    /// <summary>Describes a mounted directory holding the given entry names.</summary>
    /// <param name="directoryPath">The directory the deployment named.</param>
    /// <param name="fileNames">The entry names the directory holds, in whatever order the file system reports them.</param>
    /// <returns>The same instance, so a test can describe a mount in one expression.</returns>
    public FakeProvisionedConfigurationFileSystem WithDirectory(string directoryPath, params string[] fileNames)
    {
        this.directories[directoryPath] = fileNames;

        return this;
    }

    /// <summary>Describes a mounted single file.</summary>
    /// <param name="filePath">The file the deployment named.</param>
    /// <returns>The same instance, so a test can describe a mount in one expression.</returns>
    public FakeProvisionedConfigurationFileSystem WithFile(string filePath)
    {
        this.files.Add(filePath);

        return this;
    }

    /// <inheritdoc />
    public bool DirectoryExists(string directoryPath) => this.directories.ContainsKey(directoryPath);

    /// <inheritdoc />
    public bool FileExists(string filePath) => this.files.Contains(filePath);

    /// <inheritdoc />
    public IReadOnlyList<string> ListFileNames(string directoryPath) => this.directories[directoryPath];
}
