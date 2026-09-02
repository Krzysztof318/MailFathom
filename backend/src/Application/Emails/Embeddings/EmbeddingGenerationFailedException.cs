// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Failures;

namespace MailFathom.Application.Emails.Embeddings;

/// <summary>Indicates that an embedding request produced no vectors, and says which kind of failure ended it.</summary>
/// <remarks>
/// <para>
/// An exception rather than a result type, because the fact travels through code that cannot decide what it means: the
/// resilience pipeline between the adapter and its caller reads it to choose whether to make another attempt, and a
/// result would have to be unwrapped and rethrown there anyway.
/// </para>
/// <para>
/// The message names the endpoint by the alias the deployment gave it in configuration and never by its address, for
/// the reason <see cref="MailFathomException" /> states: an endpoint address identifies a tenant, and this message
/// reaches a log.
/// </para>
/// </remarks>
public sealed class EmbeddingGenerationFailedException : MailFathomException
{
    /// <summary>Initializes a failure naming the endpoint alias, what kind of failure it was, and the failure that revealed it.</summary>
    /// <param name="endpointAlias">The deployment's own configured name for the endpoint that failed.</param>
    /// <param name="failure">What kind of failure ended the request.</param>
    /// <param name="cause">The failure this one was raised for.</param>
    public EmbeddingGenerationFailedException(
        string endpointAlias,
        EmbeddingGenerationFailure failure,
        Exception cause)
        : base(DescribeFailure(endpointAlias, failure), cause) =>
        this.Failure = failure;

    /// <summary>Initializes a failure naming the endpoint alias and what kind of failure it was.</summary>
    /// <param name="endpointAlias">The deployment's own configured name for the endpoint that failed.</param>
    /// <param name="failure">What kind of failure ended the request.</param>
    public EmbeddingGenerationFailedException(string endpointAlias, EmbeddingGenerationFailure failure)
        : base(DescribeFailure(endpointAlias, failure)) =>
        this.Failure = failure;

    /// <inheritdoc />
    /// <remarks>
    /// Three codes cover six classifications, because a code names what an operator does about a failure rather than
    /// how it arrived: a refused credential is rotated, a wrong-shaped answer is a declaration to correct, and
    /// everything else is a run to repeat later. <see cref="Failure" /> keeps the finer distinction for the pipeline
    /// that has to decide about repeating.
    /// </remarks>
    public override MailFathomErrorCode ErrorCode => this.Failure switch
    {
        EmbeddingGenerationFailure.CredentialRejected => MailFathomErrorCode.EmbeddingProviderCredentialRejected,
        EmbeddingGenerationFailure.VectorShapeUnexpected => MailFathomErrorCode.EmbeddingVectorShapeUnexpected,
        _ => MailFathomErrorCode.EmbeddingProviderUnavailable,
    };

    /// <summary>Gets what kind of failure ended the request.</summary>
    public EmbeddingGenerationFailure Failure { get; }

    /// <summary>Gets whether repeating the request could succeed without anything else changing first.</summary>
    /// <remarks>
    /// Declared here rather than left to each caller, so the resilience pipeline and any supervisor deciding whether
    /// to keep asking give the same answer. A refused credential, a rejected request, and an answer of the wrong shape
    /// each need somebody to change something before the next attempt can differ from this one.
    /// </remarks>
    public bool IsWorthRepeating => this.Failure
        is EmbeddingGenerationFailure.RateLimited
        or EmbeddingGenerationFailure.RequestTimedOut
        or EmbeddingGenerationFailure.TransportFaulted;

    private static string DescribeFailure(string endpointAlias, EmbeddingGenerationFailure failure) => failure switch
    {
        EmbeddingGenerationFailure.CredentialRejected =>
            $"Embedding endpoint '{endpointAlias}' refused the configured credential. Rotate or correct it; repeating the request cannot change the answer.",
        EmbeddingGenerationFailure.RateLimited =>
            $"Embedding endpoint '{endpointAlias}' refused the request because the deployment is over its allowed rate.",
        EmbeddingGenerationFailure.RequestTimedOut =>
            $"Embedding endpoint '{endpointAlias}' did not answer within the time configured for one embedding request.",
        EmbeddingGenerationFailure.TransportFaulted =>
            $"Embedding endpoint '{endpointAlias}' could not be reached, or the answer it began was unreadable.",
        EmbeddingGenerationFailure.RequestRefused =>
            $"Embedding endpoint '{endpointAlias}' rejected the request itself. Check the declared model and the configured input bounds.",
        EmbeddingGenerationFailure.VectorShapeUnexpected =>
            $"Embedding endpoint '{endpointAlias}' answered with a vector the declared geometry does not describe.",
        _ => $"Embedding endpoint '{endpointAlias}' did not produce vectors.",
    };
}
