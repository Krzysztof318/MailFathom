// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Session;

/// <summary>What the client is doing about reaching its deployment, said in enough detail to put on a screen.</summary>
/// <param name="Standing">Whether the deployment is being reached, was reached, or has stopped answering.</param>
/// <param name="Attempt">Which attempt is under way or was the last made, counting from one.</param>
/// <param name="Attempts">How many attempts this client makes before it stops and asks.</param>
/// <remarks>
/// <para>
/// The attempt numbers are on the record rather than kept beside it because the point of showing them is that a person
/// can see the client working. An application that has lost its connection and says nothing is one somebody restarts;
/// one that says which attempt it is on is one they wait for.
/// </para>
/// <para>
/// Both readings a view binds are stated as affirmatives, for the reason <c>SessionStanding</c> states them that way:
/// a value that has not arrived reaches a binding as its type's default, so a control shown on the absence of an
/// answer is a control shown before there is one.
/// </para>
/// </remarks>
public sealed record DeploymentConnection(ConnectionStanding Standing, int Attempt, int Attempts)
{
    /// <summary>Gets whether the client is trying again after an attempt that did not arrive.</summary>
    /// <remarks>
    /// The first attempt is deliberately not one: every read of the session starts with one, and a banner saying the
    /// connection is being retried would then be on the screen for every ordinary start.
    /// </remarks>
    public bool IsRetrying => this.Standing is ConnectionStanding.Reaching && this.Attempt > 1;

    /// <summary>Gets whether the client has stopped trying on its own.</summary>
    public bool IsLost => this.Standing is ConnectionStanding.Lost;

    /// <summary>Gets whether the deployment answered, which is the fact every per-account reading rests on.</summary>
    public bool IsReached => this.Standing is ConnectionStanding.Reached;
}
