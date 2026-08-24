// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Backend;

/// <summary>The three transports this assembly registers, named so each caller can ask for the one it is entitled to.</summary>
/// <remarks>
/// Three rather than one, because they aim at three kinds of machine under three sets of terms. The deployment's
/// transport is aimed at wherever the client is currently pointed and carries the handler that attaches this run's
/// access token; the authorization server's carries neither a base address nor a token — every address it is given is
/// absolute and derived from the issuer the deployment named, and a bearer token issued for MailFathom is not something
/// to present to somebody's identity provider; and the probe's carries no token either, because it is aimed at an
/// address nobody has vouched for yet and is how this client finds out what is there.
/// </remarks>
internal static class DeploymentHttpClients
{
    /// <summary>The transport aimed at the deployment this client is pointed at.</summary>
    internal const string Deployment = "MailFathom.Deployment";

    /// <summary>The transport used for the authorization server's own discovery and token endpoints.</summary>
    internal const string AuthorizationServer = "MailFathom.AuthorizationServer";

    /// <summary>The transport a candidate address is asked on, before anything is pointed at it.</summary>
    internal const string DeploymentProbe = "MailFathom.DeploymentProbe";
}
