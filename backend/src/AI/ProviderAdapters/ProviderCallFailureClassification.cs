// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ClientModel;
using System.Net;
using System.Net.Sockets;

namespace MailFathom.AI.ProviderAdapters;

/// <summary>Turns whatever a provider client threw into one of the classifications a calling boundary publishes.</summary>
/// <remarks>
/// <para>
/// This is the deliverable rather than a detail of one. Without it every provider failure is "the call failed", and
/// the two decisions that follow are made wrongly: a rate limit answered with an immediate retry is how an account
/// gets throttled harder, and a refused credential repeated is how the same refusal is bought again while the account
/// carries the requests.
/// </para>
/// <para>
/// One classifier serves every provider role. What an HTTP status means about the remote party does not depend on what
/// was asked of it, so an embedding call and a chat call read a <c>429</c> identically and differ only in what they do
/// next — which is why each maps this vocabulary into a failure enumeration of its own instead of receiving one.
/// </para>
/// <para>
/// The verdict is read from the failure's type and its HTTP status alone. Nothing from a response body, a request
/// payload, or a credential takes part in it, and nothing from any of them is carried into the failure this produces —
/// a provider error body quotes the request, and a request is mail text or somebody's question.
/// </para>
/// </remarks>
internal static class ProviderCallFailureClassification
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
    public static ProviderCallFailure? Classify(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return failure switch
        {
            OperationCanceledException => null,
            ClientResultException refusal => ClassifyStatus((HttpStatusCode)refusal.Status),
            HttpRequestException transportFailure => transportFailure.StatusCode is { } status
                ? ClassifyStatus(status)
                : ProviderCallFailure.TransportFaulted,
            TimeoutException => ProviderCallFailure.RequestTimedOut,
            SocketException or IOException => ProviderCallFailure.TransportFaulted,
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
    private static ProviderCallFailure ClassifyStatus(HttpStatusCode status) => status switch
    {
        0 => ProviderCallFailure.TransportFaulted,
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => ProviderCallFailure.CredentialRejected,
        HttpStatusCode.TooManyRequests => ProviderCallFailure.RateLimited,
        HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout => ProviderCallFailure.RequestTimedOut,
        _ => (int)status >= 500
            ? ProviderCallFailure.TransportFaulted
            : ProviderCallFailure.RequestRefused,
    };
}
