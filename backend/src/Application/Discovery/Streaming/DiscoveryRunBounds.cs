// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Planning;
using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Discovery.Runs;

namespace MailFathom.Application.Discovery.Streaming;

/// <summary>What one streamed run may take, hold, and leave behind, and what happens at each of those points.</summary>
/// <remarks>
/// <para>
/// A streamed run outlives the request that started it, so every one of these is a bound on memory or on a provider
/// that nothing else would apply: the request has already been answered by the time any of them is reached.
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
        + DiscoveryPresentationComposition.MaximumCitations
        + PresentationPlan.MaxBlocks
        + 1;

    /// <summary>The greatest number of runs this process holds at once, whether executing or waiting to be read.</summary>
    /// <remarks>
    /// A run is one person's question rather than a request being served, and a person does not ask eight at a time — so
    /// this bounds what a client looping over the start route can make this process hold, and a start beyond it is
    /// refused rather than queued. A refusal is the honest answer: queueing would leave somebody watching a run that has
    /// not begun, which is the spinner the whole surface exists to remove.
    /// </remarks>
    public const int MaximumConcurrentRuns = 8;

    /// <summary>The longest one run may take before it is stopped where it stands.</summary>
    /// <remarks>
    /// Generous against what a question needs — a derivation and up to six ranked reads — and short enough that a run
    /// whose provider never answers stops holding a scope and a database connection. It is what a run that lost its
    /// client is eventually ended by, and it ends the run as <see cref="DiscoveryRunFailure.TimedOut" /> with whatever
    /// it had already published still readable.
    /// </remarks>
    public static TimeSpan MaximumDuration { get; } = TimeSpan.FromMinutes(5);

    /// <summary>How long a run that has ended stays readable before it is forgotten.</summary>
    /// <remarks>
    /// The window a dropped connection is recovered inside. It runs from the last time anything read or wrote the run,
    /// so a client that is reading is never cut off mid-stream and one that never came back is dropped rather than kept
    /// for the life of the process.
    /// </remarks>
    public static TimeSpan RetentionAfterLastUse { get; } = TimeSpan.FromMinutes(5);
}
