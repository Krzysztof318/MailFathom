// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Infrastructure.Secrets.Resolution;

namespace MailFathom.Infrastructure.Secrets.Sources;

/// <summary>Retrieves one secret file under a deadline, bounding how many opens the platform may hold blocked.</summary>
/// <remarks>
/// <para>
/// Opening a file is the one step of a retrieval that no cancellation token reaches. The kernel returns from
/// <c>open</c> when it is ready to, which for a FIFO with no writer is never and for a stalled network mount is
/// whenever the storage answers, so a token passed to the read behind it protects nothing. Startup resolves every
/// reference before workers start, which turns one such target into a host that neither starts nor says why.
/// </para>
/// <para>
/// The deadline is therefore imposed from outside the call rather than inside it: the open runs on a thread pool
/// thread and the caller stops waiting for it when
/// <see cref="SecretMaterialLimits.RetrievalDeadline" /> passes, reporting
/// <see cref="SecretResolutionFailure.RetrievalTimedOut" />. What that trades is stated plainly rather than hidden.
/// <b>The abandoned thread stays blocked in the kernel</b> — nothing can interrupt it — and it is released only when
/// the platform call finally returns, at which point the stream it produced is disposed and the permit it holds is
/// given back. <see cref="SecretMaterialLimits.MaximumConcurrentRetrievalCount" /> is what keeps that from being an
/// unbounded leak, because a stuck retrieval keeps its permit and the next one waits for a permit rather than for
/// another thread.
/// </para>
/// <para>
/// A retrieval that fails this way is still a result rather than an exception, so an unreachable target costs its
/// place in the aggregated startup report and nothing more.
/// </para>
/// </remarks>
internal sealed class BoundedSecretFileRetrieval(TimeProvider timeProvider) : IDisposable
{
    private readonly SemaphoreSlim availableRetrievals = new(SecretMaterialLimits.MaximumConcurrentRetrievalCount);

    /// <summary>Opens the target and reads its material, both within the retrieval deadline.</summary>
    /// <param name="openTarget">Opens the provisioned file, answering <see langword="null" /> for every refusal the file system expresses as an exception.</param>
    /// <param name="maximumByteCount">The ceiling enforced while reading.</param>
    /// <param name="cancellationToken">Cancels the retrieval.</param>
    /// <returns>The owned material, or the named failure that explains its absence.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="openTarget" /> is <see langword="null" />.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels, which the deadline expiring deliberately is not.</exception>
    [SuppressMessage("Reliability", "CA2025:Ensure tasks using IDisposable instances complete before the instances are disposed", Justification = "Outliving this scope is what the abandoned release is for; it touches neither token source, only the gate's own semaphore and the open it was handed.")]
    internal async Task<SecretResolutionResult> ReadAsync(
        Func<Stream?> openTarget,
        int maximumByteCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(openTarget);

        using var deadline = new CancellationTokenSource(SecretMaterialLimits.RetrievalDeadline, timeProvider);
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);

        try
        {
            await this.availableRetrievals.WaitAsync(bounded.Token);
        }
        catch (OperationCanceledException)
        {
            return ReportUnlessTheCallerCancelled(cancellationToken);
        }

        var open = Task.Run(openTarget, CancellationToken.None);

        Stream? target;
        try
        {
            target = await open.WaitAsync(bounded.Token);
        }
        catch (OperationCanceledException)
        {
            _ = this.ReleaseWhenTheAbandonedOpenReturnsAsync(open);

            return ReportUnlessTheCallerCancelled(cancellationToken);
        }
        catch (Exception)
        {
            // An open that failed in a way the adapter does not translate is still an open that finished, so its
            // permit goes back here rather than being lost with the exception on its way out.
            this.availableRetrievals.Release();

            throw;
        }

        try
        {
            if (target is null)
            {
                return SecretResolutionResult.Failed(SecretResolutionFailure.MaterialNotFound);
            }

            await using (target)
            {
                return await BoundedSecretMaterialReader.ReadAsync(target, maximumByteCount, bounded.Token);
            }
        }
        catch (OperationCanceledException)
        {
            return ReportUnlessTheCallerCancelled(cancellationToken);
        }
        finally
        {
            this.availableRetrievals.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => this.availableRetrievals.Dispose();

    /// <summary>Distinguishes the deadline expiring, which is a reportable failure, from the caller cancelling.</summary>
    private static SecretResolutionResult ReportUnlessTheCallerCancelled(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return SecretResolutionResult.Failed(SecretResolutionFailure.RetrievalTimedOut);
    }

    /// <summary>Gives the permit back, and disposes the stream nobody is waiting for, if the open ever returns.</summary>
    /// <remarks>
    /// This is deliberately not awaited: waiting for it is the thing the deadline exists to stop doing. It is the only
    /// place a permit taken by an abandoned retrieval can come back, which is why it observes every way the open can
    /// end rather than only the one that produces a stream.
    /// </remarks>
    private async Task ReleaseWhenTheAbandonedOpenReturnsAsync(Task<Stream?> abandonedOpen)
    {
        var abandonedTarget = await ObserveTheOutcomeAsync(abandonedOpen);

        abandonedTarget?.Dispose();

        try
        {
            this.availableRetrievals.Release();
        }
        catch (ObjectDisposedException)
        {
            // The platform answered after shutdown disposed this gate, so there is nobody left to admit.
        }
    }

    /// <summary>Waits for an open nobody is waiting for, answering the stream it produced or none if it failed.</summary>
    /// <remarks>
    /// Awaiting is what marks a failure observed. Unobserved, it would surface much later against whatever unrelated
    /// work the finalizer thread happened to be running, and the retrieval it belonged to has already been reported.
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The abandoned retrieval was already reported as a deadline, so any way its open ends is an outcome nobody is left to act on.")]
    private static async Task<Stream?> ObserveTheOutcomeAsync(Task<Stream?> abandonedOpen)
    {
        try
        {
            return await abandonedOpen;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
