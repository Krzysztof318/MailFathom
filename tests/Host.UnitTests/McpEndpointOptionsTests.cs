// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Host.Configuration;
using Xunit;

namespace MailMcp.Host.UnitTests;

/// <summary>Covers the one decision the MCP endpoint section carries.</summary>
/// <remarks>
/// The section answers whether the surface is served and nothing else: the path is a constant the protocol surface
/// publishes and the transport is always stateless, so neither can be misconfigured and neither needs validating. What
/// remains worth a test is the default, because it is the security decision — a deployment that configures nothing must
/// serve no mailbox over the network.
/// </remarks>
public sealed class McpEndpointOptionsTests
{
    [Fact]
    public void Enabled_UnconfiguredDeployment_ServesNoMcpEndpoint()
    {
        // Arrange, Act
        var options = new McpEndpointOptions();

        // Assert
        Assert.False(options.Enabled);
    }
}
