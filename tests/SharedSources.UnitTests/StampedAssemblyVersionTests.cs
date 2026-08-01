// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System.Reflection;
using MailFathom.Versioning;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>
/// Covers the split every assembly that reports its own identity depends on. A fault here misreports the version in
/// the host's startup record and in what the MCP surface tells a client during initialization at the same time.
/// </summary>
public sealed class StampedAssemblyVersionTests
{
    [Fact]
    public void Parse_VersionCarryingASourceRevision_SeparatesTheVersionFromTheRevision()
    {
        // Act
        var stamped = StampedAssemblyVersion.Parse("0.2.0+3f1c9ab");

        // Assert
        Assert.Equal("0.2.0", stamped.Version);
        Assert.Equal("3f1c9ab", stamped.Revision);
    }

    /// <summary>
    /// A prerelease identifier is part of the version rather than provenance, so it must stay on the left of the split.
    /// This is the shape every nightly build carries, per ADR 0004.
    /// </summary>
    [Fact]
    public void Parse_PrereleaseVersionCarryingASourceRevision_KeepsThePrereleaseIdentifierWithTheVersion()
    {
        // Act
        var stamped = StampedAssemblyVersion.Parse("0.2.0-nightly.41+3f1c9ab");

        // Assert
        Assert.Equal("0.2.0-nightly.41", stamped.Version);
        Assert.Equal("3f1c9ab", stamped.Revision);
    }

    /// <summary>
    /// A build with no repository in its context and no revision supplied to it — the container build, before this
    /// repository's Dockerfile passes one through — stamps no revision, which is a legitimate state rather than a fault.
    /// </summary>
    [Fact]
    public void Parse_VersionWithoutASourceRevision_ReportsTheVersionAndAnUnknownRevision()
    {
        // Act
        var stamped = StampedAssemblyVersion.Parse("0.2.0");

        // Assert
        Assert.Equal("0.2.0", stamped.Version);
        Assert.Equal(StampedAssemblyVersion.Unknown, stamped.Revision);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_NothingStamped_ReportsBothPartsAsUnknown(string? informationalVersion)
    {
        // Act
        var stamped = StampedAssemblyVersion.Parse(informationalVersion);

        // Assert
        Assert.Equal(StampedAssemblyVersion.Unknown, stamped.Version);
        Assert.Equal(StampedAssemblyVersion.Unknown, stamped.Revision);
    }

    [Theory]
    [InlineData("0.2.0+")]
    [InlineData("0.2.0+   ")]
    public void Parse_SeparatorWithNothingAfterIt_ReportsAnUnknownRevisionRatherThanAnEmptyOne(string informationalVersion)
    {
        // Act
        var stamped = StampedAssemblyVersion.Parse(informationalVersion);

        // Assert
        Assert.Equal("0.2.0", stamped.Version);
        Assert.Equal(StampedAssemblyVersion.Unknown, stamped.Revision);
    }

    /// <summary>
    /// The expectation is derived from the same attribute the reader consults rather than restated as a literal, so
    /// this asserts the reading path and not whichever version the repository happens to declare today.
    /// </summary>
    [Fact]
    public void ReadFrom_AStampedAssembly_ReportsWhatItsInformationalVersionAttributeCarries()
    {
        // Arrange
        var assembly = typeof(StampedAssemblyVersionTests).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        // Act
        var stamped = StampedAssemblyVersion.ReadFrom(assembly);

        // Assert
        Assert.Equal(StampedAssemblyVersion.Parse(informationalVersion), stamped);
        Assert.DoesNotContain("+", stamped.Version, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadFrom_NoAssembly_RejectsTheCall()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => StampedAssemblyVersion.ReadFrom(null!));
    }
}
