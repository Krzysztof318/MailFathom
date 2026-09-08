// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;
using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Discovery.Presentation.Citations;
using MailFathom.Application.Discovery.Runs;
using MailFathom.Application.Retrieval.AskMail;

namespace MailFathom.Application.Discovery.Streaming;

/// <summary>The run has begun, which is the first thing a client is told and the reason it is told anything at all.</summary>
/// <remarks>
/// <para>
/// It carries the revision of the presentation contract rather than anything about the question, because a client keys
/// its renderers by that and needs it before the first block arrives. What the run decided is not in it: the intent and
/// the composition are derived from the question after the run starts, and a start that waited for them would be a
/// start that arrives no sooner than the derivation it was supposed to precede.
/// </para>
/// <para>
/// <strong>It also carries the envelope, which is what a run says about cost before it has spent anything.</strong>
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0022-what-an-ai-run-reports-about-cost-cancellation-and-the-model.md">ADR 0022</see>
/// refuses an estimate — a run is a conversation whose length is a model's decision, so anything predicted before it
/// starts is a guess, and a guess people learn to ignore takes the true figure with it. What is stated instead is known
/// without asking anything: what one question may spend on this deployment, and which endpoint will answer it.
/// </para>
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

    /// <summary>Gets what one question may spend on this deployment, which is what every count a run reports is read against.</summary>
    public MailAnsweringRunBounds Bounds { get; init; } = MailAnsweringRunBounds.Default;

    /// <summary>Gets this deployment's own name for the endpoint answering the run, and empty on a deployment that answers no questions.</summary>
    /// <remarks>The name the operator chose, which is already what this deployment's logs, metrics, and failures call the endpoint. The address it is reached at and the name a request is routed under are neither of them published.</remarks>
    public string EndpointAlias { get; init; } = string.Empty;

    /// <summary>Gets the model name the operator declared for publication, and empty where they declared none.</summary>
    /// <remarks>Absent by default: disclosure is declared rather than inferred, so a deployment that wrote no publishable name names the alias alone.</remarks>
    public string PublishedModel { get; init; } = string.Empty;
}

/// <summary>One of the plan's lookups has settled, and this is how far the retrieval has got and what the run has spent reaching it.</summary>
/// <param name="Progress">The counts the retrieval reported.</param>
/// <param name="Spend">What the run has consumed so far, against the ceilings the start named.</param>
/// <remarks>
/// <para>
/// Published once per lookup rather than on a timer, so the number of these is bounded by the plan and a run that is
/// waiting on a slow provider publishes nothing rather than repeating itself.
/// </para>
/// <para>
/// The spend rides here rather than on a channel of its own, which is what ADR 0022 settles: a second stream carrying
/// cost would be a second thing to keep in step with the first, and this is the event a run already publishes while it
/// is working. Both halves are counts and neither carries mail.
/// </para>
/// </remarks>
public sealed record DiscoveryRetrievalProgressed(DiscoveryRetrievalProgress Progress, MailAnsweringRunSpend Spend)
    : DiscoveryRunEvent
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
/// <param name="Spend">What the run consumed in total, against the ceilings the start named.</param>
/// <remarks>
/// <para>
/// A run that composed no block still completes rather than failing. A mailbox holding nothing on the subject is an
/// answer, and reporting it as a failure would tell somebody to ask again.
/// </para>
/// <para>
/// All three are statements about the whole run rather than about any one block, which is why they arrive here instead
/// of as events of their own: none is known until the composition has finished, and a client holding the ending holds
/// the last parts of the plan it has been assembling.
/// </para>
/// <para>
/// The final counts stay with the result rather than with the application, which is ADR 0022's placement: a cost figure
/// in permanent chrome teaches people to watch a meter while they work, and one on the answer is legible exactly when
/// somebody asks whether that answer was expensive.
/// </para>
/// </remarks>
public sealed record DiscoveryRunCompleted(
    IReadOnlyList<PresentationLimitation> Limitations,
    IReadOnlyList<AccountCoverage> Coverage,
    MailAnsweringRunSpend Spend) : DiscoveryRunEvent
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
/// <param name="Spend">What the run consumed before it stopped, against the ceilings the start named.</param>
/// <remarks>
/// The spend is carried here as well as on a completed run, because it is most worth having exactly when a run did not
/// end cleanly: a cancelled run and a run a ceiling refused have both spent something, and cancelling buys the
/// remainder rather than a refund.
/// </remarks>
public sealed record DiscoveryRunFailed(DiscoveryRunFailure Failure, MailAnsweringRunSpend Spend) : DiscoveryRunEvent
{
    /// <summary>The value the type discriminator carries on the wire.</summary>
    public const string Kind = "failed";

    /// <inheritdoc />
    [JsonIgnore]
    public override string EventName => Kind;

    /// <inheritdoc />
    [JsonIgnore]
    public override bool EndsTheRun => true;

    /// <summary>Gets when the refused allowance returns, which only <see cref="DiscoveryRunFailure.PeriodSpent" /> carries.</summary>
    /// <remarks>
    /// <para>
    /// The roll-over of the fixed epoch-anchored window the period is placed in, so it is a function of the clock and
    /// the configured period rather than of anybody's activity — which is what lets it be published at all. A client
    /// re-enables the question at that instant rather than offering a retry that will be refused.
    /// </para>
    /// <para>
    /// <see langword="null" /> for every other ending, including the run's own ceiling: asking the same question again
    /// reaches that one by the same route, so there is no instant to name. Nothing here says how much was consumed —
    /// how much of the period is left is a fact about what everybody on the deployment has been asking, and on a
    /// deployment serving several users it would report one person's activity to another.
    /// </para>
    /// </remarks>
    public DateTimeOffset? RetryAt { get; init; }
}
