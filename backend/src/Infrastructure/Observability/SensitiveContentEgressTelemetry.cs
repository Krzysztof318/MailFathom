// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics;
using System.Diagnostics.Metrics;
using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Application.SensitiveContent.Redaction;
using MailFathom.Common.Observability;
using MailFathom.Domain.Access;

namespace MailFathom.Infrastructure.Observability;

/// <summary>Reports what guarding each egress point found, refused, and cost.</summary>
/// <remarks>
/// <para>
/// The egress point is on every instrument because it is what an operator acts on. "Something was redacted" says
/// nothing; a scanner finding credentials in retrieved extracts and nothing in subjects, or adding two seconds to a
/// listing and nothing to an embedding call, is where a category list or a bound gets changed.
/// </para>
/// <para>
/// Nothing recorded here is mail or derived from it. The tags on the instruments are an egress point, a category name,
/// and a scanner name, all three of them MailFathom's own closed sets, and the values are counts and durations — never
/// a rule's match, a position, a message identity, or any part of what was found, each of which would put the
/// credential in the telemetry written to prove it never left.
/// </para>
/// <para>
/// One further attribute is exported, on the span alone and on no instrument: <c>mailfathom.owner</c>, the deployment's
/// own configured identifier for whoever the published mail belongs to. Postures differ between the people one
/// deployment serves, so a scan nothing attributes cannot be read against what its owner asked for; it names a person
/// no more than a mail account alias does, and it stays off every counter because an identifier on a series
/// incremented once per guarded text would be the unbounded dimension the closed sets above exist to avoid.
/// </para>
/// </remarks>
public sealed class SensitiveContentEgressTelemetry : ISensitiveContentEgressTelemetry
{
    /// <summary>The name one guarded operation opens its span under, beneath whatever asked for the payload.</summary>
    /// <remarks>
    /// One span for the operation rather than one per text, because a body, a subject, and a display name are what a
    /// single read publishes and their sum is what the caller waited for. The instruments beside it stay per value,
    /// which is the level a category list or a bound is decided at.
    /// </remarks>
    internal const string GuardedOperationSpanName = "scan_sensitive_content";

    private const string EgressPointTagName = "mailfathom.sensitive_content.egress_point";
    // The category and the scanner are one dimension apiece across both sensitive-content families, so they are read
    // from the derivation publisher rather than restated here: a dashboard splitting either family by category splits
    // both on the same key, and a second literal would be the second place for it to drift.
    private const string CategoryTagName = SensitiveContentDerivationTelemetry.CategoryTagName;
    private const string ScannerTagName = SensitiveContentDerivationTelemetry.ScannerTagName;

    /// <summary>How many texts one guarded operation scanned, which is what its duration has to be read against.</summary>
    private const string GuardedTextCountTagName = "mailfathom.sensitive_content.texts";

    /// <summary>What the scanner and the category read as on an act stopped because nothing analyzed the whole text.</summary>
    /// <remarks>
    /// A value rather than an omitted tag, because a series with one tag missing is a second series: an operator asking
    /// this counter to break down by scanner would see every length refusal disappear from the answer rather than
    /// appear under a name they can read.
    /// </remarks>
    private const string NotScannedTagValue = "not_scanned";

    /// <summary>Whose mail one guarded operation published, so a scan is read against the posture that person asked for.</summary>
    /// <remarks>
    /// A span attribute and never a metric dimension. Postures differ between the people one deployment serves, so a
    /// scan nothing attributes cannot be read against what its owner asked for; an owner identifier on a counter
    /// incremented once per text would be an unbounded series, which is what every closed tag above exists to avoid.
    /// </remarks>
    private const string OwnerTagName = "mailfathom.owner";

    /// <summary>How the operation ended, which separates a scan that answered from one that could not and one that stopped.</summary>
    private const string OutcomeTagName = "mailfathom.sensitive_content.outcome";

    private const string SucceededOutcomeName = "succeeded";
    private const string RefusedOutcomeName = "refused";
    private const string CancelledOutcomeName = "cancelled";
    private const string FailedOutcomeName = "failed";

    private readonly Counter<long> guardedTextCount;
    private readonly Counter<long> findingCount;
    private readonly Counter<long> omittedCharacterCount;
    private readonly Counter<long> refusalCount;
    private readonly Counter<long> stoppedCount;
    private readonly Histogram<double> scanDuration;

    /// <summary>Initializes the instruments every guarded egress reports through.</summary>
    public SensitiveContentEgressTelemetry()
    {
        this.guardedTextCount = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.sensitive_content.guarded",
            unit: "{text}",
            description: "Texts scanned before egress, whatever followed the scan — including one that stopped the act — by egress point.");
        this.findingCount = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.sensitive_content.findings",
            unit: "{finding}",
            description: "Sensitive-content findings detected before egress — redacted where the point redacts, stopping the act where it screens — by egress point and category.");
        this.omittedCharacterCount = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.sensitive_content.omitted",
            unit: "{character}",
            description: "Characters dropped at the analyzed ceiling rather than trusted unscanned, by egress point.");
        this.refusalCount = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.sensitive_content.refusals",
            unit: "{refusal}",
            description: "Egress operations refused because a switched-on scanner could not answer, by egress point and scanner.");
        this.stoppedCount = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.sensitive_content.stopped",
            unit: "{act}",
            description: "Acts stopped because a screened egress point carried material this deployment will not let leave, by egress point, scanner, and category.");
        this.scanDuration = Telemetry.Meter.CreateHistogram<double>(
            "mailfathom.sensitive_content.scan.duration",
            unit: "s",
            description: "How long scanning added to one guarded operation, by egress point.");
    }

    /// <inheritdoc />
    public void RecordGuarded(SensitiveContentEgressPoint egressPoint, RedactedText redacted, TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(redacted);

        var tags = new TagList { { EgressPointTagName, TagOf(egressPoint) } };

        this.guardedTextCount.Add(1, tags);
        this.scanDuration.Record(elapsed.TotalSeconds, tags);

        // Recorded per category rather than as one total, because which kind of material a mailbox is producing is what
        // decides whether a category list is right — and a total says only that the feature is switched on.
        foreach (var category in redacted.Findings.GroupBy(finding => finding.Category.Name, StringComparer.Ordinal))
        {
            this.findingCount.Add(
                category.Count(),
                new TagList
                {
                    { EgressPointTagName, TagOf(egressPoint) },
                    { CategoryTagName, category.Key },
                });
        }

        // Only when the ceiling actually cut something. A zero on every guarded text would make the series say the
        // ceiling is in play on ordinary mail, which is exactly the question this instrument exists to answer.
        if (redacted.OmittedCharacterCount > 0)
        {
            this.omittedCharacterCount.Add(redacted.OmittedCharacterCount, tags);
        }
    }

    /// <inheritdoc />
    public void RecordRefused(SensitiveContentEgressPoint egressPoint, SensitiveContentScannerKind scanner) =>
        this.refusalCount.Add(
            1,
            new TagList
            {
                { EgressPointTagName, TagOf(egressPoint) },
                { ScannerTagName, SensitiveContentDerivationTelemetry.TagOf(scanner) },
            });

    /// <inheritdoc />
    public void RecordStopped(SensitiveContentEgressPoint egressPoint, SensitiveContentEgressRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);

        var tags = new TagList { { EgressPointTagName, TagOf(egressPoint) } };

        // Both tags are written whatever the reason, and a ceiling refusal writes the same literal into each. An absent
        // tag would make the two reasons two shapes of the same series, so a query summing the counter by scanner would
        // quietly drop every act stopped for length.
        tags.Add(
            ScannerTagName,
            refusal.Scanner is { } scanner
                ? SensitiveContentDerivationTelemetry.TagOf(scanner)
                : NotScannedTagValue);
        tags.Add(CategoryTagName, refusal.Category?.Name ?? NotScannedTagValue);

        this.stoppedCount.Add(1, tags);
    }

    /// <inheritdoc />
    public ISensitiveContentGuardScope BeginGuardedOperation(
        SensitiveContentEgressPoint egressPoint,
        MailOwnerId owner,
        CancellationToken cancellationToken)
    {
        var activity = Telemetry.ActivitySource.StartActivity(GuardedOperationSpanName);
        activity?.SetTag(EgressPointTagName, TagOf(egressPoint));

        // Written only where an owner was resolved, which is every scanning flow: a deployment scanning nobody opens no
        // operation at all, so an unset value here is a flow that reached this before it established whose mail it holds
        // and an empty attribute reads more honestly than a zero UUID.
        if (owner.IsSpecified)
        {
            activity?.SetTag(OwnerTagName, owner.Value.ToString());
        }

        return new GuardedOperation(activity, cancellationToken);
    }

    /// <summary>Names an egress point as a tag value.</summary>
    /// <remarks>
    /// A closed mapping rather than the member's own name, for the reason every published mapping here is closed: the
    /// tag value is what a dashboard and an alert are written against, so a member added without one has to fail rather
    /// than silently rename a series.
    /// </remarks>
    private static string TagOf(SensitiveContentEgressPoint egressPoint) => egressPoint switch
    {
        SensitiveContentEgressPoint.ChatPrompt => "chat_prompt",
        SensitiveContentEgressPoint.HostedEmbeddingInput => "hosted_embedding_input",
        SensitiveContentEgressPoint.McpSnippet => "mcp_snippet",
        SensitiveContentEgressPoint.McpEmailContent => "mcp_email_content",
        SensitiveContentEgressPoint.OutgoingMail => "outgoing_mail",
        SensitiveContentEgressPoint.ClientMailListing => "client_mail_listing",
        SensitiveContentEgressPoint.ClientMailSearch => "client_mail_search",
        _ => "unknown",
    };

    /// <summary>Carries one guarded operation from the span that opens it to the count and the ending that close it.</summary>
    /// <remarks>
    /// <para>
    /// The count is written at the end rather than as each text arrives, because a tag set repeatedly is a tag rewritten
    /// repeatedly on a span nothing has read yet.
    /// </para>
    /// <para>
    /// Four endings rather than two, because every way a scan stops leaves through the same disposal. A refusal is not
    /// an error status, for the reason the refusal is not a defect: a scanner that could not answer stopped an egress on
    /// purpose, and the operation the caller sees fails with an error code of its own. A shutdown is cancelled and
    /// carries no error either. What is left — an operation that neither finished nor was stopped — is the scanner
    /// having faulted, and reporting that as a success is what would rule the scanner out of an investigation it
    /// belongs in.
    /// </para>
    /// </remarks>
    private sealed class GuardedOperation(Activity? activity, CancellationToken cancellationToken)
        : ISensitiveContentGuardScope
    {
        private int guardedTextCount;
        private bool refused;
        private bool completed;

        // Counted atomically because the operation is the unit and the values inside it are not: a consumer that
        // guards the fields of one payload concurrently would otherwise publish a count lower than what it scanned.
        public void TextGuarded() => Interlocked.Increment(ref this.guardedTextCount);

        public void Refused() => this.refused = true;

        public void Completed() => this.completed = true;

        public void Dispose()
        {
            if (activity is null)
            {
                return;
            }

            var outcome = this.OutcomeName();

            activity.SetTag(GuardedTextCountTagName, Volatile.Read(ref this.guardedTextCount));
            activity.SetTag(OutcomeTagName, outcome);
            activity.SetStatus(outcome == FailedOutcomeName ? ActivityStatusCode.Error : ActivityStatusCode.Ok);
            activity.Dispose();
        }

        /// <summary>Reads which of the four endings this operation reached.</summary>
        /// <remarks>
        /// A refusal is read before completion because it is the stronger fact: it stopped the egress, whatever the
        /// consumer went on to report.
        /// </remarks>
        private string OutcomeName()
        {
            if (this.refused)
            {
                return RefusedOutcomeName;
            }

            if (this.completed)
            {
                return SucceededOutcomeName;
            }

            return cancellationToken.IsCancellationRequested ? CancelledOutcomeName : FailedOutcomeName;
        }
    }
}
