// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ClientModel;
using System.Net;
using System.Net.Sockets;
using MailFathom.Application.Emails.Embeddings;

namespace MailFathom.AI.ProviderAdapters;

/// <summary>Turns whatever a provider client threw into one of the classifications the port publishes.</summary>
/// <remarks>
/// <para>
/// This is the deliverable rather than a detail of one. Without it every provider failure is "the call failed", and
/// the two decisions that follow are made wrongly: a rate limit answered with an immediate retry is how an account
/// gets throttled harder, and a refused credential repeated is how the same refusal is bought again while the account
/// carries the requests.
/// </para>
/// <para>
/// The verdict is read from the failure's type and its HTTP status alone. Nothing from a response body, a request
/// payload, or a credential takes part in it, and nothing from any of them is carried into the failure this produces —
/// a provider error body quotes the request, and the request is mail text.
/// </para>
/// </remarks>
internal static class EmbeddingProviderFailureClassification
{
    /// <summary>Classifies a failure raised while calling a provider.</summary>
    /// <param name="failure">The failure the call produced.</param>
    /// <returns>The classification, or <see langword="null" /> when the failure is not one a provider produced.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="failure" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A caller's own cancellation and a host shutdown are deliberately unclassified. Both arrive as
    /// <see cref="OperationCanceledException" /> and neither says anything about the provider, so reporting one as a
    /// provider failure would let this system's own decision open a circuit against a healthy endpoint.
    /// </remarks>
    public static EmbeddingGenerationFailure? Classify(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return failure switch
        {
            OperationCanceledException => null,
            ClientResultException refusal => ClassifyStatus((HttpStatusCode)refusal.Status),
            HttpRequestException transportFailure => transportFailure.StatusCode is { } status
                ? ClassifyStatus(status)
                : EmbeddingGenerationFailure.TransportFaulted,
            TimeoutException => EmbeddingGenerationFailure.RequestTimedOut,
            SocketException or IOException => EmbeddingGenerationFailure.TransportFaulted,
            _ => null,
        };
    }

    /// <summary>Classifies a provider's answer from its status alone.</summary>
    /// <remarks>
    /// <para>
    /// A status of zero is what a client library reports when the request never reached one, which is a transport
    /// fault rather than a refusal. `408` and `504` are the two the provider itself calls a timeout; everything else
    /// in the `5xx` class is the provider failing rather than deciding, and both are worth another attempt.
    /// </para>
    /// <para>
    /// `403` joins `401` rather than the refusals below it. The two are different statements — one says the credential
    /// is unknown, the other that it is known and not permitted here — but the operator's move is the same for both
    /// and repeating either buys the same answer, so they share a classification.
    /// </para>
    /// </remarks>
    private static EmbeddingGenerationFailure ClassifyStatus(HttpStatusCode status) => status switch
    {
        0 => EmbeddingGenerationFailure.TransportFaulted,
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => EmbeddingGenerationFailure.CredentialRejected,
        HttpStatusCode.TooManyRequests => EmbeddingGenerationFailure.RateLimited,
        HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout => EmbeddingGenerationFailure.RequestTimedOut,
        _ => (int)status >= 500
            ? EmbeddingGenerationFailure.TransportFaulted
            : EmbeddingGenerationFailure.RequestRefused,
    };
}
