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
/// <para>
/// One implementation here is not a head's answer at all: <see cref="BuildStatedDeploymentAddress" /> carries what the
/// build stated and wraps the head's own source, which is the case where neither of the two answers above exists —
/// a head an orchestration started, served from a socket of its own beside the service. <c>App</c> applies it, so a
/// new head still owes exactly one implementation of this interface.
/// </para>
/// <para>
/// What none of them is, is the last word. A person's own choice is read before any of these and outlives every
/// restart — <see cref="DeploymentChoice" /> is where the two meet — so a source here answers what this head knows
/// before anybody has said anything.
/// </para>
/// </remarks>
internal interface IDeploymentAddressSource
{
    /// <summary>Resolves the deployment's base address for this head.</summary>
    /// <param name="settings">What the installation stated, which a head is free to have no use for.</param>
    /// <returns>The absolute address every route is resolved against, or <see langword="null" /> where this head has nothing to say.</returns>
    /// <exception cref="InvalidOperationException">Thrown when something did state an address and it is not one anything could be reached at.</exception>
    /// <remarks>
    /// Nothing to say and something unusable to say are deliberately different answers. A head nobody has configured is
    /// the ordinary state of a first run, and the client asks a person rather than failing; a head configured with an
    /// address that cannot be parsed was configured wrongly, and quietly asking as though nothing had been written
    /// would leave whoever wrote it with no way to find out it was ignored.
    /// </remarks>
    Uri? Resolve(DeploymentSettings settings);
}
