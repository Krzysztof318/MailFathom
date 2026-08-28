// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Backend;

/// <summary>The three transports this assembly registers, named so each caller can ask for the one it is entitled to.</summary>
/// <remarks>
/// Three rather than one, because they carry the session's credential under three different sets of terms. The
/// deployment's transport is aimed at wherever the client is currently pointed and carries the handler that presents
/// whoever is signed in; the sign-in's carries no ambient credential, because the whole of what it sends is one
/// candidate offered explicitly, and mixing the two would present the running session on an attempt that is meant to
/// prove another; and the probe's carries none either, because it is aimed at an address nobody has vouched for yet
/// and is how this client finds out what is there.
/// </remarks>
internal static class DeploymentHttpClients
{
    /// <summary>The transport aimed at the deployment this client is pointed at.</summary>
    internal const string Deployment = "MailFathom.Deployment";

    /// <summary>The transport one candidate credential is offered on, carrying no ambient one.</summary>
    internal const string SignIn = "MailFathom.SignIn";

    /// <summary>The transport a candidate address is asked on, before anything is pointed at it.</summary>
    internal const string DeploymentProbe = "MailFathom.DeploymentProbe";
}
