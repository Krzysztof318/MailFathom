// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using MailFathom.Common.Observability;

namespace MailFathom.Infrastructure.Mail;

/// <summary>Names whether an IMAP connection carries bounded work or waits indefinitely for a notification.</summary>
internal enum MailServerConnectionPurpose
{
    /// <summary>A synchronization, discovery, or mailbox-write connection.</summary>
    Work = 0,

    /// <summary>A push connection that may remain open for the process lifetime.</summary>
    PushNotification = 1,
}

/// <summary>Bounds open or establishing IMAP connections by server host across every account and owner.</summary>
public sealed class MailServerConnectionBudget : IDisposable
{
    /// <summary>The host ceiling used when the composition root supplies none.</summary>
    public const int DefaultMaximumConnectionsPerHost = 20;

    internal const string HostTagName = "mailfathom.mail.server.host";

    private readonly ConcurrentDictionary<string, HostBudget> hosts = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource disposalCancellation = new();
    private readonly int maximumConnectionsPerHost;
    private int disposed;

    /// <summary>Initializes the process-wide host budgets.</summary>
    /// <param name="maximumConnectionsPerHost">The greatest number of IMAP connections one host may hold.</param>
    public MailServerConnectionBudget(int maximumConnectionsPerHost)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumConnectionsPerHost, 2);

        this.maximumConnectionsPerHost = maximumConnectionsPerHost;

        Telemetry.Meter.CreateObservableGauge(
            "mailfathom.mail.server.connections.limit",
            this.ObserveLimits,
            unit: "{connection}",
            description: "Open or establishing IMAP connections each server host admits across every owner.");
        Telemetry.Meter.CreateObservableGauge(
            "mailfathom.mail.server.connections.active",
            this.ObserveActiveConnections,
            unit: "{connection}",
            description: "Open or establishing IMAP connections currently holding each server host's budget.");
        Telemetry.Meter.CreateObservableGauge(
            "mailfathom.mail.server.connections.queued",
            this.ObserveQueuedConnections,
            unit: "{connection}",
            description: "IMAP connection attempts waiting for each server host's budget.");
    }

    /// <summary>Waits for one host slot and returns the lease that holds it until the socket closes.</summary>
    /// <param name="host">The server host, compared without regard to DNS casing.</param>
    /// <param name="purpose">Whether the connection may remain open waiting for push notifications.</param>
    /// <param name="cancellationToken">Cancels the wait without consuming a slot.</param>
    internal Task<IDisposable> AcquireAsync(
        string host,
        MailServerConnectionPurpose purpose,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref this.disposed) != 0, this);

        return this.hosts
            .GetOrAdd(
                host.Trim(),
                key => new HostBudget(key, this.maximumConnectionsPerHost, this.disposalCancellation.Token))
            .AcquireAsync(purpose, cancellationToken);
    }

    private IEnumerable<Measurement<long>> ObserveLimits() =>
        this.hosts.Values.Select(static host => host.Measure(host.MaximumConnections));

    private IEnumerable<Measurement<long>> ObserveActiveConnections() =>
        this.hosts.Values.Select(static host => host.Measure(host.ActiveConnections));

    private IEnumerable<Measurement<long>> ObserveQueuedConnections() =>
        this.hosts.Values.Select(static host => host.Measure(host.QueuedConnections));

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref this.disposed, 1) != 0)
        {
            return;
        }

        this.disposalCancellation.Cancel();
        this.disposalCancellation.Dispose();
    }

    /// <summary>One host's total ceiling and the smaller ceiling its long-lived push connections share.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "The process-wide semaphores never expose AvailableWaitHandle and remain valid until every admitted lease releases after shutdown cancellation.")]
    private sealed class HostBudget(
        string host,
        int maximumConnections,
        CancellationToken disposalToken)
    {
        private readonly SemaphoreSlim allConnections = new(maximumConnections, maximumConnections);
        private readonly SemaphoreSlim pushConnections = new(maximumConnections - 1, maximumConnections - 1);
        private long activeConnections;
        private long queuedConnections;

        internal long MaximumConnections => maximumConnections;

        internal long ActiveConnections => Volatile.Read(ref this.activeConnections);

        internal long QueuedConnections => Volatile.Read(ref this.queuedConnections);

        internal async Task<IDisposable> AcquireAsync(
            MailServerConnectionPurpose purpose,
            CancellationToken cancellationToken)
        {
            var pushSlotHeld = false;
            Interlocked.Increment(ref this.queuedConnections);
            using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                disposalToken);

            try
            {
                if (purpose == MailServerConnectionPurpose.PushNotification)
                {
                    await this.pushConnections.WaitAsync(waitCancellation.Token);
                    pushSlotHeld = true;
                }

                await this.allConnections.WaitAsync(waitCancellation.Token);
                Interlocked.Increment(ref this.activeConnections);

                return new ConnectionLease(this, pushSlotHeld);
            }
            catch (OperationCanceledException) when (
                disposalToken.IsCancellationRequested
                && !cancellationToken.IsCancellationRequested)
            {
                throw new ObjectDisposedException(nameof(MailServerConnectionBudget));
            }
            catch
            {
                if (pushSlotHeld)
                {
                    this.pushConnections.Release();
                }

                throw;
            }
            finally
            {
                Interlocked.Decrement(ref this.queuedConnections);
            }
        }

        private void Release(bool pushSlotHeld)
        {
            Interlocked.Decrement(ref this.activeConnections);
            this.allConnections.Release();

            if (pushSlotHeld)
            {
                this.pushConnections.Release();
            }
        }

        internal Measurement<long> Measure(long value) => new(
            value,
            new KeyValuePair<string, object?>(HostTagName, host));

        private sealed class ConnectionLease(HostBudget budget, bool pushSlotHeld) : IDisposable
        {
            private int disposed;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref this.disposed, 1) == 0)
                {
                    budget.Release(pushSlotHeld);
                }
            }
        }
    }
}
