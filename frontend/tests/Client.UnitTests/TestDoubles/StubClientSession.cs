// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Session;

namespace MailFathom.Client.UnitTests.TestDoubles;

/// <summary>Answers what a deployment allows, from a script, without one being reached.</summary>
/// <remarks>
/// A state rather than a value, because what a screen does with a session it is still waiting for is as much of the
/// behaviour as what it does with an answer. It scripts no failure: the failure axis of this feed is asserted against
/// the real <c>DeploymentClientSession</c> over a refusing transport, which is where a failed fetch actually comes
/// from, and a second way to produce one here would be surface nothing exercises.
/// </remarks>
internal sealed class StubClientSession : IClientSession, IDisposable
{
    private readonly Signal signal = new();

    /// <summary>Builds a session answering with a standing, or with nothing at all.</summary>
    /// <param name="standing">What the deployment is to be read as having reported.</param>
    /// <param name="reach">Where the connection is to be read as standing, which defaults to a deployment that answered.</param>
    internal StubClientSession(SessionStanding? standing = null, DeploymentConnection? reach = null)
    {
        this.Answer = standing;
        this.Answered = reach ?? Reachable;
        this.Standing = State.Async(this, this.ReadAsync, this.signal);
        this.Connection = State.Async(this, this.ReadConnectionAsync, this.signal);
    }

    /// <summary>A deployment that answered on the first attempt, which is what a test not about the connection wants.</summary>
    internal static DeploymentConnection Reachable { get; } = new(ConnectionStanding.Reached, Attempt: 1, Attempts: 5);

    /// <summary>Gets or sets what the deployment is read as having reported.</summary>
    internal SessionStanding? Answer { get; set; }

    /// <summary>Gets or sets where the connection to the deployment is read as standing.</summary>
    internal DeploymentConnection Answered { get; set; }

    /// <summary>Gets how many times the session was asked to fetch again.</summary>
    internal int Refreshes { get; private set; }

    /// <inheritdoc />
    public IFeed<SessionStanding> Standing { get; }

    /// <inheritdoc />
    public IFeed<DeploymentConnection> Connection { get; }

    /// <inheritdoc />
    public void Refresh()
    {
        this.Refreshes++;
        this.signal.Raise();
    }

    /// <inheritdoc />
    public void Dispose() => this.signal.Dispose();

    private ValueTask<SessionStanding> ReadAsync(CancellationToken cancellationToken)
    {
        return this.Answer is { } answer
            ? ValueTask.FromResult(answer)
            : throw new InvalidOperationException("This stub was not told what the deployment answers.");
    }

    private ValueTask<DeploymentConnection> ReadConnectionAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(this.Answered);
}
