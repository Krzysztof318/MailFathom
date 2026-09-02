// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Common;
using MailFathom.Mcp.UnitTests.TestDoubles;
using MailFathom.Versioning;
using Xunit;

namespace MailFathom.Mcp.UnitTests;

/// <summary>
/// Covers what the protocol surface says about itself when a client initializes a session. It is one of the two places
/// a MailFathom build is observable at run time, and the only one a client can reach.
/// </summary>
public sealed class McpServerIdentityTests
{
    /// <summary>
    /// How long a hint naming one address can honestly be. The bound is what would catch documentation content being
    /// moved into the handshake — the pages belong on the site, and instructions a client may put in front of a model
    /// are the wrong place to serve them from.
    /// </summary>
    private const int OneSentenceAndAnAddress = 200;

    [Fact]
    public void ServerInfo_ComposedSurface_NamesTheProductRatherThanTheHostAssembly()
    {
        // Act
        var serverInfo = RegisteredMcpToolSurface.ServerInfo();

        // Assert
        Assert.NotNull(serverInfo);
        Assert.Equal("MailFathom", serverInfo.Name);
    }

    /// <summary>
    /// The expectation is read from the protocol assembly's own build-time metadata rather than restated as a literal,
    /// so a registration that regressed to a hardcoded version — one that would stay plausible while the declared
    /// version moved on — fails here instead of reporting a build that is not running.
    /// </summary>
    [Fact]
    public void ServerInfo_ComposedSurface_ReportsTheVersionStampedIntoTheProtocolAssembly()
    {
        // Arrange
        var stamped = StampedAssemblyVersion.ReadFrom(typeof(McpServiceCollectionExtensions).Assembly);

        // Act
        var serverInfo = RegisteredMcpToolSurface.ServerInfo();

        // Assert
        Assert.NotNull(serverInfo);
        Assert.Equal(stamped.Version, serverInfo.Version);
    }

    /// <summary>
    /// A client that connected over MCP may be the only way its user meets MailFathom, so the handshake is where an
    /// agent learns where to read about the deployment it is talking to. The address is derived from the version the
    /// same handshake reports, which is what keeps the pages named here describing the build in front of the client.
    /// </summary>
    [Fact]
    public void ServerInstructions_ComposedSurface_NameTheDocumentationForTheVersionItReports()
    {
        // Arrange
        var stamped = StampedAssemblyVersion.ReadFrom(typeof(McpServiceCollectionExtensions).Assembly);
        var expectedAddress = DocumentationAddress.ForVersion(stamped.Version);

        // Act
        var instructions = RegisteredMcpToolSurface.ServerInstructions();

        // Assert
        Assert.NotNull(expectedAddress);
        Assert.NotNull(instructions);
        Assert.Contains(expectedAddress, instructions, StringComparison.Ordinal);
    }

    /// <summary>
    /// Instructions are a hint a client may put in front of a model, so what belongs in them is where to read rather
    /// than what to read: the pages themselves stay on the site, and this surface serves mail.
    /// </summary>
    [Fact]
    public void ServerInstructions_ComposedSurface_CarryOneSentenceAndNoDocumentationOfTheirOwn()
    {
        // Act
        var instructions = RegisteredMcpToolSurface.ServerInstructions();

        // Assert
        Assert.NotNull(instructions);
        Assert.EndsWith(".", instructions, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', instructions);
        Assert.InRange(instructions.Length, 1, OneSentenceAndAnAddress);
    }

    /// <summary>
    /// The revision belongs to build provenance rather than to the compatibility statement a client reads, so it stays
    /// out of what the handshake publishes even though the same metadata carries it.
    /// </summary>
    [Fact]
    public void ServerInfo_ComposedSurface_PublishesNoSourceControlBuildMetadata()
    {
        // Act
        var serverInfo = RegisteredMcpToolSurface.ServerInfo();

        // Assert
        Assert.NotNull(serverInfo);
        Assert.NotNull(serverInfo.Version);
        Assert.DoesNotContain("+", serverInfo.Version, StringComparison.Ordinal);
    }
}
