// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.UnitTests;

public sealed class ClientBuildTests
{
    /// <summary>Both assemblies are stamped from Version.props at the repository root, so a client reporting a different number from the suite built beside it would mean the import in frontend/Directory.Build.props had been lost.</summary>
    [Fact]
    public void FromAssembly_TheClientAssembly_ReportsTheVersionTheBuildDeclares()
    {
        // Arrange
        var expected = ClientBuild.FromAssembly(typeof(ClientBuildTests).Assembly);

        // Act
        var build = ClientBuild.FromAssembly(typeof(App).Assembly);

        // Assert
        Assert.NotEmpty(build.Version);
        Assert.Equal(expected.Version, build.Version);
    }

    /// <summary>Continuous integration appends the commit as a build metadata suffix, which names the build rather than the release.</summary>
    [Fact]
    public void FromAssembly_ABuildStampedWithARevision_ReportsTheReleaseRatherThanTheCommit()
    {
        // Arrange
        var clientAssembly = typeof(App).Assembly;

        // Act
        var build = ClientBuild.FromAssembly(clientAssembly);

        // Assert
        Assert.DoesNotContain("+", build.Version, StringComparison.Ordinal);
    }

    /// <summary>The client and the service are one product, and both read the name from the same import.</summary>
    [Fact]
    public void FromAssembly_TheClientAssembly_ReportsTheProductBothStacksShip()
    {
        // Arrange
        var clientAssembly = typeof(App).Assembly;

        // Act
        var build = ClientBuild.FromAssembly(clientAssembly);

        // Assert
        Assert.Equal("MailFathom", build.Product);
    }
}
