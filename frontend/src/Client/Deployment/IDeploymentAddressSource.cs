// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Deployment;

/// <summary>Answers where this head's deployment is, which is the one part of composition the heads disagree about.</summary>
/// <remarks>
/// A seam rather than a branch on the running platform, for the reason <c>ISignInRedirectListener</c> is one: the two
/// answers are genuinely different mechanisms rather than two values of one. A head that is installed reads what
/// somebody wrote; a head that was downloaded from the deployment already knows, because it is the deployment that
/// served it. Composition takes whichever the head handed it and knows about neither.
/// </remarks>
internal interface IDeploymentAddressSource
{
    /// <summary>Resolves the deployment's base address for this head.</summary>
    /// <param name="settings">What the installation stated, which a head is free to have no use for.</param>
    /// <returns>The absolute address every route is resolved against.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the head needs a stated address and the installation states none it can use.</exception>
    Uri Resolve(DeploymentSettings settings);
}
