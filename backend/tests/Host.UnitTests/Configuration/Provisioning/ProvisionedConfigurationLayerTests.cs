// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Failures;
using MailFathom.Host.Configuration.Provisioning;
using MailFathom.Host.UnitTests.TestDoubles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Configuration.Memory;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Provisioning;

/// <summary>Covers which provisioned configuration files are layered in, and where among the host's own sources.</summary>
/// <remarks>
/// Both decisions are contracts an operator reasons about: which files a mounted ConfigMap contributes, and whether an
/// environment variable still overrides them. Neither needs a directory on disk to be proven, which is what keeps them
/// here rather than in the integration suite.
/// </remarks>
public sealed class ProvisionedConfigurationLayerTests
{
    private const string MountPath = "/etc/mailfathom/config";

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
    public void FindFiles_MountedDirectory_SkipsEntriesThatAreNeitherJsonNorYaml()
    {
        // Arrange
        var fileSystem = new FakeProvisionedConfigurationFileSystem()
            .WithDirectory(MountPath, "settings.json", "notes.txt", "settings.json.bak", "values.yaml.orig", "README");

        // Act
        var files = ProvisionedConfigurationLayer.FindFiles(new ProvisionedConfigurationPaths(MountPath, null), fileSystem);

        // Assert
        Assert.Equal([MountedFile("settings.json")], files);
    }

    /// <summary>JSON and YAML files interleave by their whole names, so an operator orders them by naming them.</summary>
    [Fact]
    public void FindFiles_MountedDirectoryMixingFormats_LayersThemByNameAndReadsEachInItsOwnFormat()
    {
        // Arrange
        var fileSystem = new FakeProvisionedConfigurationFileSystem()
            .WithDirectory(MountPath, "30-search.yml", "10-mail.YAML", "20-persistence.json");

        // Act
        var files = ProvisionedConfigurationLayer.FindFiles(new ProvisionedConfigurationPaths(MountPath, null), fileSystem);

        // Assert
        Assert.Equal(
            [
                MountedFile("10-mail.YAML", ProvisionedConfigurationFormat.Yaml),
                MountedFile("20-persistence.json"),
                MountedFile("30-search.yml", ProvisionedConfigurationFormat.Yaml),
            ],
            files);
    }

    /// <summary>Two files at the same place in the order would be ordered by their extension, which nobody chose.</summary>
    [Theory]
    [InlineData("10-mail.json", "10-mail.yaml")]
    [InlineData("10-mail.yaml", "10-mail.yml")]
    [InlineData("10-mail.json", "10-mail.JSON")]
    public void FindFiles_FilesWhoseNamesDifferOnlyByExtension_FailsNamingBoth(string first, string second)
    {
        // Arrange
        var fileSystem = new FakeProvisionedConfigurationFileSystem()
            .WithDirectory(MountPath, first, "20-search.json", second);

        // Act
        var failure = Assert.Throws<ProvisionedConfigurationSourceInvalidException>(
            () => ProvisionedConfigurationLayer.FindFiles(new ProvisionedConfigurationPaths(MountPath, null), fileSystem));

        // Assert
        Assert.Equal(MailFathomErrorCode.ProvisionedConfigurationSourceInvalid, failure.ErrorCode);
        Assert.Contains(first, failure.Message, StringComparison.Ordinal);
        Assert.Contains(second, failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("20-search.json", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/etc/mailfathom/override.yaml", true)]
    [InlineData("/etc/mailfathom/override.yml", true)]
    [InlineData("/etc/mailfathom/override.json", false)]
    public void FindFiles_MountedFile_IsReadInTheFormatItsExtensionNames(string filePath, bool readAsYaml)
    {
        // Arrange
        var fileSystem = new FakeProvisionedConfigurationFileSystem().WithFile(filePath);

        // Act
        var files = ProvisionedConfigurationLayer.FindFiles(new ProvisionedConfigurationPaths(null, filePath), fileSystem);

        // Assert
        var expectedFormat = readAsYaml ? ProvisionedConfigurationFormat.Yaml : ProvisionedConfigurationFormat.Json;
        Assert.Equal([new ProvisionedConfigurationFile(filePath, expectedFormat)], files);
    }

    /// <summary>A file whose extension names neither format would otherwise be read under a guess.</summary>
    [Fact]
    public void FindFiles_MountedFileThatIsNeitherJsonNorYaml_FailsNamingTheConfigurationKeyAndThePath()
    {
        // Arrange
        const string filePath = "/etc/mailfathom/override.conf";
        var fileSystem = new FakeProvisionedConfigurationFileSystem().WithFile(filePath);

        // Act
        var failure = Assert.Throws<ProvisionedConfigurationSourceInvalidException>(
            () => ProvisionedConfigurationLayer.FindFiles(new ProvisionedConfigurationPaths(null, filePath), fileSystem));

        // Assert
        Assert.Contains(ProvisionedConfigurationPaths.FileKey, failure.Message, StringComparison.Ordinal);
        Assert.Contains(filePath, failure.Message, StringComparison.Ordinal);
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
        var overridePath = "/etc/mailfathom/override.json";
        var fileSystem = new FakeProvisionedConfigurationFileSystem()
            .WithDirectory(MountPath, "settings.json")
            .WithFile(overridePath);

        // Act
        var files = ProvisionedConfigurationLayer.FindFiles(
            new ProvisionedConfigurationPaths(MountPath, overridePath),
            fileSystem);

        // Assert
        Assert.Equal(
            [MountedFile("settings.json"), new ProvisionedConfigurationFile(overridePath, ProvisionedConfigurationFormat.Json)],
            files);
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
        Assert.Equal(MailFathomErrorCode.ProvisionedConfigurationSourceInvalid, failure.ErrorCode);
        Assert.Contains(ProvisionedConfigurationPaths.DirectoryKey, failure.Message, StringComparison.Ordinal);
        Assert.Contains(MountPath, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FindFiles_FileThatDoesNotExist_FailsNamingTheConfigurationKeyAndThePath()
    {
        // Arrange
        var missingPath = "/etc/mailfathom/override.json";
        var paths = new ProvisionedConfigurationPaths(null, missingPath);

        // Act
        var failure = Assert.Throws<ProvisionedConfigurationSourceInvalidException>(
            () => ProvisionedConfigurationLayer.FindFiles(paths, new FakeProvisionedConfigurationFileSystem()));

        // Assert
        Assert.Equal(MailFathomErrorCode.ProvisionedConfigurationSourceInvalid, failure.ErrorCode);
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

    /// <summary>
    /// The developer's secret store is one of the operator's overrides, so a provisioned file sits below it rather
    /// than above. That is the direction the persisted settings layer is inserted into as well: it lands between these
    /// files and that store.
    /// </summary>
    [Fact]
    public void FindInsertionIndex_UserSecretsComposed_LandsBelowIt()
    {
        // Arrange
        IReadOnlyList<IConfigurationSource> sources =
        [
            new JsonConfigurationSource { Path = "appsettings.json" },
            new JsonConfigurationSource { Path = "appsettings.Development.json" },
            new JsonConfigurationSource { Path = "secrets.json" },
            new EnvironmentVariablesConfigurationSource(),
        ];

        // Act
        var insertionIndex = ProvisionedConfigurationLayer.FindInsertionIndex(sources);

        // Assert
        Assert.Equal(2, insertionIndex);
    }

    private static ProvisionedConfigurationFile MountedFile(
        string fileName,
        ProvisionedConfigurationFormat format = ProvisionedConfigurationFormat.Json) =>
        new(Path.Combine(MountPath, fileName), format);
}
