// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.AiProviders;
using MailFathom.Application.Emails.Search;

namespace MailFathom.Application.Retrieval.AskMail;

/// <summary>Decides whether this deployment can answer a question about the mailbox, without calling a provider.</summary>
/// <remarks>
/// <para>
/// Answering needs both halves of the AI configuration at once: an embedding profile a question can be retrieved
/// against, and a chat endpoint the run is conducted through. Either one absent makes answering something this
/// deployment does not do, and either one failing makes it something it currently cannot do. Both readings are made
/// here, in one place, because two callers deciding it separately is how a tool comes to be advertised by the surface
/// that lists it and refused by the one that runs it.
/// </para>
/// <para>
/// It is deliberately cheap and deliberately free: one committed read of local state and one read of process-local
/// health. A capability that spent a provider call to be reported would put an operator's money behind every listing of
/// the tools this server offers.
/// </para>
/// <para>
/// The chat half carries a recheck window and the embedding half does not, and the asymmetry is the point rather than an
/// omission. Health is observed from real calls, and the embedding provider is called by the synchronization workers and
/// by every search that is let through, so its record renews itself on an instance nobody is asking questions of. The
/// chat endpoint has no such traffic: with the second retrieval pass off, answering a question is the only thing that
/// calls it. A deployment that hid the tool for as long as the last failure was on record would therefore hide it
/// forever, because nothing left would ever establish that the credential had been rotated.
/// </para>
/// </remarks>
public sealed class MailAnsweringCapability
{
    /// <summary>How long a recorded chat failure keeps the capability withheld before one question is let through again.</summary>
    /// <remarks>
    /// The same window the semantic read path applies, and for the same reason stated the other way round: a refusing
    /// endpoint is asked at most once a minute however many clients are listing tools, and a repaired one is offered
    /// again within a minute, which nobody watching notices. It is a constant rather than a setting because its whole
    /// range sits between "immediately" and "within a minute".
    /// </remarks>
    private static readonly TimeSpan ProviderRecheckInterval = TimeSpan.FromMinutes(1);

    private readonly SemanticEmailSearch semanticSearch;
    private readonly IAiProviderHealthReader providerHealthReader;
    private readonly TimeProvider timeProvider;
    private readonly IMailQuestionAnswerer? questionAnswerer;

    /// <summary>Initializes the capability over whatever this deployment configured.</summary>
    /// <param name="semanticSearch">Answers whether this instance can retrieve by meaning at all, which is the embedding half.</param>
    /// <param name="providerHealthReader">Answers what the last call to the chat provider established about it.</param>
    /// <param name="timeProvider">Measures how long ago that was, which is what keeps a recorded failure from latching.</param>
    /// <param name="questionAnswerer">Conducts a run, or <see langword="null" /> when this deployment declared no chat endpoint.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="semanticSearch" />, <paramref name="providerHealthReader" />, or <paramref name="timeProvider" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The answerer is the one optional dependency, and its absence is the deployment decision rather than a missing
    /// registration: the composition root registers an answering agent only for an instance that declared a chat
    /// endpoint, so requiring one here would make every other deployment fail to serve a tool list at all.
    /// </remarks>
    public MailAnsweringCapability(
        SemanticEmailSearch semanticSearch,
        IAiProviderHealthReader providerHealthReader,
        TimeProvider timeProvider,
        IMailQuestionAnswerer? questionAnswerer)
    {
        ArgumentNullException.ThrowIfNull(semanticSearch);
        ArgumentNullException.ThrowIfNull(providerHealthReader);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.semanticSearch = semanticSearch;
        this.providerHealthReader = providerHealthReader;
        this.timeProvider = timeProvider;
        this.questionAnswerer = questionAnswerer;
    }

    /// <summary>Reads what this deployment can do with a question right now.</summary>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The availability, which is what decides whether the tool is offered.</returns>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels or the host is shutting down.</exception>
    public async Task<MailAnsweringAvailability> ReadAsync(CancellationToken cancellationToken) =>
        (await this.ResolveAsync(cancellationToken)).Availability;

    /// <summary>Decides the availability and hands back the answerer when a question may run.</summary>
    /// <remarks>
    /// The answerer travels beside the decision so the use case neither resolves an optional dependency of its own nor
    /// has to assert that an availability implies one. It is non-null exactly when the availability is
    /// <see cref="MailAnsweringAvailability.Available" />.
    /// </remarks>
    internal async Task<MailAnsweringGate> ResolveAsync(CancellationToken cancellationToken)
    {
        // Read first because it separates "this deployment answers no questions" from every other reading. Every
        // condition below is about an instance that does, and reporting one of them without an answerer in hand would
        // call a supported deployment degraded.
        if (this.questionAnswerer is not { } answerer)
        {
            return new MailAnsweringGate(MailAnsweringAvailability.Inactive, null);
        }

        var semanticCapability = await this.semanticSearch.ReadCapabilityAsync(cancellationToken);
        if (semanticCapability is not SemanticSearchCapability.Available)
        {
            // An instance that embeds nothing answers no questions, and one whose vectors nothing can place a query
            // beside answers none for now. The semantic capability already separates the two, so it is republished
            // rather than restated.
            return new MailAnsweringGate(
                semanticCapability is SemanticSearchCapability.Inactive
                    ? MailAnsweringAvailability.Inactive
                    : MailAnsweringAvailability.Degraded,
                null);
        }

        return this.IsChatProviderRefusingRecently()
            ? new MailAnsweringGate(MailAnsweringAvailability.Degraded, null)
            : new MailAnsweringGate(MailAnsweringAvailability.Available, answerer);
    }

    /// <summary>Reports whether the chat endpoint refused recently enough that asking again now would only buy the same answer.</summary>
    /// <remarks>
    /// Unobserved is never recent: a freshly started instance has failed at nothing, and the first question to arrive is
    /// what establishes the state every later reading sees. A recorded state with no moment attached is treated as old
    /// rather than fresh, so it can never be what withholds the capability indefinitely.
    /// </remarks>
    private bool IsChatProviderRefusingRecently()
    {
        var health = this.providerHealthReader.Read(AiProviderRole.Chat);

        return health.State is AiProviderHealthState.Unavailable or AiProviderHealthState.Misconfigured
            && health.ObservedAt is { } observedAt
            && this.timeProvider.GetUtcNow() - observedAt < ProviderRecheckInterval;
    }
}
