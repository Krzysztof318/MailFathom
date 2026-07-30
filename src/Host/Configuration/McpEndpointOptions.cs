// Copyright © 2026 Krzysztof Kasprowicz

using System.Diagnostics.CodeAnalysis;

namespace MailMcp.Host.Configuration;

/// <summary>Configures whether the MCP protocol surface is served.</summary>
/// <remarks>
/// <para>
/// Whether is the only question this section answers. The path is a constant published by the protocol surface, and the
/// transport is always stateless, because every MailMcp tool answers one request from the local mailbox copy and needs
/// no server-initiated message — which is the shape MCP deployments take today. Should a tool that pushes notifications
/// ever need sessions, that is a change to the surface rather than a knob an operator was expected to find.
/// </para>
/// <para>
/// The endpoint is disabled by default, so a deployment that configures nothing serves no mailbox over the network. That
/// default is a security decision rather than a convenience: until the OAuth 2.1 work lands, the endpoint carries no
/// transport authentication, and anything that can reach it can read mail.
/// </para>
/// <para>
/// The value is read once, while the host is being composed, because whether an endpoint exists is part of the
/// application's routing rather than something a request re-reads. A change takes effect on restart; the setting
/// deliberately does not participate in configuration reload.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class McpEndpointOptions
{
    /// <summary>The configuration section the endpoint settings are bound from.</summary>
    public const string SectionName = "McpEndpoint";

    /// <summary>Gets or sets whether the MCP endpoint is served at all.</summary>
    /// <remarks>Disabled unless a deployment states otherwise, so reaching a mailbox over MCP is always something an operator turned on.</remarks>
    public bool Enabled { get; set; }
}
