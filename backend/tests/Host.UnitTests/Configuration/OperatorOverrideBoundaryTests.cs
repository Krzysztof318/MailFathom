// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.Provisioning;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.CommandLine;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration;

/// <summary>
/// Covers where the deployment's own sources stop and the operator's overrides begin. Both layers MailFathom adds — the
/// files a deployment provisioned and the settings it persisted — are inserted at this index, so the answer decides
/// whether an operator repairing a deployment can still beat what it configured.
/// </summary>
public sealed class OperatorOverrideBoundaryTests
{
    /// <summary>User Secrets is the lowest override, so both layers land below it rather than only below the environment.</summary>
    [Fact]
    public void FindIn_DevelopmentSources_LandsBelowUserSecrets()
    {
        // Arrange
        IReadOnlyList<IConfigurationSource> sources =
        [
            new EnvironmentVariablesConfigurationSource { Prefix = "DOTNET_" },
            new JsonConfigurationSource { Path = "appsettings.json" },
            new JsonConfigurationSource { Path = "appsettings.Development.json" },
            new JsonConfigurationSource { Path = "secrets.json" },
            new EnvironmentVariablesConfigurationSource(),
            new CommandLineConfigurationSource(),
        ];

        // Act
        var boundary = OperatorOverrideBoundary.FindIn(sources);

        // Assert
        Assert.Equal(3, boundary);
    }

    /// <summary>Outside Development no secret store is composed, so the unprefixed environment provider is the boundary.</summary>
    [Fact]
    public void FindIn_ProductionSources_LandsBelowTheUnprefixedEnvironmentProvider()
    {
        // Arrange
        IReadOnlyList<IConfigurationSource> sources =
        [
            new EnvironmentVariablesConfigurationSource { Prefix = "ASPNETCORE_" },
            new JsonConfigurationSource { Path = "appsettings.json" },
            new JsonConfigurationSource { Path = "appsettings.Production.json" },
            new EnvironmentVariablesConfigurationSource(),
            new CommandLineConfigurationSource(),
        ];

        // Act
        var boundary = OperatorOverrideBoundary.FindIn(sources);

        // Assert
        Assert.Equal(3, boundary);
    }

    /// <summary>
    /// A prefixed environment provider carries the host's own settings and is composed before the application's files,
    /// so treating one as the boundary would put both layers below <c>appsettings.json</c> instead of above it.
    /// </summary>
    [Fact]
    public void FindIn_PrefixedEnvironmentProvidersOnly_LandsAboveEverySource()
    {
        // Arrange
        IReadOnlyList<IConfigurationSource> sources =
        [
            new EnvironmentVariablesConfigurationSource { Prefix = "DOTNET_" },
            new JsonConfigurationSource { Path = "appsettings.json" },
        ];

        // Act
        var boundary = OperatorOverrideBoundary.FindIn(sources);

        // Assert
        Assert.Equal(2, boundary);
    }

    /// <summary>A JSON source is only the secret store when it is the file the framework layers that store in under.</summary>
    [Fact]
    public void FindIn_JsonSourceNamedForItsOwnFile_IsNotMistakenForTheSecretStore()
    {
        // Arrange
        IReadOnlyList<IConfigurationSource> sources =
        [
            new JsonConfigurationSource { Path = "appsettings.json" },
            new JsonConfigurationSource { Path = "/etc/mailfathom/config/secrets.json.bak" },
            new JsonConfigurationSource { Path = "/etc/mailfathom/config/10-secrets.json" },
        ];

        // Act
        var boundary = OperatorOverrideBoundary.FindIn(sources);

        // Assert
        Assert.Equal(3, boundary);
    }

    /// <summary>
    /// A provisioned file resolves to a bare file name, so a deployment that named one <c>secrets.json</c> would reach
    /// the name comparison looking exactly like the secret store. The layer that inserted it is what settles it, and
    /// the persisted layer therefore still lands above the file rather than below it.
    /// </summary>
    [Fact]
    public void FindIn_ProvisionedFileNamedLikeTheSecretStore_IsStillTheDeploymentsOwn()
    {
        // Arrange
        IReadOnlyList<IConfigurationSource> sources =
        [
            new JsonConfigurationSource { Path = "appsettings.json" },
            new ProvisionedJsonConfigurationSource { Path = "secrets.json" },
            new EnvironmentVariablesConfigurationSource(),
        ];

        // Act
        var boundary = OperatorOverrideBoundary.FindIn(sources);

        // Assert
        Assert.Equal(2, boundary);
    }

    /// <summary>
    /// Command-line arguments are an operator's override and are still not the boundary, because the boundary is the
    /// lowest override rather than any of them. A host that composed the command line before its own files would
    /// otherwise place both layers beneath those files, which is the inversion the boundary exists to prevent.
    /// </summary>
    [Fact]
    public void FindIn_CommandLineComposedBeforeTheApplicationsFiles_IsStillNotTheBoundary()
    {
        // Arrange
        IReadOnlyList<IConfigurationSource> sources =
        [
            new CommandLineConfigurationSource(),
            new JsonConfigurationSource { Path = "appsettings.json" },
            new EnvironmentVariablesConfigurationSource(),
        ];

        // Act
        var boundary = OperatorOverrideBoundary.FindIn(sources);

        // Assert
        Assert.Equal(2, boundary);
    }
}
