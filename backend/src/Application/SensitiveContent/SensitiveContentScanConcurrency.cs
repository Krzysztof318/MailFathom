// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.SensitiveContent;

/// <summary>The one budget of scans this process runs at once, whichever owner's mail each of them is reading.</summary>
/// <remarks>
/// <para>
/// It is a type of its own rather than a field of <see cref="Redaction.SensitiveContentRedactor" /> because a
/// deployment composes one redaction per owner posture and the permits are the process's CPU and memory rather than
/// any owner's. A semaphore inside each redaction would multiply the bound by the number of distinct postures, so a
/// deployment that admitted a second owner would silently double what one analyzer is asked to serve.
/// </para>
/// <para>
/// The bound is the deployment's for the same reason the analyzer address is: the machine is shared, and no owner's
/// document may buy more of it.
/// </para>
/// </remarks>
public sealed class SensitiveContentScanConcurrency : IDisposable
{
    private readonly SemaphoreSlim permits;

    /// <summary>Initializes the budget of a deployment.</summary>
    /// <param name="maximumConcurrentScans">How many scans may run at once across this process.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maximumConcurrentScans" /> is not positive.</exception>
    public SensitiveContentScanConcurrency(int maximumConcurrentScans)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumConcurrentScans);

        this.permits = new SemaphoreSlim(maximumConcurrentScans, maximumConcurrentScans);
    }

    /// <summary>Waits for one permit and returns what releases it.</summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The held permit, released when it is disposed.</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken" /> is cancelled.</exception>
    /// <remarks>
    /// The permit is published as <see cref="IDisposable" /> rather than as a type of its own, because it is a scope a
    /// caller holds rather than a value one compares: the shape is <c>BeginScope</c>'s, and the allocation it costs is
    /// one per scan beside the task the wait already allocated.
    /// </remarks>
    public async Task<IDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        await this.permits.WaitAsync(cancellationToken);

        return new Permit(this.permits);
    }

    /// <inheritdoc />
    public void Dispose() => this.permits.Dispose();

    /// <summary>One held permit, which releases itself when the scan that took it finishes.</summary>
    private sealed class Permit(SemaphoreSlim permits) : IDisposable
    {
        public void Dispose() => permits.Release();
    }
}
