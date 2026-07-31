// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

namespace MailFathom.Infrastructure.Security;

/// <summary>Whether the client a trust profile identifies has to present its certificate.</summary>
/// <remarks>
/// <para>
/// The distinction is about the request that arrives without a certificate at all, because that is the only one no
/// profile can be matched against. A deployment carrying at least one <see cref="Required" /> profile refuses such a
/// request; one whose profiles are all <see cref="Optional" /> serves it and simply identifies no client application.
/// </para>
/// <para>
/// <see cref="Optional" /> is therefore what a profile beside another authentication mechanism states: the ChatGPT
/// connector presents its managed certificate while a workstation reaches the same endpoint with an API key alone.
/// <see cref="Required" /> is what a deployment states once every client it serves holds a certificate.
/// </para>
/// </remarks>
public enum McpClientCertificateRequirement
{
    /// <summary>A request without a client certificate is served; a certificate that is presented is still validated against every profile.</summary>
    Optional = 0,

    /// <summary>A request without a client certificate is refused, whichever other credential it carries.</summary>
    Required = 1,
}
