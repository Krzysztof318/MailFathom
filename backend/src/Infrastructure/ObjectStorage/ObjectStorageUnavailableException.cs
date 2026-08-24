// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Failures;

namespace MailFathom.Infrastructure.ObjectStorage;

/// <summary>The failure an operation against the configured object-storage endpoint is reported as.</summary>
/// <remarks>
/// <para>
/// One exception type carries every classification <see cref="ObjectStorageFailure" /> declares except the caller's own
/// cancellation, which is rethrown unchanged so a caller that went away and an endpoint that refused work never arrive
/// as one failure. The code is the classification's rather than the type's, because what an operator does about a
/// refused credential and about an unreachable endpoint are different acts and an alert has to tell them apart.
/// </para>
/// <para>
/// <b>No message here carries the endpoint address, the bucket, an object key, or any part of a credential.</b> Each
/// names the configuration key an operator edits instead, which is the one thing they need told: the value is already
/// in the file they would open, while <c>backend/src/AGENTS.md</c> § <i>Failures</i> lists a host name beside a
/// credential among the things a message may never carry. The endpoint's own answer stays on the inner exception, which
/// is diagnostic detail for a log rather than something a boundary republishes.
/// </para>
/// </remarks>
public sealed class ObjectStorageUnavailableException : MailFathomException
{
    private ObjectStorageUnavailableException(
        string operatorSafeMessage,
        ObjectStorageFailure failure,
        Exception innerException)
        : base(operatorSafeMessage, innerException) => this.Failure = failure;

    /// <summary>Gets what ended the operation.</summary>
    public ObjectStorageFailure Failure { get; }

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => this.Failure.ErrorCode;

    /// <summary>Reports an operation that ended in a classified failure.</summary>
    /// <param name="failure">What ended it, which decides the code and whether a repetition is worthwhile.</param>
    /// <param name="cause">The endpoint's own failure, kept as diagnostic detail rather than republished.</param>
    /// <returns>The failure to raise.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="cause" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="failure" /> is the unspecified struct default, which names no classification and carries no code.</exception>
    public static ObjectStorageUnavailableException From(ObjectStorageFailure failure, Exception cause)
    {
        ArgumentNullException.ThrowIfNull(cause);

        if (!failure.IsSpecified)
        {
            throw new ArgumentException(
                "An object-storage failure must be raised under a declared classification, because the classification is what supplies its code.",
                nameof(failure));
        }

        return new ObjectStorageUnavailableException(DescribeFailure(failure), failure, cause);
    }

    private static string DescribeFailure(ObjectStorageFailure failure) => failure switch
    {
        var refused when refused == ObjectStorageFailure.AuthenticationFailed =>
            "The object-storage endpoint refused the credential MailFathom presented. Check that ContentStorage:ObjectStorage:AccessKeyId and ContentStorage:ObjectStorage:SecretAccessKey reference material the endpoint still accepts, and that it grants that identity the configured bucket.",

        var slow when slow == ObjectStorageFailure.TimedOut =>
            "The object-storage endpoint did not answer within the budget one attempt is given. Raise Resilience:ObjectStorageInvocation:AttemptTimeout, or give the endpoint the resources it needs.",

        var stopping when stopping == ObjectStorageFailure.HostShuttingDown =>
            "The operation against the object-storage endpoint was abandoned because the host is shutting down.",

        var unreachable when unreachable == ObjectStorageFailure.TransientTransportFailure =>
            "The object-storage endpoint could not be reached. Check that ContentStorage:ObjectStorage:Endpoint names a reachable address, that its certificate chains to an authority this deployment trusts, and that the endpoint is serving.",

        _ => "An operation against the object-storage endpoint failed in a way MailFathom does not recognize. The endpoint's own answer is in the log record beside this one.",
    };
}
