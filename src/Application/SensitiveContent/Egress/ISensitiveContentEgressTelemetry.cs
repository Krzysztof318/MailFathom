// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent.Redaction;

namespace MailFathom.Application.SensitiveContent.Egress;

/// <summary>Reports what guarding one egress point found and what it cost, without reporting any of it.</summary>
/// <remarks>
/// <para>
/// The three questions an operator has about a switched-on scanner are whether it is finding anything, whether it is
/// refusing anything, and what it is adding to a response. All three are answered per egress point, because a scanner
/// that is quick on a subject and slow on a retrieved extract is one fact rather than two deployments.
/// </para>
/// <para>
/// <b>Nothing recorded through this port is mail or derived from it.</b> A category name, a rule name, a detector
/// identity, and an egress point are MailFathom's own closed sets; a count, a character count, and a duration are
/// numbers. The detected value, the text it sat in, and the position it sat at are none of an instrument's business —
/// recording them would put the credential in the telemetry written to prove it never left.
/// </para>
/// </remarks>
public interface ISensitiveContentEgressTelemetry
{
    /// <summary>Records one text that passed a guard, whatever the guard had to remove from it.</summary>
    /// <param name="egressPoint">Where the text was about to go.</param>
    /// <param name="redacted">What the redaction produced, read for its findings and its dropped remainder.</param>
    /// <param name="elapsed">How long the scan added to the operation being guarded.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="redacted" /> is <see langword="null" />.</exception>
    void RecordGuarded(SensitiveContentEgressPoint egressPoint, RedactedText redacted, TimeSpan elapsed);

    /// <summary>Records one egress refused because the scanner guarding it could not answer.</summary>
    /// <param name="egressPoint">Where the text was about to go, and did not.</param>
    /// <param name="scanner">Which switched-on scanner could not answer.</param>
    void RecordRefused(SensitiveContentEgressPoint egressPoint, SensitiveContentScannerKind scanner);
}
