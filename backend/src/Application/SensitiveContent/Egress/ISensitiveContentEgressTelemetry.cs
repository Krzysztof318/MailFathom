// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent.Redaction;
using MailFathom.Domain.Access;

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

    /// <summary>Records one act stopped because a screened egress point carried material the deployment will not let leave.</summary>
    /// <param name="egressPoint">Where the text was about to go, and did not.</param>
    /// <param name="refusal">What stopped it, which names a scanner and a category or says the text outran the ceiling.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="refusal" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A separate instrument from the one above, because the two are opposite facts about a deployment. A scan that
    /// could not run says the analyzer is down and nothing about the mail; a scan that stopped an act says the scanner
    /// is working and somebody tried to send something. Counting them together would make an outage read as a mailbox
    /// full of credentials, and a mailbox full of credentials read as an outage.
    /// </remarks>
    void RecordStopped(SensitiveContentEgressPoint egressPoint, SensitiveContentEgressRefusal refusal);

    /// <summary>Opens the report of one guarded operation, which is what a caller actually waits on.</summary>
    /// <param name="egressPoint">Where the texts this operation guards are going.</param>
    /// <param name="owner">Whose mail this operation is publishing, which the span records so a scan is attributable.</param>
    /// <param name="cancellationToken">The caller's token, read as the scope is disposed to tell a shutdown from an operation that broke.</param>
    /// <returns>The scope, which the caller must dispose exactly once and inside which the scanning happens.</returns>
    /// <remarks>
    /// <para>
    /// The instruments above answer over every guarded text of a deployment; this answers what one operation cost the
    /// caller in front of it. Both are needed and neither substitutes for the other: a percentile over values says a
    /// scan is quick while a read that ran fifty of them was not.
    /// </para>
    /// <para>
    /// The owner is here and on none of the instruments above, because postures now differ between the people one
    /// deployment serves and a scan that cannot be attributed to one of them cannot be read against what that person
    /// asked for. It is a span attribute rather than a metric dimension for the reason every tag above is a closed set:
    /// an identifier on a counter incremented per text is an unbounded series, while a span already carries the one
    /// operation it describes. The identifier is an opaque UUID and is not mail.
    /// </para>
    /// </remarks>
    ISensitiveContentGuardScope BeginGuardedOperation(
        SensitiveContentEgressPoint egressPoint,
        MailOwnerId owner,
        CancellationToken cancellationToken);
}
