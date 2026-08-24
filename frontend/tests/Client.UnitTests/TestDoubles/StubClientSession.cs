// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Session;

namespace MailFathom.Client.UnitTests.TestDoubles;

/// <summary>Answers what a deployment allows, from a script, without one being reached.</summary>
/// <remarks>
/// A state rather than a value, because what a screen does with a session it is still waiting for and one it could not
/// have are as much of the behaviour as what it does with an answer. Setting <see cref="Failure" /> is how a test
/// states the second of those.
/// </remarks>
internal sealed class StubClientSession : IClientSession, IDisposable
{
    private readonly Signal signal = new();

    /// <summary>Builds a session answering with a standing, or with nothing at all.</summary>
    /// <param name="standing">What the deployment is to be read as having reported.</param>
    internal StubClientSession(SessionStanding? standing = null)
    {
        this.Answer = standing;
        this.Standing = State.Async(this, this.ReadAsync, this.signal);
    }

    /// <summary>Gets or sets what the deployment is read as having reported.</summary>
    internal SessionStanding? Answer { get; set; }

    /// <summary>Gets or sets what asking the deployment raises instead of answering.</summary>
    internal Exception? Failure { get; set; }

    /// <summary>Gets how many times the session was asked to fetch again.</summary>
    internal int Refreshes { get; private set; }

    /// <inheritdoc />
    public IFeed<SessionStanding> Standing { get; }

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
        if (this.Failure is { } failure)
        {
            throw failure;
        }

        return this.Answer is { } answer
            ? ValueTask.FromResult(answer)
            : throw new InvalidOperationException("This stub was told neither what the deployment answers nor how it fails.");
    }
}
