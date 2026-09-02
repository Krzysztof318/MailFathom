// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Net.Sockets;
using Amazon.Runtime;
using MailFathom.Infrastructure.Resilience;

namespace MailFathom.Infrastructure.ObjectStorage;

/// <summary>Decides what ended one operation against the object-storage endpoint.</summary>
/// <remarks>
/// <para>
/// The decision is made from the failure's type, its HTTP status, and the endpoint's own error code alone. Nothing from
/// an object key, a bucket name, a credential, or a response body takes part in it, and nothing from the failure is
/// recorded here.
/// </para>
/// <para>
/// The three cancellation shapes are separated first and by the tokens rather than by the exception, because .NET gives
/// all three the same type. A caller that abandoned a read, a host that is stopping, and an endpoint that never answered
/// are three different facts, and only the last of them is worth attempting again.
/// </para>
/// <para>
/// A limit the pipeline itself imposed is separated before any of that, because it is the one shape that reaches here
/// without the endpoint having said anything at all.
/// </para>
/// <para>
/// Everything unrecognized is <see cref="ObjectStorageFailure.Unrecognized" /> and therefore terminal, on the reasoning
/// every family in <see cref="TransientFailureClassifier" /> follows: a rejection whose meaning is unknown is
/// not one a repetition improves on.
/// </para>
/// </remarks>
internal static class ObjectStorageFailureClassifier
{
    /// <summary>The endpoint error codes that name a credential the endpoint will go on refusing.</summary>
    /// <remarks>
    /// A status is not enough on its own. S3 answers a missing object under a policy that grants no <c>ListBucket</c>
    /// with <c>403</c> and <c>AccessDenied</c>, and answers a wrong signature with <c>403</c> too, so the code is what
    /// separates a credential that is wrong from one that is merely narrow — and both are the same act for an operator,
    /// which is why they share a classification rather than a message.
    /// </remarks>
    private static readonly string[] CredentialRefusalCodes =
    [
        "AccessDenied",
        "AccountProblem",
        "AuthorizationHeaderMalformed",
        "ExpiredToken",
        "InvalidAccessKeyId",
        "InvalidSecurity",
        "RequestTimeTooSkewed",
        "SignatureDoesNotMatch",
        "TokenRefreshRequired",
    ];

    /// <summary>Classifies what ended an operation.</summary>
    /// <param name="failure">The failure the operation ended with.</param>
    /// <param name="callerToken">The token the caller passed in, which is what makes a cancellation the caller's own.</param>
    /// <param name="shutdownToken">The host's stopping token, which is what makes a cancellation a shutdown.</param>
    /// <returns>The classification, which supplies the code a boundary reports and decides whether a repetition is worthwhile.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="failure" /> is <see langword="null" />.</exception>
    internal static ObjectStorageFailure Classify(
        Exception failure,
        CancellationToken callerToken,
        CancellationToken shutdownToken)
    {
        ArgumentNullException.ThrowIfNull(failure);

        if (failure is OutboundDependencyUnavailableException pipelineRejection)
        {
            // A limit the pipeline imposed says nothing about what the endpoint answered, because no answer arrived:
            // the operation either outlived its budget or was refused before it left this process. Reporting either as
            // unrecognized would send an operator looking for an endpoint answer that does not exist, and would call a
            // shed probe terminal when it is the one thing a readiness scrape exists to keep asking about.
            return pipelineRejection.ExhaustedItsTimeBudget
                ? ObjectStorageFailure.TimedOut
                : ObjectStorageFailure.TransientTransportFailure;
        }

        if (failure is OperationCanceledException)
        {
            // The caller is asked about first, so a shutdown that reached a caller which had already cancelled is still
            // reported as the caller's: a request nobody is waiting for is nobody's work to resume.
            return callerToken.IsCancellationRequested
                ? ObjectStorageFailure.CallerCancelled
                : shutdownToken.IsCancellationRequested
                    ? ObjectStorageFailure.HostShuttingDown
                    : ObjectStorageFailure.TimedOut;
        }

        return failure switch
        {
            TimeoutException => ObjectStorageFailure.TimedOut,
            AmazonServiceException answered => ClassifyAnswer(answered),
            AmazonClientException => ObjectStorageFailure.TransientTransportFailure,
            _ => ClassifyTransportFailure(failure),
        };
    }

    /// <summary>Classifies an answer the endpoint itself composed.</summary>
    /// <remarks>
    /// A status the answer never carried is a request that failed before one arrived, which the SDK surfaces as this
    /// same type with the status left at its default; it is transport rather than a decision the endpoint took.
    /// </remarks>
    private static ObjectStorageFailure ClassifyAnswer(AmazonServiceException answer)
    {
        if (answer.ErrorCode is { Length: > 0 } errorCode
            && CredentialRefusalCodes.Contains(errorCode, StringComparer.Ordinal))
        {
            return ObjectStorageFailure.AuthenticationFailed;
        }

        return answer.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => ObjectStorageFailure.AuthenticationFailed,
            HttpStatusCode.RequestTimeout => ObjectStorageFailure.TimedOut,
            HttpStatusCode.TooManyRequests => ObjectStorageFailure.TransientTransportFailure,
            0 => ObjectStorageFailure.TransientTransportFailure,
            var status when (int)status >= 500 => ObjectStorageFailure.TransientTransportFailure,
            _ => ObjectStorageFailure.Unrecognized,
        };
    }

    private static ObjectStorageFailure ClassifyTransportFailure(Exception failure) =>
        failure is HttpRequestException or SocketException or IOException
            ? ObjectStorageFailure.TransientTransportFailure
            : ObjectStorageFailure.Unrecognized;
}
