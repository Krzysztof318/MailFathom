// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Application.SensitiveContent.Redaction;
using MailFathom.Domain.Access;

namespace MailFathom.TestSupport;

/// <summary>Records what the egress guard reported, so a test can assert it without a metric listener.</summary>
/// <remarks>
/// Hand-written rather than substituted because what these tests assert is a sequence — which egress points were
/// guarded, in which order, which of them a scanner could not answer for, and which of them a screen stopped — and a
/// recorded list reports that without a matcher. It is shared because every boundary holding a guarded egress point
/// needs the same one.
/// </remarks>
internal sealed class RecordingSensitiveContentEgressTelemetry : ISensitiveContentEgressTelemetry
{
    private readonly List<GuardedEgress> guarded = [];
    private readonly List<RefusedEgress> refused = [];
    private readonly List<StoppedEgress> stopped = [];
    private readonly List<GuardedOperation> operations = [];

    /// <summary>Gets every text that passed a guard, in the order the guards ran.</summary>
    public IReadOnlyList<GuardedEgress> Guarded => this.guarded;

    /// <summary>Gets every egress a scanner refused, in the order the refusals happened.</summary>
    public IReadOnlyList<RefusedEgress> Refused => this.refused;

    /// <summary>Gets every act a screen stopped, in the order the refusals happened.</summary>
    public IReadOnlyList<StoppedEgress> Stopped => this.stopped;

    /// <summary>Gets every guarded operation that was opened, in the order they began.</summary>
    public IReadOnlyList<GuardedOperation> Operations => this.operations;

    /// <inheritdoc />
    public void RecordGuarded(SensitiveContentEgressPoint egressPoint, RedactedText redacted, TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(redacted);

        this.guarded.Add(new GuardedEgress(egressPoint, redacted, elapsed));
    }

    /// <inheritdoc />
    public void RecordRefused(SensitiveContentEgressPoint egressPoint, SensitiveContentScannerKind scanner) =>
        this.refused.Add(new RefusedEgress(egressPoint, scanner));

    /// <inheritdoc />
    public void RecordStopped(SensitiveContentEgressPoint egressPoint, SensitiveContentEgressRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);

        this.stopped.Add(new StoppedEgress(egressPoint, refusal));
    }

    /// <inheritdoc />
    public ISensitiveContentGuardScope BeginGuardedOperation(
        SensitiveContentEgressPoint egressPoint,
        MailOwnerId owner,
        CancellationToken cancellationToken)
    {
        var operation = new GuardedOperation(egressPoint, owner, cancellationToken);
        this.operations.Add(operation);

        return operation;
    }

    /// <summary>One text that passed a guard.</summary>
    /// <param name="EgressPoint">Where it was going.</param>
    /// <param name="Redacted">What the redaction produced.</param>
    /// <param name="Elapsed">What the scan added to the operation.</param>
    internal sealed record GuardedEgress(
        SensitiveContentEgressPoint EgressPoint,
        RedactedText Redacted,
        TimeSpan Elapsed);

    /// <summary>One guarded operation and what was reported into it before its scope was closed.</summary>
    /// <param name="egressPoint">Where the texts this operation guarded were going.</param>
    /// <param name="owner">Whose mail the operation was publishing.</param>
    /// <param name="cancellationToken">The token the consumer opened the operation with, which a test reads to tell a shutdown apart.</param>
    internal sealed class GuardedOperation(
        SensitiveContentEgressPoint egressPoint,
        MailOwnerId owner,
        CancellationToken cancellationToken) : ISensitiveContentGuardScope
    {
        /// <summary>Gets where the texts this operation guarded were going.</summary>
        public SensitiveContentEgressPoint EgressPoint => egressPoint;

        /// <summary>Gets whose mail the operation was publishing.</summary>
        public MailOwnerId Owner => owner;

        /// <summary>Gets the token the consumer opened the operation with.</summary>
        public CancellationToken CancellationToken => cancellationToken;

        /// <summary>Gets how many texts were scanned inside this operation.</summary>
        public int GuardedTextCount { get; private set; }

        /// <summary>Gets whether a scanner refused the operation.</summary>
        public bool WasRefused { get; private set; }

        /// <summary>Gets whether the consumer reported the operation as having guarded everything it was going to.</summary>
        public bool WasCompleted { get; private set; }

        /// <summary>Gets whether the scope was closed, which a guarded operation always is.</summary>
        public bool WasClosed { get; private set; }

        /// <inheritdoc />
        public void TextGuarded() => this.GuardedTextCount++;

        /// <inheritdoc />
        public void Refused() => this.WasRefused = true;

        /// <inheritdoc />
        public void Completed() => this.WasCompleted = true;

        /// <inheritdoc />
        public void Dispose() => this.WasClosed = true;
    }

    /// <summary>One egress a scanner refused.</summary>
    /// <param name="EgressPoint">Where the text was going, and did not.</param>
    /// <param name="Scanner">The scanner that could not answer.</param>
    internal sealed record RefusedEgress(SensitiveContentEgressPoint EgressPoint, SensitiveContentScannerKind Scanner);

    /// <summary>One act a screen stopped because of what the text carried.</summary>
    /// <param name="EgressPoint">Where the text was going, and did not.</param>
    /// <param name="Refusal">What stopped it.</param>
    internal sealed record StoppedEgress(
        SensitiveContentEgressPoint EgressPoint,
        SensitiveContentEgressRefusal Refusal);
}
