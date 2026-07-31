// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System.Net;

namespace MailFathom.Host.Configuration;

/// <summary>The socket one or more HTTPS profiles bind.</summary>
/// <param name="Address">The IP address the listener binds.</param>
/// <param name="Port">The TCP port the listener binds.</param>
/// <remarks>
/// It exists so that "which profiles share a listener" is answered by comparing one value rather than by comparing two
/// fields wherever the question comes up. Profiles that share it are told apart by the server name a client sends;
/// everything a listener owns rather than a connection — the socket and the set of HTTP versions — is therefore common
/// to all of them.
/// </remarks>
internal readonly record struct McpHttpsListenerAddress(IPAddress Address, int Port);
