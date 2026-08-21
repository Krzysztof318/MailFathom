// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.Endpoints;

/// <summary>Which surfaces a listener serves.</summary>
/// <remarks>
/// A set rather than one value, because a deployment may put two or three surfaces on one socket, and what a request
/// arriving there may ask for is then the union of what those surfaces answer. The isolation middlewares read this from
/// the port a connection was accepted on, which is a property of the socket the operating system accepted it on and
/// therefore something a caller cannot state, spoof, or forward.
/// </remarks>
[Flags]
internal enum ServedSurfaces
{
    /// <summary>No surface, which is the state of a port this process does not bind.</summary>
    None = 0,

    /// <summary>The MCP protocol surface, and every path no other surface claims.</summary>
    Mcp = 1,

    /// <summary>The administrative surface beneath its route prefix, and its protected resource metadata document.</summary>
    Admin = 2,

    /// <summary>The startup, readiness, and liveness probes.</summary>
    Probes = 4,
}
