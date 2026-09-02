// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Resilience;
using MailFathom.Infrastructure.Observability;
using MailFathom.Infrastructure.Resilience;
using Microsoft.Extensions.Hosting;

namespace MailFathom.Infrastructure.ObjectStorage;

/// <summary>Runs one request to the object-storage endpoint under its resilience budget, measures it, and classifies what stopped it.</summary>
/// <remarks>
/// <para>
/// Every call this system makes to the endpoint goes through here, which is what keeps the readiness probe and the
/// content adapter bounded, retried, and classified on identical terms. The alternative was the same forty lines in
/// both, where the two would agree until one of them was corrected.
/// </para>
/// <para>
/// One logical operation is retried at exactly one layer, so a caller with several requests to make runs them one after
/// another through this type rather than nesting them: re-entering the resilience class on one flow is refused by the
/// executor itself.
/// </para>
/// </remarks>
internal sealed class ObjectStorageOperationRunner
{
    private readonly OutboundOperationExecutor operationExecutor;
    private readonly ObjectStorageTelemetry telemetry;
    private readonly IHostApplicationLifetime applicationLifetime;

    /// <summary>Initializes the runner.</summary>
    /// <param name="operationExecutor">Runs each request under the object-storage resilience budget.</param>
    /// <param name="telemetry">Publishes what each request cost and what stopped it.</param>
    /// <param name="applicationLifetime">Supplies the stopping token that tells a shutdown from a caller giving up.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public ObjectStorageOperationRunner(
        OutboundOperationExecutor operationExecutor,
        ObjectStorageTelemetry telemetry,
        IHostApplicationLifetime applicationLifetime)
    {
        ArgumentNullException.ThrowIfNull(operationExecutor);
        ArgumentNullException.ThrowIfNull(telemetry);
        ArgumentNullException.ThrowIfNull(applicationLifetime);

        this.operationExecutor = operationExecutor;
        this.telemetry = telemetry;
        this.applicationLifetime = applicationLifetime;
    }

    /// <summary>Makes one request, and answers with what the endpoint said.</summary>
    /// <typeparam name="TAnswer">What the request answers with.</typeparam>
    /// <param name="operation">The published name this request is measured under.</param>
    /// <param name="request">The request itself, given the attempt's own cancellation.</param>
    /// <param name="measurePayload">Reads the byte volume out of the answer, or answers <see langword="null" /> when the request moves no payload.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>What the endpoint answered.</returns>
    /// <exception cref="ObjectStorageUnavailableException">Thrown when the endpoint did not answer, carrying the classification an operator acts on.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller abandoned the operation, which is recorded and never translated.</exception>
    /// <remarks>
    /// A caller's own cancellation is recorded and rethrown rather than translated, so an operation the caller abandoned
    /// stays what it was: a fact about the caller, not an endpoint that failed.
    /// </remarks>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Whatever stopped the request, it is classified into what an operator acts on and rethrown; narrowing the catch would let an unrecognized failure reach a caller uncoded.")]
    public async Task<TAnswer> RunAsync<TAnswer>(
        string operation,
        Func<CancellationToken, Task<TAnswer>> request,
        Func<TAnswer, long?> measurePayload,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(measurePayload);

        using var measurement = this.telemetry.Begin(operation);

        try
        {
            var answer = await this.operationExecutor.ExecuteAsync(
                OutboundDependency.ObjectStorageInvocation,
                attemptToken => this.AttemptAsync(request, attemptToken, cancellationToken),
                cancellationToken);

            measurement.Succeeded(measurePayload(answer));

            return answer;
        }
        catch (Exception failure)
        {
            // An attempt has already classified what it met, so its verdict is read rather than derived a second time
            // from a wrapper the classifier would not recognize.
            var classification = failure is ObjectStorageUnavailableException alreadyClassified
                ? alreadyClassified.Failure
                : ObjectStorageFailureClassifier.Classify(
                    failure,
                    cancellationToken,
                    this.applicationLifetime.ApplicationStopping);

            measurement.Failed(classification);

            if (failure is ObjectStorageUnavailableException
                || classification == ObjectStorageFailure.CallerCancelled)
            {
                throw;
            }

            throw ObjectStorageUnavailableException.From(classification, failure);
        }
    }

    /// <summary>Makes one attempt and classifies whatever ended it, before the pipeline decides whether to repeat it.</summary>
    /// <remarks>
    /// The translation belongs inside the attempt rather than around the whole operation, because the retry and the
    /// circuit breaker judge the exception an attempt threw: handed the AWS client's own type they would fall through to
    /// the transport rules, which match neither a <c>5xx</c> nor a <c>429</c> the endpoint answered, and the configured
    /// budget would collapse to a single attempt. Translating here is what makes the classification a caller reads and
    /// the one the pipeline acts on the same value. <c>MailOAuthAccessTokenSource</c> translates inside its own attempt
    /// for the same reason.
    /// <para>
    /// Cancellation passes through untouched, in all three of its shapes. An attempt cut by the pipeline's own timeout
    /// is cancelled through <paramref name="attemptToken" />, and the timeout strategy recognizes that only as the
    /// <see cref="OperationCanceledException" /> it raised; a translation here would leave it looking like a failure the
    /// endpoint produced.
    /// </para>
    /// </remarks>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Every failure an attempt can meet is classified into what the pipeline acts on; narrowing the catch would leave a class of them judged by the transport rules instead.")]
    private async Task<TAnswer> AttemptAsync<TAnswer>(
        Func<CancellationToken, Task<TAnswer>> request,
        CancellationToken attemptToken,
        CancellationToken cancellationToken)
    {
        try
        {
            return await request(attemptToken);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            var classification = ObjectStorageFailureClassifier.Classify(
                failure,
                cancellationToken,
                this.applicationLifetime.ApplicationStopping);

            throw ObjectStorageUnavailableException.From(classification, failure);
        }
    }
}
