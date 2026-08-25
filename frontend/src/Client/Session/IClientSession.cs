// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Session;

/// <summary>What the deployment made of this client, as state every screen reads instead of asking for itself.</summary>
/// <remarks>
/// <para>
/// One for the run rather than one per model, for the reason the workspace is: a model is discarded as its view is
/// navigated away from, and a session fetched per screen would be a request per screen and an answer per screen to
/// disagree about. It is fetched once, on the first read, and again whenever the answer stops describing this session.
/// </para>
/// <para>
/// A feed rather than a value, because fetching it can be under way and can fail. A screen that depends on a grant
/// renders the three axes of this feed — what it is doing, what went wrong, and what it found — rather than assuming
/// the session is there.
/// </para>
/// </remarks>
public interface IClientSession
{
    /// <summary>Gets what may be offered here, and the version of the deployment that said so.</summary>
    IFeed<SessionStanding> Standing { get; }

    /// <summary>Gets whether the deployment can be reached at all, and what the client is doing about it when it cannot.</summary>
    /// <remarks>
    /// Beside the standing rather than folded into it, because they answer different questions and a screen acts on
    /// them differently. A deployment that refused this credential was reached; a deployment nothing answered from was
    /// not, and only the second is something the client recovers from by itself. It is also what keeps a lost
    /// connection from reading as mail that is out of date — the two are separate sentences on a screen because they
    /// are separate facts here.
    /// </remarks>
    IFeed<DeploymentConnection> Connection { get; }

    /// <summary>Asks the deployment again, which is what a person presses after a fetch that failed.</summary>
    /// <remarks>Nothing is awaited: the answer arrives on <see cref="Standing" /> like any other, so a retry is the same state a first fetch is rather than a second path with its own progress and its own failure.</remarks>
    void Refresh();
}
