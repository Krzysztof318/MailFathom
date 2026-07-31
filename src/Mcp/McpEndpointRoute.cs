// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

namespace MailFathom.Mcp;

/// <summary>Names where the MailFathom protocol surface answers.</summary>
/// <remarks>
/// The route is a constant rather than a setting. An MCP client is configured with a server URL, so a deployment that
/// could move the path would only be able to move it in step with every client pointed at it — the configurability would
/// buy nothing and cost one more way for a deployment to be reachable somewhere nobody is looking. Publishing it here
/// keeps the surface's own address with the surface, and leaves mapping it a decision the host still makes.
/// </remarks>
public static class McpEndpointRoute
{
    /// <summary>The path the Streamable HTTP transport is mapped on.</summary>
    public const string Path = "/mcp";
}
