// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.UnitTests;

public class ClientBuildTests
{
    [Fact]
    public void FromAssembly_TheClientAssembly_ReportsTheVersionTheBuildDeclares()
    {
        // Both assemblies are stamped from Version.props at the repository root, so a client reporting a different
        // number from the suite built beside it would mean the import in frontend/Directory.Build.props had been lost.
        var expected = ClientBuild.FromAssembly(typeof(ClientBuildTests).Assembly);

        var build = ClientBuild.FromAssembly(typeof(App).Assembly);

        Assert.NotEmpty(build.Version);
        Assert.Equal(expected.Version, build.Version);
    }

    [Fact]
    public void FromAssembly_ABuildStampedWithARevision_ReportsTheReleaseRatherThanTheCommit()
    {
        // Continuous integration appends the commit as `+<sha>`, which names the build rather than the release.
        var build = ClientBuild.FromAssembly(typeof(App).Assembly);

        Assert.DoesNotContain("+", build.Version, StringComparison.Ordinal);
    }

    [Fact]
    public void FromAssembly_TheClientAssembly_ReportsTheProductBothStacksShip()
    {
        var build = ClientBuild.FromAssembly(typeof(App).Assembly);

        Assert.Equal("MailFathom", build.Product);
    }
}
