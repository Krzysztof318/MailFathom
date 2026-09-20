// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Planning;
using MailFathom.Application.Discovery.Presentation;

namespace MailFathom.Application.Discovery.Streaming;

/// <summary>What one watched run may take, hold, and leave behind, and what happens at each of those points.</summary>
/// <remarks>
/// <para>
/// A run outlives the request that started it, so every one of these is a bound on what a run may write, on
/// what it may cost a provider, or on how long its answer is kept: the request has already been answered by the time
/// any of them is reached.
/// </para>
/// <para>
/// They are constants rather than settings. Each is a property of what the contract can express — how many events a run
/// can produce follows from how many blocks and citations a plan may hold — or a ceiling that stops a leak rather than
/// a figure an operator would tune. What one run may spend, and what a deployment allows it to cost, is a budget of its
/// own and is not decided here.
/// </para>
/// </remarks>
public static class DiscoveryRunBounds
{
    /// <summary>The greatest number of events one run publishes.</summary>
    /// <remarks>
    /// <para>
    /// Derived rather than chosen: a run publishes one start, at most one progress report per lookup a plan may hold,
    /// at most one citation per source a run may declare, at most one block per block a plan may hold, and one ending.
    /// So a run that reaches this bound has published everything the contract lets it publish.
    /// </para>
    /// <para>
    /// Reaching it stops the run composing anything further and ends it stating
    /// <see cref="PresentationLimitation.BlocksOmitted" />, rather than dropping an event or growing past the bound. The
    /// ending itself is always published, which is what the reservation below is for: a client is never left waiting on
    /// a run that has already stopped.
    /// </para>
    /// </remarks>
    public const int MaximumEvents = 1
        + RetrievalPlan.MaximumLookups
        + PresentationPlan.MaxCitations
        + PresentationPlan.MaxBlocks
        + 1;

    /// <summary>The greatest number of runs one person has executing at once, across the whole deployment.</summary>
    /// <remarks>
    /// <para>
    /// A run is one person's question rather than a request being served, and a person does not ask eight at a time — so
    /// this bounds what a client looping over the start route can make a deployment do on that person's behalf, and a
    /// start beyond it is refused rather than queued. A refusal is the honest answer: queueing would leave somebody
    /// watching a run that has not begun, which is the spinner the whole surface exists to remove.
    /// </para>
    /// <para>
    /// <strong>It is one person's and the deployment's, rather than one process's.</strong> It is counted in the same
    /// statement that opens the run, so raising the replica count does not multiply it and what somebody may start does
    /// not depend on which replica their request reached — which is what lets the number a client is told mean
    /// something. A run that has ended is not one of them: what this bounds is work in flight, and an answer waiting to
    /// be read costs rows rather than a provider.
    /// </para>
    /// </remarks>
    public const int MaximumConcurrentRunsPerUser = 8;

    /// <summary>The longest one run may take before it is stopped where it stands.</summary>
    /// <remarks>
    /// Generous against what a question needs — a derivation and up to six ranked reads — and short enough that a run
    /// whose provider never answers stops holding a scope and a database connection. It is what a run that lost its
    /// client is eventually ended by, and it ends the run as <see cref="DiscoveryRunFailure.TimedOut" /> with whatever
    /// it had already published still readable.
    /// <para>
    /// <see cref="WatchedDiscoveryRun" /> applies it, over a cancellation source of its own rather than over the token
    /// its caller passes, which is what keeps a run that spent this bound distinguishable from one a stopping
    /// deployment cut short.
    /// </para>
    /// </remarks>
    public static TimeSpan MaximumDuration { get; } = TimeSpan.FromMinutes(5);

    /// <summary>How long a run that has ended stays readable before it is forgotten.</summary>
    /// <remarks>
    /// The window a dropped connection is recovered inside. It runs from the last time anything read or wrote the run,
    /// so a client that is reading is never cut off mid-read and one that never came back is dropped rather than kept
    /// indefinitely. It is also the storage limitation on what a run wrote, which is mail-derived throughout: the
    /// removal is what makes a run's answer stop existing rather than a decision anybody has to take.
    /// </remarks>
    public static TimeSpan RetentionAfterLastUse { get; } = TimeSpan.FromMinutes(5);
}
