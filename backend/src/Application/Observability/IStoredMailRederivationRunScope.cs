// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Observability;

/// <summary>Holds one segment's report open for as long as the segment is running, and opens each pass beneath it.</summary>
/// <remarks>
/// The report is open <em>around</em> the segment rather than written after it, so the database work its passes cause is
/// reported beneath them. A segment that never reports having reached the end of the scope was stopped — by the
/// execution timeout, by shutdown, or by a lease that moved on — and handed the rest of the walk to a segment of its
/// own, which is an ordinary outcome rather than a failure.
/// </remarks>
public interface IStoredMailRederivationRunScope : IDisposable
{
    /// <summary>Opens the report of one bounded pass, which is published when the returned scope is disposed.</summary>
    /// <returns>The scope, which the caller must dispose exactly once and inside which the pass runs.</returns>
    IStoredMailRederivationPassScope BeginPass();

    /// <summary>Records that the segment reached the end of its scope, which is what ends the run.</summary>
    /// <remarks>Called once, on the path that found no mail left. A segment that handed the rest of the walk on does not call it.</remarks>
    void ReachedEndOfScope();

    /// <summary>Records that the segment handed the rest of the walk on, and whether the queue took it.</summary>
    /// <param name="queued">Whether the segment that carries the remainder is waiting in the queue.</param>
    /// <remarks>
    /// A queue at its bound refuses the hand-on, and that is the one way a run stalls without anything failing: it stays
    /// outstanding, nothing carries it, and no dead letter records it either. So the segment reports it rather than
    /// discarding it, which is what puts the stall in front of an operator watching the deployment instead of leaving it
    /// to be inferred from a progress figure that stopped moving.
    /// </remarks>
    void HandedOn(bool queued);
}
