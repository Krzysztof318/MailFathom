// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Application.SensitiveContent.Redaction;

namespace MailFathom.TestSupport;

/// <summary>Records what the egress guard reported, so a test can assert it without a metric listener.</summary>
/// <remarks>
/// Hand-written rather than substituted because what these tests assert is a sequence — which egress points were
/// guarded, in which order, and which of them were refused — and a recorded list reports that without a matcher. It is
/// shared because every boundary holding a guarded egress point needs the same one.
/// </remarks>
internal sealed class RecordingSensitiveContentEgressTelemetry : ISensitiveContentEgressTelemetry
{
    private readonly List<GuardedEgress> guarded = [];
    private readonly List<RefusedEgress> refused = [];

    /// <summary>Gets every text that passed a guard, in the order the guards ran.</summary>
    public IReadOnlyList<GuardedEgress> Guarded => this.guarded;

    /// <summary>Gets every egress a scanner refused, in the order the refusals happened.</summary>
    public IReadOnlyList<RefusedEgress> Refused => this.refused;

    /// <inheritdoc />
    public void RecordGuarded(SensitiveContentEgressPoint egressPoint, RedactedText redacted, TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(redacted);

        this.guarded.Add(new GuardedEgress(egressPoint, redacted, elapsed));
    }

    /// <inheritdoc />
    public void RecordRefused(SensitiveContentEgressPoint egressPoint, SensitiveContentScannerKind scanner) =>
        this.refused.Add(new RefusedEgress(egressPoint, scanner));

    /// <summary>One text that passed a guard.</summary>
    /// <param name="EgressPoint">Where it was going.</param>
    /// <param name="Redacted">What the redaction produced.</param>
    /// <param name="Elapsed">What the scan added to the operation.</param>
    internal sealed record GuardedEgress(
        SensitiveContentEgressPoint EgressPoint,
        RedactedText Redacted,
        TimeSpan Elapsed);

    /// <summary>One egress a scanner refused.</summary>
    /// <param name="EgressPoint">Where the text was going, and did not.</param>
    /// <param name="Scanner">The scanner that could not answer.</param>
    internal sealed record RefusedEgress(SensitiveContentEgressPoint EgressPoint, SensitiveContentScannerKind Scanner);
}
