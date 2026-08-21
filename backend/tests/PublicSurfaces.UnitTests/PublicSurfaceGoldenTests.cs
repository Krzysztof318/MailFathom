// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Xunit;

namespace MailFathom.PublicSurfaces.UnitTests;

/// <summary>Holds the two public surfaces nothing else records against the files committed beside this suite.</summary>
/// <remarks>
/// <para>
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0004-versioning-and-release-policy.md">ADR 0004</see>
/// permits a break on any of the four public surfaces below <c>1.0.0</c> and makes the record of the break the
/// obligation instead. The database schema already has a test that proves what a release ships, and the deployment
/// contract is read by the assets that render it; the tool contract and the configuration key set had nothing but the
/// author's memory between a rename and a release note that never mentioned it.
/// </para>
/// <para>
/// So the whole of each surface is rendered into one ordered file, and a change to it is a diff in the pull request
/// rather than something a reviewer has to reconstruct from twenty tool classes. Neither test judges whether a change
/// is right — a break is permitted here and the golden file is where it is written down.
/// </para>
/// </remarks>
public sealed class PublicSurfaceGoldenTests
{
    /// <summary>Every tool descriptor a client discovers, as the registration publishes it.</summary>
    [Fact]
    public void McpToolContract_AsTheRegistrationPublishesIt_MatchesTheCommittedRecord() =>
        PublicSurfaceGolden.AssertMatches(
            "mcp-tool-contract.json",
            "published MCP tool contract",
            McpToolContractSurface.Render());

    /// <summary>Every configuration key an operator may write, as the host binds it.</summary>
    [Fact]
    public void ConfigurationKeySet_AsTheHostBindsIt_MatchesTheCommittedRecord() =>
        PublicSurfaceGolden.AssertMatches(
            "configuration-keys.txt",
            "published configuration key set",
            ConfigurationKeySurface.Render());

    /// <summary>Two renderings of one surface are the same text, which is what makes a diff mean a change.</summary>
    /// <remarks>
    /// The control for both tests above. Reflection promises no ordering, a schema generator none either, and a
    /// rendering that varied between runs would fail on a tree nobody touched and pass on one somebody broke — the
    /// failure a golden file is least able to report about itself.
    /// </remarks>
    [Fact]
    public void PublicSurfaces_RenderedTwice_ProduceTheSameText()
    {
        // Arrange, Act
        var tools = McpToolContractSurface.Render();
        var keys = ConfigurationKeySurface.Render();

        // Assert
        Assert.Equal(tools, McpToolContractSurface.Render(), StringComparer.Ordinal);
        Assert.Equal(keys, ConfigurationKeySurface.Render(), StringComparer.Ordinal);
    }
}
