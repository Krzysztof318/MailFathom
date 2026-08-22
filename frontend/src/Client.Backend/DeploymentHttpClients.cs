// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Backend;

/// <summary>The two transports this assembly registers, named so the sign-in can ask for each by what it is for.</summary>
/// <remarks>
/// Two rather than one, because they aim at two machines with two sets of settings. The deployment's transport carries
/// the base address the host stated and the handler that attaches this run's access token; the authorization server's
/// carries neither — every address it is given is absolute and derived from the issuer the deployment named, and a
/// bearer token issued for MailFathom is not something to present to somebody's identity provider.
/// </remarks>
internal static class DeploymentHttpClients
{
    /// <summary>The transport aimed at the deployment, whose base address the composing host supplied.</summary>
    internal const string Deployment = "MailFathom.Deployment";

    /// <summary>The transport used for the authorization server's own discovery and token endpoints.</summary>
    internal const string AuthorizationServer = "MailFathom.AuthorizationServer";
}
