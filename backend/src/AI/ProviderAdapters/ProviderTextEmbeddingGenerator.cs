// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Embeddings;
using MailFathom.AI.Providers;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Resilience;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Domain.Failures;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MailFathom.AI.ProviderAdapters;

/// <summary>Produces vectors by calling a provider, through whichever endpoint of the declared chain is serving.</summary>
/// <remarks>
/// <para>
/// The only type in this system that speaks to an embedding provider. Everything provider-specific stops here: the
/// client library, its options, its exceptions, and the two authentication shapes are all confined to this namespace,
/// so a third provider is a new endpoint kind rather than a change anywhere above.
/// </para>
/// <para>
/// Falling through the chain never changes what is written. Every endpoint declares the same geometry — startup
/// refuses a chain where they differ — so the vectors a fallback returns belong to the same profile as the ones before
/// them, and no read path has to know which endpoint produced which. See
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0006-embedding-profile-identity-lifecycle-and-activation-cost.md">ADR 0006</see>.
/// </para>
/// <para>
/// Every passage is scanned before it is sent, where this deployment scans anything, and a scanner that cannot answer
/// refuses the call rather than letting it go unscanned. The guard applies to every declared endpoint: an address
/// inside the deployment is still a configured endpoint reached over HTTP, nothing in the declaration distinguishes a
/// local provider from a hosted one, and text leaving the process is text leaving the process. The one embedding
/// generator that is exempt is the deterministic one, which computes a vector in this process and sends nothing.
/// </para>
/// </remarks>
internal sealed class ProviderTextEmbeddingGenerator : ITextEmbeddingGenerator
{
    /// <summary>Names the registered transport an embedding request is sent over.</summary>
    /// <remarks>
    /// Declared by the consumer rather than by the registration, because a name resolved through
    /// <see cref="IHttpClientFactory" /> is a string either side can get wrong in silence: asking for one that was
    /// never registered yields a client with no bounds and no handlers rather than a failure. One constant, referenced
    /// from both, is what makes that a compile-time agreement.
    /// </remarks>
    internal const string TransportName = "mailfathom.embedding-provider";

    private readonly EmbeddingGenerationPlan plan;
    private readonly IProviderEndpointCredentialSource credentialSource;
    private readonly OpenAiCompatibleClientFactory clientFactory;
    private readonly IHttpClientFactory transportFactory;
    private readonly IOutboundOperationRunner operationRunner;
    private readonly IAiProviderHealthRecorder healthRecorder;
    private readonly SensitiveContentEgressGuard egressGuard;
    private readonly ILogger<ProviderTextEmbeddingGenerator> logger;

    /// <summary>Initializes a generator over the declared chain, its credentials, its transport, and its resilience budget.</summary>
    /// <param name="plan">The validated declaration: one vector space and the endpoints that serve it.</param>
    /// <param name="credentialSource">Resolves what a request presents to an endpoint.</param>
    /// <param name="clientFactory">Opens a provider client over one endpoint.</param>
    /// <param name="transportFactory">Opens the transport a request is sent over, one per attempt.</param>
    /// <param name="operationRunner">Applies the provider resilience budget.</param>
    /// <param name="healthRecorder">Records what each call established about the embedding provider.</param>
    /// <param name="egressGuard">Scans every passage before it is sent, where this deployment scans anything.</param>
    /// <param name="logger">Records the outcome without recording any passage, vector, or credential.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public ProviderTextEmbeddingGenerator(
        EmbeddingGenerationPlan plan,
        IProviderEndpointCredentialSource credentialSource,
        OpenAiCompatibleClientFactory clientFactory,
        IHttpClientFactory transportFactory,
        IOutboundOperationRunner operationRunner,
        IAiProviderHealthRecorder healthRecorder,
        SensitiveContentEgressGuard egressGuard,
        ILogger<ProviderTextEmbeddingGenerator> logger)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(credentialSource);
        ArgumentNullException.ThrowIfNull(clientFactory);
        ArgumentNullException.ThrowIfNull(transportFactory);
        ArgumentNullException.ThrowIfNull(operationRunner);
        ArgumentNullException.ThrowIfNull(healthRecorder);
        ArgumentNullException.ThrowIfNull(egressGuard);
        ArgumentNullException.ThrowIfNull(logger);

        this.plan = plan;
        this.credentialSource = credentialSource;
        this.clientFactory = clientFactory;
        this.transportFactory = transportFactory;
        this.operationRunner = operationRunner;
        this.healthRecorder = healthRecorder;
        this.egressGuard = egressGuard;
        this.logger = logger;
    }

    /// <inheritdoc />
    public EmbeddingProfileIdentity Identity => this.plan.Identity;

    /// <inheritdoc />
    public int MaximumPassagesPerCall => this.plan.MaximumPassagesPerCall;

    /// <inheritdoc />
    public async Task<IReadOnlyList<EmbeddingVector>> GenerateAsync(
        IReadOnlyList<string> passages,
        CancellationToken cancellationToken)
    {
        EmbeddingRequestBounds.Require(passages, this.plan.MaximumPassagesPerCall);

        // Scanned before the chain rather than before each endpoint, so one call costs one scan however many endpoints
        // it falls through and however many attempts the resilience pipeline makes at each. A scanner that cannot
        // answer refuses the call here, where the refusal is still itself rather than a fault attributed to a provider
        // that was never reached.
        var guarded = await this.egressGuard.GuardAllAsync(
            SensitiveContentEgressPoint.HostedEmbeddingInput,
            passages,
            cancellationToken);

        try
        {
            var vectors = await this.GenerateAcrossChainAsync(guarded, cancellationToken);

            // Recorded once the chain has produced vectors, not once an endpoint has: an endpoint that fell through to
            // a working fallback is a chain that served, and reporting the fall-through as ill health would describe
            // the declaration's own resilience as a fault.
            this.healthRecorder.RecordServed(AiProviderRole.Embedding);

            return vectors;
        }
        catch (EmbeddingGenerationFailedException failure)
        {
            this.RecordFailure(failure);

            throw;
        }
    }

    private async Task<IReadOnlyList<EmbeddingVector>> GenerateAcrossChainAsync(
        IReadOnlyList<string> passages,
        CancellationToken cancellationToken)
    {
        var prepared = passages
            .Select(passage => EmbeddingPassagePreparation.Prepare(passage, this.plan.Identity.InputPreparation))
            .ToArray();

        EmbeddingGenerationFailedException? lastFailure = null;

        foreach (var endpoint in this.plan.Endpoints)
        {
            try
            {
                return await this.GenerateAtAsync(endpoint, prepared, cancellationToken);
            }
            catch (EmbeddingGenerationFailedException failure) when (IsWorthAnotherEndpoint(failure))
            {
                EmbeddingProviderEvents.LogFallingThrough(this.logger, endpoint.Alias, failure.Failure);

                lastFailure = failure;
            }
        }

        // Reached only after every endpoint fell through, so a failure is always in hand.
        EmbeddingProviderEvents.LogChainExhausted(this.logger, this.plan.Endpoints.Count, lastFailure!.Failure);

        throw lastFailure;
    }

    /// <summary>Records what the failure established about the provider, at the granularity an operator acts on.</summary>
    /// <remarks>
    /// The split is the exception's own <see cref="EmbeddingGenerationFailedException.IsWorthRepeating" />, so the
    /// health state and the resilience pipeline can never disagree about whether waiting is the answer.
    /// </remarks>
    private void RecordFailure(EmbeddingGenerationFailedException failure)
    {
        if (failure.IsWorthRepeating)
        {
            this.healthRecorder.RecordUnavailable(AiProviderRole.Embedding);

            return;
        }

        this.healthRecorder.RecordMisconfigured(AiProviderRole.Embedding);
    }

    /// <summary>Reports whether the next endpoint of the chain could answer where this one did not.</summary>
    /// <remarks>
    /// An unreachable endpoint, a throttled one, a slow one, and one that refused the credential are all statements
    /// about that endpoint, and the next one is a different address with a different credential. An answer of the
    /// wrong shape is not: every endpoint of a chain declares the same geometry, so a model returning a width nothing
    /// declared means the declaration is wrong, and asking the next endpoint would buy a second paid call to learn the
    /// same thing.
    /// </remarks>
    private static bool IsWorthAnotherEndpoint(EmbeddingGenerationFailedException failure) =>
        failure.Failure is not EmbeddingGenerationFailure.VectorShapeUnexpected;

    /// <summary>Reads a transport-level classification into the one this port publishes.</summary>
    /// <remarks>
    /// Five of the six members map straight across, because the shared classifier already names what the remote party
    /// did. The sixth — an answer of the wrong shape — has no transport-level counterpart at all: it is decided from
    /// what came back rather than from how, so it is raised where the vectors are read instead.
    /// </remarks>
    private static EmbeddingGenerationFailure ToEmbeddingFailure(ProviderCallFailure failure) => failure switch
    {
        ProviderCallFailure.CredentialRejected => EmbeddingGenerationFailure.CredentialRejected,
        ProviderCallFailure.RateLimited => EmbeddingGenerationFailure.RateLimited,
        ProviderCallFailure.RequestTimedOut => EmbeddingGenerationFailure.RequestTimedOut,
        ProviderCallFailure.RequestRefused => EmbeddingGenerationFailure.RequestRefused,
        _ => EmbeddingGenerationFailure.TransportFaulted,
    };

    private async Task<IReadOnlyList<EmbeddingVector>> GenerateAtAsync(
        EmbeddingEndpoint endpoint,
        string[] prepared,
        CancellationToken cancellationToken)
    {
        // Resolved per request and released with it, so a rotated key is picked up by the next call and the material
        // exists for one request rather than for process uptime.
        using var credential = await this.credentialSource.ResolveAsync(endpoint.Alias, cancellationToken);

        IReadOnlyList<Embedding<float>> vectors;
        try
        {
            // Keyed per endpoint. One unreachable provider must not open the circuit the others are served through,
            // and it must not spend their concurrency budget either; the alias is the deployment's own name, so
            // nothing personal reaches resilience telemetry.
            vectors = await this.operationRunner.RunAsync(
                OutboundDependency.AiProviderInvocation,
                endpoint.Alias,
                attemptToken => this.RequestVectorsAsync(endpoint, credential, prepared, attemptToken),
                cancellationToken);
        }
        catch (MailFathomException rejection)
            when (rejection.ErrorCode == MailFathomErrorCode.OutboundDependencyUnavailable)
        {
            // The pipeline declined to call this endpoint at all — its circuit is open, or its concurrency budget is
            // spent. Recognized by code rather than by type, which is what a stable error code is for: the resilience
            // library and the exception it raises belong to another adapter boundary that this one may not reference.
            //
            // Translating it is what lets the chain do its job. An open circuit on the first endpoint is exactly the
            // condition a fallback exists for, and leaving the rejection to propagate would make the whole chain
            // unusable for as long as its first entry stays broken.
            throw new EmbeddingGenerationFailedException(
                endpoint.Alias,
                EmbeddingGenerationFailure.TransportFaulted,
                rejection);
        }

        return this.MapVectors(endpoint, vectors, prepared.Length);
    }

    /// <summary>Sends one request and returns exactly what the provider answered.</summary>
    /// <remarks>
    /// The transport is opened per attempt and released with it, which is what keeps the connection bounds in the
    /// registration rather than in a client held across a run: the factory retires a handler chain on its own
    /// schedule, so an endpoint that has moved is reached at its new address by the next attempt. Inside a retry a
    /// per-attempt client also costs nothing.
    /// </remarks>
    private async Task<IReadOnlyList<Embedding<float>>> RequestVectorsAsync(
        EmbeddingEndpoint endpoint,
        ProviderEndpointCredential credential,
        string[] prepared,
        CancellationToken cancellationToken)
    {
        // The timeout is this deployment's and is applied here rather than left to the client, so one attempt is
        // bounded whichever provider library is underneath and whatever it defaults to.
        using var attemptDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        attemptDeadline.CancelAfter(this.plan.RequestTimeout);

        using var transport = this.transportFactory.CreateClient(TransportName);
        using var generator = this.clientFactory.OpenEmbeddingGenerator(endpoint, credential, transport);

        var options = new EmbeddingGenerationOptions
        {
            // Asking for the narrower space beats cutting one out of a wider answer: a model trained to answer at a
            // requested width returns a vector that is already normalized for it, which a truncation only approximates.
            Dimensions = endpoint.SupportsRequestedDimension ? this.plan.Identity.Dimension : null,
        };

        try
        {
            return await generator.GenerateAsync(prepared, options, attemptDeadline.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The caller did not cancel, so the deadline above did. Reporting it as a cancellation would tell the
            // pipeline that this system stopped the work, and a timeout is the dependency failing to answer.
            throw new EmbeddingGenerationFailedException(
                endpoint.Alias,
                EmbeddingGenerationFailure.RequestTimedOut);
        }
        catch (Exception failure) when (ProviderCallFailureClassification.Classify(failure) is { } classified)
        {
            throw new EmbeddingGenerationFailedException(endpoint.Alias, ToEmbeddingFailure(classified), failure);
        }
    }

    /// <summary>Turns the provider's answer into vectors of the declared space, or refuses it.</summary>
    /// <remarks>
    /// The count is checked as well as the width, because a provider that returned fewer vectors than passages leaves
    /// the caller mapping vectors onto the wrong chunks — a corruption no later check can see, since every vector is
    /// individually valid.
    /// </remarks>
    private IReadOnlyList<EmbeddingVector> MapVectors(
        EmbeddingEndpoint endpoint,
        IReadOnlyList<Embedding<float>> answered,
        int passageCount)
    {
        if (answered.Count != passageCount)
        {
            throw new EmbeddingGenerationFailedException(
                endpoint.Alias,
                EmbeddingGenerationFailure.VectorShapeUnexpected);
        }

        var declaredDimension = this.plan.Identity.Dimension;
        var answeredDimension = answered[0].Vector.Length;

        if (answered.Any(vector => vector.Vector.Length != answeredDimension))
        {
            throw new EmbeddingGenerationFailedException(
                endpoint.Alias,
                EmbeddingGenerationFailure.VectorShapeUnexpected);
        }

        if (answeredDimension != declaredDimension
            && (answeredDimension < declaredDimension || !this.plan.AllowTrimVectors))
        {
            throw new EmbeddingGenerationFailedException(
                endpoint.Alias,
                EmbeddingGenerationFailure.VectorShapeUnexpected);
        }

        if (answeredDimension != declaredDimension)
        {
            EmbeddingProviderEvents.LogVectorsShortened(
                this.logger,
                endpoint.Alias,
                answeredDimension,
                declaredDimension);
        }

        try
        {
            return
            [
                .. answered.Select(vector => EmbeddingVector
                    .Create(vector.Vector.Span)
                    .Shorten(declaredDimension)),
            ];
        }
        catch (Exception failure) when (failure is ArgumentException or InvalidOperationException)
        {
            // A component that is not a finite number, or a vector whose components are all zero. Both survive every
            // distance operator as a result that is neither an error nor a number, so the chunk carrying one would
            // quietly stop being retrievable rather than fail.
            throw new EmbeddingGenerationFailedException(
                endpoint.Alias,
                EmbeddingGenerationFailure.VectorShapeUnexpected,
                failure);
        }
    }
}
