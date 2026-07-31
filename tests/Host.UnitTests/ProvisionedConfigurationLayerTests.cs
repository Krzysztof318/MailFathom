// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Domain.Failures;
using MailMcp.Host.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Configuration.Memory;
using Xunit;

namespace MailMcp.Host.UnitTests;

/// <summary>Covers which provisioned configuration files are layered in, and where among the host's own sources.</summary>
/// <remarks>
/// Both decisions are contracts an operator reasons about: which files a mounted ConfigMap contributes, and whether an
/// environment variable still overrides them. Neither needs a directory on disk to be proven, which is what keeps them
/// here rather than in the integration suite.
/// </remarks>
public sealed class ProvisionedConfigurationLayerTests
{
    private const string MountPath = "/etc/mailmcp/config";

    [Fact]
    public void FindFiles_NothingProvisioned_LayersNoFile()
    {
        // Arrange
        var paths = new ProvisionedConfigurationPaths(null, null);

        // Act
        var files = ProvisionedConfigurationLayer.FindFiles(paths, new FakeProvisionedConfigurationFileSystem());

        // Assert
        Assert.Empty(files);
    }

    [Fact]
    public void FindFiles_MountedDirectory_LayersEveryJsonFileInOrdinalNameOrder()
    {
        // Arrange
        var fileSystem = new FakeProvisionedConfigurationFileSystem()
            .WithDirectory(MountPath, "persistence.json", "Accounts.json", "search.json");

        // Act
        var files = ProvisionedConfigurationLayer.FindFiles(new ProvisionedConfigurationPaths(MountPath, null), fileSystem);

        // Assert
        Assert.Equal(
            [MountedFile("Accounts.json"), MountedFile("persistence.json"), MountedFile("search.json")],
            files);
    }

    [Fact]
    public void FindFiles_MountedDirectory_SkipsEntriesThatAreNotJsonFiles()
    {
        // Arrange
        var fileSystem = new FakeProvisionedConfigurationFileSystem()
            .WithDirectory(MountPath, "settings.json", "notes.txt", "settings.json.bak", "README");

        // Act
        var files = ProvisionedConfigurationLayer.FindFiles(new ProvisionedConfigurationPaths(MountPath, null), fileSystem);

        // Assert
        Assert.Equal([MountedFile("settings.json")], files);
    }

    /// <summary>The atomic-update entries Kubernetes writes beside the keys are bookkeeping, never configuration.</summary>
    [Fact]
    public void FindFiles_MountedDirectory_SkipsKubernetesVolumeBookkeepingEntries()
    {
        // Arrange
        var fileSystem = new FakeProvisionedConfigurationFileSystem()
            .WithDirectory(MountPath, "..data", "..2026_07_31_10_15_00.1234.json", "settings.json");

        // Act
        var files = ProvisionedConfigurationLayer.FindFiles(new ProvisionedConfigurationPaths(MountPath, null), fileSystem);

        // Assert
        Assert.Equal([MountedFile("settings.json")], files);
    }

    /// <summary>A ConfigMap with no keys is a legitimate state during a rollout, not a reason to refuse to start.</summary>
    [Fact]
    public void FindFiles_EmptyMountedDirectory_LayersNoFileAndDoesNotThrow()
    {
        // Arrange
        var fileSystem = new FakeProvisionedConfigurationFileSystem().WithDirectory(MountPath);

        // Act
        var files = ProvisionedConfigurationLayer.FindFiles(new ProvisionedConfigurationPaths(MountPath, null), fileSystem);

        // Assert
        Assert.Empty(files);
    }

    [Fact]
    public void FindFiles_MountedFileBesideADirectory_LayersTheFileLast()
    {
        // Arrange
        var overridePath = "/etc/mailmcp/override.json";
        var fileSystem = new FakeProvisionedConfigurationFileSystem()
            .WithDirectory(MountPath, "settings.json")
            .WithFile(overridePath);

        // Act
        var files = ProvisionedConfigurationLayer.FindFiles(
            new ProvisionedConfigurationPaths(MountPath, overridePath),
            fileSystem);

        // Assert
        Assert.Equal([MountedFile("settings.json"), overridePath], files);
    }

    /// <summary>A mount that never arrived must stop the host rather than leave it running on defaults.</summary>
    [Fact]
    public void FindFiles_DirectoryThatDoesNotExist_FailsNamingTheConfigurationKeyAndThePath()
    {
        // Arrange
        var paths = new ProvisionedConfigurationPaths(MountPath, null);

        // Act
        var failure = Assert.Throws<ProvisionedConfigurationSourceInvalidException>(
            () => ProvisionedConfigurationLayer.FindFiles(paths, new FakeProvisionedConfigurationFileSystem()));

        // Assert
        Assert.Equal(MailMcpErrorCode.ProvisionedConfigurationSourceInvalid, failure.ErrorCode);
        Assert.Contains(ProvisionedConfigurationPaths.DirectoryKey, failure.Message, StringComparison.Ordinal);
        Assert.Contains(MountPath, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FindFiles_FileThatDoesNotExist_FailsNamingTheConfigurationKeyAndThePath()
    {
        // Arrange
        var missingPath = "/etc/mailmcp/override.json";
        var paths = new ProvisionedConfigurationPaths(null, missingPath);

        // Act
        var failure = Assert.Throws<ProvisionedConfigurationSourceInvalidException>(
            () => ProvisionedConfigurationLayer.FindFiles(paths, new FakeProvisionedConfigurationFileSystem()));

        // Assert
        Assert.Equal(MailMcpErrorCode.ProvisionedConfigurationSourceInvalid, failure.ErrorCode);
        Assert.Contains(ProvisionedConfigurationPaths.FileKey, failure.Message, StringComparison.Ordinal);
        Assert.Contains(missingPath, failure.Message, StringComparison.Ordinal);
    }

    /// <summary>An environment variable overrides a mounted file, which is what the insertion point decides.</summary>
    [Fact]
    public void FindInsertionIndex_HostSources_LandsBelowTheUnprefixedEnvironmentProvider()
    {
        // Arrange
        IReadOnlyList<IConfigurationSource> sources =
        [
            new EnvironmentVariablesConfigurationSource { Prefix = "DOTNET_" },
            new JsonConfigurationSource { Path = "appsettings.json" },
            new JsonConfigurationSource { Path = "appsettings.Production.json" },
            new EnvironmentVariablesConfigurationSource(),
            new MemoryConfigurationSource(),
        ];

        // Act
        var insertionIndex = ProvisionedConfigurationLayer.FindInsertionIndex(sources);

        // Assert
        Assert.Equal(3, insertionIndex);
    }

    /// <summary>Without an environment provider there is nothing to sit below, so provisioned files take precedence.</summary>
    [Fact]
    public void FindInsertionIndex_NoUnprefixedEnvironmentProvider_LandsAboveEverySource()
    {
        // Arrange
        IReadOnlyList<IConfigurationSource> sources =
        [
            new JsonConfigurationSource { Path = "appsettings.json" },
            new EnvironmentVariablesConfigurationSource { Prefix = "ASPNETCORE_" },
        ];

        // Act
        var insertionIndex = ProvisionedConfigurationLayer.FindInsertionIndex(sources);

        // Assert
        Assert.Equal(2, insertionIndex);
    }

    private static string MountedFile(string fileName) => Path.Combine(MountPath, fileName);
}
