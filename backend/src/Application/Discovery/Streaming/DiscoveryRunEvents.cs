// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;
using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Discovery.Presentation.Citations;
using MailFathom.Application.Discovery.Runs;

namespace MailFathom.Application.Discovery.Streaming;

/// <summary>The run has begun, which is the first thing a client is told and the reason it is told anything at all.</summary>
/// <remarks>
/// It carries the revision of the presentation contract rather than anything about the question, because a client keys
/// its renderers by that and needs it before the first block arrives. What the run decided is not in it: the intent and
/// the composition are derived from the question after the run starts, and a start that waited for them would be a
/// start that arrives no sooner than the derivation it was supposed to precede.
/// </remarks>
public sealed record DiscoveryRunStarted : DiscoveryRunEvent
{
    /// <summary>The value the type discriminator carries on the wire.</summary>
    public const string Kind = "started";

    /// <inheritdoc />
    [JsonIgnore]
    public override string EventName => Kind;

    /// <summary>Gets the revision of the presentation contract this run writes its blocks against.</summary>
    public int PlanSchemaVersion { get; init; } = PresentationPlan.CurrentSchemaVersion;
}

/// <summary>One of the plan's lookups has settled, and this is how far the retrieval has got.</summary>
/// <param name="Progress">The counts the retrieval reported.</param>
/// <remarks>
/// Published once per lookup rather than on a timer, so the number of these is bounded by the plan and a run that is
/// waiting on a slow provider publishes nothing rather than repeating itself.
/// </remarks>
public sealed record DiscoveryRetrievalProgressed(DiscoveryRetrievalProgress Progress) : DiscoveryRunEvent
{
    /// <summary>The value the type discriminator carries on the wire.</summary>
    public const string Kind = "retrieval";

    /// <inheritdoc />
    [JsonIgnore]
    public override string EventName => Kind;
}

/// <summary>A source the run's blocks rest on, declared before anything names it.</summary>
/// <param name="Citation">The source, under the name the blocks refer to it by.</param>
/// <remarks>
/// Declared as its own event rather than repeated inside each block for the reason a whole plan declares its citations
/// once: two facts drawn from one message are visibly the same source, and a client can list what a run rested on
/// before it has drawn a block.
/// </remarks>
public sealed record DiscoveryCitationDeclared(PresentationCitation Citation) : DiscoveryRunEvent
{
    /// <summary>The value the type discriminator carries on the wire.</summary>
    public const string Kind = "citation";

    /// <inheritdoc />
    [JsonIgnore]
    public override string EventName => Kind;
}

/// <summary>A block is ready, and every source it names has already been declared.</summary>
/// <param name="Block">The block, in the place it holds in the run's reading order.</param>
public sealed record DiscoveryBlockComposed(PresentationBlock Block) : DiscoveryRunEvent
{
    /// <summary>The value the type discriminator carries on the wire.</summary>
    public const string Kind = "block";

    /// <inheritdoc />
    [JsonIgnore]
    public override string EventName => Kind;
}

/// <summary>The run finished, and this is what it knows about its own reach.</summary>
/// <param name="Limitations">What made the answer narrower than the question, and empty where nothing did.</param>
/// <param name="Coverage">What the run read, one entry per account its scope reached.</param>
/// <remarks>
/// <para>
/// A run that composed no block still completes rather than failing. A mailbox holding nothing on the subject is an
/// answer, and reporting it as a failure would tell somebody to ask again.
/// </para>
/// <para>
/// Both halves are statements about the whole run rather than about any one block, which is why they arrive here
/// instead of as events of their own: neither is known until the composition has finished, and a client holding the
/// ending holds the last two parts of the plan it has been assembling.
/// </para>
/// </remarks>
public sealed record DiscoveryRunCompleted(
    IReadOnlyList<PresentationLimitation> Limitations,
    IReadOnlyList<AccountCoverage> Coverage) : DiscoveryRunEvent
{
    /// <summary>The value the type discriminator carries on the wire.</summary>
    public const string Kind = "completed";

    /// <inheritdoc />
    [JsonIgnore]
    public override string EventName => Kind;

    /// <inheritdoc />
    [JsonIgnore]
    public override bool EndsTheRun => true;
}

/// <summary>The run stopped without finishing, and whatever it had already published still stands.</summary>
/// <param name="Failure">Why it stopped, in the terms a client can act on.</param>
public sealed record DiscoveryRunFailed(DiscoveryRunFailure Failure) : DiscoveryRunEvent
{
    /// <summary>The value the type discriminator carries on the wire.</summary>
    public const string Kind = "failed";

    /// <inheritdoc />
    [JsonIgnore]
    public override string EventName => Kind;

    /// <inheritdoc />
    [JsonIgnore]
    public override bool EndsTheRun => true;
}
