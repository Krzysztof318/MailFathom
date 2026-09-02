// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Security.Transport;

/// <summary>One clear-text listener, and the HTTPS ports the domains it redirects to are published on.</summary>
/// <param name="Port">The TCP port the clear-text listener binds.</param>
/// <param name="PublishedDomainPorts">The HTTPS port each domain this surface publishes is served on, keyed without regard to case.</param>
/// <remarks>
/// One surface contributes one of these, because a surface has one clear-text listener and several HTTPS profiles. The
/// domains are carried rather than a single target, so several domains sharing one listener each redirect to themselves:
/// resolving one target for the listener would send every client to whichever domain configuration happened to name
/// first, which is a domain the client never asked for and a certificate it may not accept.
/// </remarks>
internal sealed record ClearTextRedirectListener(int Port, IReadOnlyDictionary<string, int> PublishedDomainPorts);
